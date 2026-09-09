using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SIAP.Api.Common;
using SIAP.Api.Data;
using SIAP.Api.DTOs.Chat;
using SIAP.Api.Entities;
using SIAP.Api.Hubs;
using SIAP.Api.Repositories.Interfaces;
using SIAP.Api.Services.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace SIAP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly IGroqService _groqService;
    private readonly IDocumentRepository _repository;
    private readonly AppDbContext _dbContext;
    private readonly IEmbeddingService _embeddingService;
    private readonly IGoogleDriveService _driveService;
    private readonly IHubContext<AppHub> _hubContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IGroqService groqService,
        IDocumentRepository repository,
        AppDbContext dbContext,
        IEmbeddingService embeddingService,
        IGoogleDriveService driveService,
        IHubContext<AppHub> hubContext,
        IConfiguration configuration,
        ILogger<ChatController> logger)
    {
        _groqService = groqService;
        _repository = repository;
        _dbContext = dbContext;
        _embeddingService = embeddingService;
        _driveService = driveService;
        _hubContext = hubContext;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("Models")]
    public IActionResult GetModels()
    {
        var textModel = _configuration["Llm:Model"] ?? _configuration["Llm:Model2"] ?? "openai/gpt-oss-120b";
        var visionModel = _configuration["Llm:Image"] ?? _configuration["Llm:image2"] ?? "qwen/qwen3.6-27b";

        var models = new[]
        {
            new {
                Id = "auto",
                Name = "Auto (Hybrid Teks & Vision)",
                Tag = "Rekomendasi",
                Icon = "fa-wand-magic-sparkles",
                Description = "Deteksi gambar otomatis jika ada, dengan respon analisis dokumen komprehensif",
                ModelName = $"{textModel} / {visionModel}"
            },
            new {
                Id = "text",
                Name = "Model Teks (Penalaran)",
                Tag = "Cepat & Luas",
                Icon = "fa-file-lines",
                Description = "Analisis teks laporan kerja, tabel kegiatan, dan perbandingan bulanan",
                ModelName = textModel
            },
            new {
                Id = "vision",
                Name = "Model Vision (Penglihatan)",
                Tag = "Visual & Kode",
                Icon = "fa-eye",
                Description = "Fokus membaca gambar dokumen, screenshot IDE/kode, dan teks antarmuka",
                ModelName = visionModel
            }
        };

        return Ok(ApiResponse<object>.Ok(new {
            CurrentTextModel = textModel,
            CurrentVisionModel = visionModel,
            Options = models
        }, "Berhasil"));
    }

    [HttpGet("Sessions")]
    public async Task<IActionResult> GetSessions()
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            return Unauthorized(ApiResponse<object>.Gagal("User not found"));

        var sessions = await _dbContext.ChatSessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new {
                s.Id,
                s.Title,
                s.CreatedAt
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(sessions, "Berhasil"));
    }

    [HttpGet("Sessions/{sessionId}")]
    public async Task<IActionResult> GetSessionDetails(Guid sessionId)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            return Unauthorized(ApiResponse<object>.Gagal("User not found"));

        var session = await _dbContext.ChatSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);

        if (session == null)
            return NotFound(ApiResponse<object>.Gagal("Sesi tidak ditemukan"));

        var messages = session.Messages.OrderBy(m => m.CreatedAt).Select(m => new {
            m.Id,
            m.Role,
            m.Content,
            Sources = string.IsNullOrEmpty(m.Sources) ? null : System.Text.Json.JsonSerializer.Deserialize<object>(m.Sources),
            m.CreatedAt
        });

        return Ok(ApiResponse<object>.Ok(new {
            session.Id,
            session.Title,
            Messages = messages
        }, "Berhasil"));
    }

    [HttpDelete("Sessions/{sessionId}")]
    public async Task<IActionResult> DeleteSession(Guid sessionId)
    {
        var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
            return Unauthorized(ApiResponse<object>.Gagal("User not found"));

        var session = await _dbContext.ChatSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);

        if (session == null)
            return NotFound(ApiResponse<object>.Gagal("Sesi tidak ditemukan"));

        _dbContext.ChatSessions.Remove(session);
        await _dbContext.SaveChangesAsync();

        await _hubContext.Clients.All.SendAsync("ChatSessionDeleted", new { SessionId = sessionId, UserId = userId });

        return Ok(ApiResponse<object?>.Ok(null, "Sesi berhasil dihapus"));
    }

    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] ChatRequestDto request)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
                return Unauthorized(ApiResponse<object>.Gagal("User not found"));

            ChatSession session;
            
            if (request.SessionId.HasValue)
            {
                session = await _dbContext.ChatSessions
                    .Include(s => s.Messages)
                    .FirstOrDefaultAsync(s => s.Id == request.SessionId.Value && s.UserId == userId)
                    ?? throw new Exception("Sesi chat tidak ditemukan.");
            }
            else
            {
                // Create a new session with title derived from the first message
                var title = request.Message.Length > 50 ? request.Message.Substring(0, 47) + "..." : request.Message;
                session = new ChatSession
                {
                    UserId = userId,
                    Title = title
                };
                _dbContext.ChatSessions.Add(session);
            }

            // 1. Rewrite and extract optimal search keywords
            var searchQuery = request.Message;
            try
            {
                var historyText = (session.Messages != null && session.Messages.Any())
                    ? string.Join("\n", session.Messages.OrderBy(m => m.CreatedAt).TakeLast(6).Select(m => $"{m.Role}: {m.Content}"))
                    : "Tidak ada riwayat sebelumnya.";

                var rewritePrompt = @"Anda adalah mesin ekstraksi kata kunci pencarian laporan kerja tenaga ahli.
Tugas Anda: Dari pertanyaan pengguna dan riwayat percakapan (jika ada), rumuskan 3-6 kata kunci pencarian paling penting untuk mencari potongan laporan yang tepat di database.
PANDUAN:
1. Normalisasi kata typo / majemuk (misal: 'ditanggal 5 mei' -> '5 Mei', 'kerjaan firman' -> 'Firman').
2. Ambil entitas kunci: Nama tenaga ahli, Tanggal / Periode (misal: '5 Mei', 'Mei 2026', 'Juni'), serta topik / kata kerja kegiatan (misal: 'backup database', 'pengembangan', 'sipandu', 'laporan').
3. Buang kata tanya dan basa-basi ('apa yang dikerjakan', 'tolong carikan', 'kapan', 'gimana').
4. HANYA berikan kata-kata kunci pencarian dipisahkan spasi tanpa tanda kutip atau penjelasan tambahan.";

                var userRewritePrompt = $"Riwayat:\n{historyText}\n\nuser: {request.Message}\nKata Kunci:";
                var rewritten = await _groqService.GetChatCompletionAsync(rewritePrompt, userRewritePrompt);
                if (!string.IsNullOrWhiteSpace(rewritten) && rewritten.Length <= 150)
                {
                    searchQuery = rewritten.Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to rewrite search query. Using raw message.");
                searchQuery = request.Message;
            }
            
            var dbUser = await _dbContext.Users.Include(u => u.Role).Include(u => u.Bidang).FirstOrDefaultAsync(u => u.Id == userId);
            if (dbUser == null) return Unauthorized(ApiResponse<object>.Gagal("Pengguna tidak ditemukan."));

            var isSuperAdmin = dbUser.Role.Name.Equals("admin", StringComparison.OrdinalIgnoreCase);
            if (!isSuperAdmin && !dbUser.IsApproved)
            {
                return StatusCode(403, ApiResponse<object>.Gagal("Akun Anda sedang menunggu persetujuan dari Admin/Kasubag dan penentuan bidang."));
            }

            var topK = request.TopK <= 0 ? 12 : request.TopK;
            var embedding = await _embeddingService.GenerateEmbeddingAsync(searchQuery);

            var results = await _repository.SearchHybridAsync(searchQuery, embedding, topK, userId, dbUser.BidangId, isSuperAdmin);

            var docIds = results.Where(chunk => chunk.Document != null).Select(chunk => chunk.DocumentId).Distinct().ToList();

            var allDocImages = await _dbContext.DocumentImages
                .Where(di => docIds.Contains(di.DocumentId))
                .OrderBy(di => di.PageNumber)
                .ToListAsync();

            var chunkContents = results.Select(c => c.Content.ToLowerInvariant()).ToList();

            // Extract query tokens & date patterns for accurate image association
            var queryLower = (request.Message + " " + searchQuery).ToLowerInvariant();
            var dateMatch = System.Text.RegularExpressions.Regex.Match(queryLower, @"\b(\d{1,2})\s*(januari|februari|maret|april|mei|juni|juli|agustus|september|oktober|november|desember)\b");
            string targetDay = dateMatch.Success ? dateMatch.Groups[1].Value : "";
            string targetMonth = dateMatch.Success ? dateMatch.Groups[2].Value : "";
            string dateRegexPattern = dateMatch.Success ? $@"\b0?{targetDay}\s+{targetMonth}\b" : "";

            var queryTokens = searchQuery.ToLowerInvariant()
                .Split(new[] { ' ', ',', '.', ';', ':', '-', '/', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 2 && t != "apa" && t != "yang" && t != "dikerjakan" && t != "pada" && t != "tanggal" && t != "ditanggal")
                .ToList();

            var scoredImages = allDocImages.Select(img => {
                int score = 0;
                var captionLower = (img.Caption ?? "").ToLowerInvariant();
                var contextLower = (img.ContextText ?? "").ToLowerInvariant();

                // 1. Date matching (highest weight)
                if (!string.IsNullOrEmpty(dateRegexPattern))
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(captionLower, dateRegexPattern))
                    {
                        score += 150;
                    }
                    else if (System.Text.RegularExpressions.Regex.IsMatch(contextLower, dateRegexPattern))
                    {
                        score += 80;
                    }
                    else
                    {
                        // If user asks for a specific date (e.g. 5 mei) and image has a DIFFERENT date, penalize heavily
                        if (System.Text.RegularExpressions.Regex.IsMatch(captionLower, @"\b\d{1,2}\s*(januari|februari|maret|april|mei|juni|juli|agustus|september|oktober|november|desember)\b") ||
                            System.Text.RegularExpressions.Regex.IsMatch(contextLower, @"\b\d{1,2}\s*(januari|februari|maret|april|mei|juni|juli|agustus|september|oktober|november|desember)\b"))
                        {
                            score -= 300;
                        }
                    }
                }

                // 2. Query terms matching
                foreach (var term in queryTokens)
                {
                    if (captionLower.Contains(term)) score += 15;
                    else if (contextLower.Contains(term)) score += 5;
                }

                // 3. Match against retrieved chunk contents
                foreach (var chunkText in chunkContents)
                {
                    if (!string.IsNullOrWhiteSpace(img.Caption) && chunkText.Contains(captionLower))
                    {
                        score += 30;
                        break;
                    }
                }

                // 4. Penalize cover and evaluation forms
                if (img.PageNumber <= 1 || captionLower.Contains("penilaian") || captionLower.Contains("hasil kegiatan bulan"))
                    score -= 50;

                return new { Image = img, Score = score };
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Image)
            .ToList();

            var relevantImages = scoredImages.Take(2).ToList();

            // Extract any images already referenced/shown in previous messages, prioritizing the MOST RECENT message first
            var historyImageFileIds = new List<string>();
            if (session.Messages != null && session.Messages.Any())
            {
                foreach (var msg in session.Messages.OrderByDescending(m => m.CreatedAt).Take(6))
                {
                    if (!string.IsNullOrEmpty(msg.Content))
                    {
                        var matches = System.Text.RegularExpressions.Regex.Matches(msg.Content, @"/api/Documents/images/([a-zA-Z0-9_\-]+)");
                        foreach (System.Text.RegularExpressions.Match m in matches)
                        {
                            var fid = m.Groups[1].Value;
                            if (!historyImageFileIds.Contains(fid))
                            {
                                historyImageFileIds.Add(fid);
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(msg.Sources))
                    {
                        var matches = System.Text.RegularExpressions.Regex.Matches(msg.Sources, @"/api/Documents/images/([a-zA-Z0-9_\-]+)");
                        foreach (System.Text.RegularExpressions.Match m in matches)
                        {
                            var fid = m.Groups[1].Value;
                            if (!historyImageFileIds.Contains(fid))
                            {
                                historyImageFileIds.Add(fid);
                            }
                        }
                    }
                }
            }

            // If user asks a follow-up about an image or if search found 0 images but history has them
            var isImageFollowUp = System.Text.RegularExpressions.Regex.IsMatch(queryLower, @"\b(gambar|foto|citra|orang|berapa|wanita|pria|laki|cewek|cowok|tampak|terlihat|lihat|zoom|tampilan|screenshot|ss|itu|tersebut|bahasa|framework|kode|code|file)\b");

            if (historyImageFileIds.Any() && (isImageFollowUp || !relevantImages.Any()))
            {
                var distinctHistoryIds = historyImageFileIds.Take(2).ToList();
                var matchedHistoryImages = new List<DocumentImage>();
                foreach (var hid in distinctHistoryIds)
                {
                    var found = await _dbContext.DocumentImages
                        .FirstOrDefaultAsync(di => di.FilePath != null && di.FilePath.Contains(hid));
                    if (found != null && !matchedHistoryImages.Any(m => m.Id == found.Id))
                    {
                        matchedHistoryImages.Add(found);
                    }
                }
                
                // If user is following up on a previously shown image, prioritize the most recent or date-matched image
                if (isImageFollowUp && matchedHistoryImages.Any())
                {
                    var bestHistory = matchedHistoryImages.FirstOrDefault(img => 
                        (!string.IsNullOrEmpty(dateRegexPattern) && (
                            System.Text.RegularExpressions.Regex.IsMatch(img.Caption ?? "", dateRegexPattern) ||
                            System.Text.RegularExpressions.Regex.IsMatch(img.ContextText ?? "", dateRegexPattern)
                        ))
                    ) ?? matchedHistoryImages[0];

                    var reordered = new List<DocumentImage> { bestHistory };
                    foreach (var hImg in matchedHistoryImages)
                    {
                        if (!reordered.Any(r => r.Id == hImg.Id))
                            reordered.Add(hImg);
                    }
                    foreach (var rImg in relevantImages)
                    {
                        if (!reordered.Any(r => r.Id == rImg.Id))
                            reordered.Add(rImg);
                    }
                    relevantImages = reordered;
                }
                else
                {
                    foreach (var hImg in matchedHistoryImages.AsEnumerable().Reverse())
                    {
                        if (!relevantImages.Any(r => r.Id == hImg.Id))
                        {
                            relevantImages.Insert(0, hImg);
                        }
                    }
                }
            }

            relevantImages = relevantImages.Take(2).ToList();

            // Check if vision is enabled based on user-selected ModelMode ("auto", "vision", "text")
            var isVisionModeAllowed = !string.Equals(request.ModelMode, "text", StringComparison.OrdinalIgnoreCase);

            // Fetch image bytes for Vision AI in parallel
            var visionImages = new List<LlmImageInput>();
            if (isVisionModeAllowed && relevantImages.Any())
            {
                var fetchTasks = relevantImages.Select(async img =>
                {
                    try
                    {
                        byte[]? bytes = null;
                        var displayPath = img.FilePath ?? string.Empty;

                        if (displayPath.StartsWith("https://drive.google.com/uc?id="))
                        {
                            var fileId = displayPath.Replace("https://drive.google.com/uc?id=", "");
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                            using var stream = await _driveService.DownloadFileAsync(fileId);
                            using var ms = new MemoryStream();
                            await stream.CopyToAsync(ms, cts.Token);
                            bytes = ms.ToArray();
                        }
                        else if (displayPath.StartsWith("/api/Documents/images/"))
                        {
                            var fileId = displayPath.Replace("/api/Documents/images/", "");
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                            using var stream = await _driveService.DownloadFileAsync(fileId);
                            using var ms = new MemoryStream();
                            await stream.CopyToAsync(ms, cts.Token);
                            bytes = ms.ToArray();
                        }
                        else if (!string.IsNullOrWhiteSpace(displayPath))
                        {
                            var localPath = Path.Combine(Directory.GetCurrentDirectory(), displayPath.TrimStart('/', '\\'));
                            if (System.IO.File.Exists(localPath))
                            {
                                bytes = await System.IO.File.ReadAllBytesAsync(localPath);
                            }
                        }

                        if (bytes != null && bytes.Length > 0 && bytes.Length <= 8 * 1024 * 1024)
                        {
                            var optimizedBytes = OptimizeImageForVision(bytes, 900);
                            return new LlmImageInput(optimizedBytes, "image/jpeg", img.Caption, img.FilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to fetch image bytes for Vision AI for image ID {ImageId}", img.Id);
                    }
                    return null;
                });

                var fetched = await Task.WhenAll(fetchTasks);
                visionImages = fetched.Where(v => v != null).Cast<LlmImageInput>().ToList();
            }

            // Limit chunks to top 5 to ensure fast inference and stay well within TPM limits
            var topChunks = results.Take(5).ToList();

            // 2. Construct context from documents
            var contextBuilder = new System.Text.StringBuilder();
            contextBuilder.AppendLine("Berikut adalah potongan dokumen relevan dari database:");
            foreach (var chunk in topChunks)
            {
                var docTitle = chunk.Document?.Nama ?? "Dokumen Tanpa Judul";
                var tenagaAhli = chunk.Document?.NamaTenagaAhli ?? "Tidak Diketahui";
                var bidangNama = chunk.Document?.Bidang?.Nama;
                var bidangInfo = !string.IsNullOrEmpty(bidangNama) ? $" [Bidang: {bidangNama}]" : "";
                contextBuilder.AppendLine($"[Dokumen: {docTitle} | Tenaga Ahli: {tenagaAhli}{bidangInfo}]");
                contextBuilder.AppendLine(chunk.Content);
                contextBuilder.AppendLine("---");
            }

            if (relevantImages.Any())
            {
                contextBuilder.AppendLine("\nGAMBAR DOKUMENTASI DARI DOKUMEN:");
                for (int i = 0; i < relevantImages.Count; i++)
                {
                    var img = relevantImages[i];
                    var captionText = !string.IsNullOrWhiteSpace(img.Caption) ? $" (Keterangan: {img.Caption})" : "";
                    
                    var displayPath = img.FilePath;
                    if (displayPath != null && displayPath.StartsWith("https://drive.google.com/uc?id="))
                    {
                        var fileId = displayPath.Replace("https://drive.google.com/uc?id=", "");
                        displayPath = $"/api/Documents/images/{fileId}";
                    }
                    
                    contextBuilder.AppendLine($"- [Gambar #{i + 1} - Halaman {img.PageNumber}]: {displayPath}{captionText}");
                }
            }

            var systemPrompt = $@"Anda adalah asisten AI dari aplikasi SIPENTA (Sistem Pelaporan Tenaga Ahli).
Tugas Anda adalah membantu pimpinan dan pengguna untuk menelusuri, mengevaluasi, dan menyimpulkan laporan kerja tenaga ahli berdasarkan dokumen yang ada dengan ringkas, teliti, dan akurat.

PANDUAN MENJAWAB:
1. **Pemeriksaan Tanggal, Nama, & Detail Secara Menyeluruh**:
   - Teliti seluruh nama tenaga ahli, tanggal, dan uraian kegiatan yang tercantum pada KONTEKS DOKUMEN di bawah. Perhatikan header [Dokumen: ... | Tenaga Ahli: ...] untuk mengetahui dokumen tersebut milik siapa.
   - Jika pengguna menanyakan kegiatan seseorang (misalnya 'Angel' atau 'Firman'), pastikan Anda membaca header Tenaga Ahli untuk mencocokkannya, walaupun nama tersebut mungkin tidak disebut lagi di dalam teks laporannya.
   - Jika pengguna menanyakan kegiatan pada tanggal atau periode tertentu (misalnya tanggal 5 Mei), telusuri dengan teliti apakah ada kegiatan atau tugas rutin pada tanggal/hari tersebut di dalam dokumen.
2. **Penglihatan Gambar & Deteksi Visual Objektif (Vision AI)**:
   - Anda menerima input visual (gambar/foto asli dokumen) serta daftar pada bagian GAMBAR DOKUMENTASI.
   - **Pemeriksaan Visual Murni & Anti-Bias**: Jika pengguna menanyakan apa yang terlihat di dalam gambar/foto (seperti bahasa pemrograman, framework, nama file, error log, perintah terminal, jumlah orang, tampilan antarmuka, objek):
     * Analisis visual langsung gambar tersebut secara objektif, teliti, dan berbasis fakta yang terlihat di layar/foto.
     * Baca teks dan elemen visual nyata pada gambar: periksa ekstensi file (misal: `.php`, `.js`, `.ts`, `.py`, `.cs`, `.json`), ikon bahasa/file, nama folder (misal: `vendor`, `laravel`, `app/Filament`, `node_modules`), perintah CLI di terminal (misal: `php artisan`, `npm`, `dotnet`), serta tab editor.
     * **Prioritas Bukti Visual Nyata**: Jangan terpengaruh atau berasumsi berdasarkan teks dari tanggal lain atau riwayat chat sebelumnya jika gambar menampilkan hal yang berbeda (contoh: jika dokumen teks di tanggal lain menyebut Next.js, namun screenshot gambar pada kegiatan ini menampilkan file `.php`, folder Laravel, dan perintah `php artisan`, jelaskan secara tepat dan jujur bahwa gambar tersebut menggunakan PHP / Laravel).
   - Jika gambar tersebut relevan dengan penjelasan atau kegiatan yang ditanyakan, sertakan gambar tersebut secara tepat dalam format Markdown:
     `![Deskripsi/Keterangan Foto](<path_dari_daftar_gambar>)`
     (Contoh: `![Foto Dokumentasi Kegiatan](/api/Documents/images/1u-...)` gunakan path URL yang ada di daftar).
   - Jika pengguna bertanya tentang gambar/foto yang baru saja dikirim pada percakapan sebelumnya, gunakan gambar yang disertakan untuk menjawab pertanyaannya secara visual.
   - Jika foto TIDAK relevan dengan apa yang ditanyakan, JANGAN sertakan foto tersebut.
3. **Ringkas, Padat, & Tuntas**:
   - Berikan jawaban yang to the point dan tidak bertele-tele.
   - Ambil inti poin penting dari laporan tenaga ahli.
   - Pastikan setiap kalimat dan bagian jawaban diselesaikan secara utuh sampai tuntas.
4. **Bahasa Profesional & Format Markdown Rapi**:
   - Gunakan gaya bahasa Indonesia yang santun, profesional, dan lugas.
   - Gunakan bullet points (-) atau penomoran untuk merinci progres/poin penting.
   - Tebalkan (**kata kunci / nama / tanggal / status**) agar mudah dibaca cepat.
5. **Kepatuhan Dokumen & Fakta Visual (Anti-Halusinasi)**:
   - Jawaban harus berlandaskan pada KONTEKS DOKUMEN dan bukti visual nyata pada gambar yang diberikan.
   - JANGAN mengarang laporan, progres, atau detail visual di luar apa yang tercantum pada dokumen dan gambar.

KONTEKS DOKUMEN:
{contextBuilder}";

            // 5. Construct compact conversation history for LLM (take last 4 messages to prevent token bloat)
            var llmHistory = new List<object>();
            
            if (session.Messages != null && session.Messages.Any())
            {
                var recentMessages = session.Messages.OrderBy(m => m.CreatedAt).TakeLast(4).ToList();
                foreach (var msg in recentMessages)
                {
                    var cleanContent = msg.Content ?? "";
                    if (cleanContent.Length > 800)
                    {
                        cleanContent = cleanContent.Substring(0, 800) + "...";
                    }
                    llmHistory.Add(new { role = msg.Role, content = (object)cleanContent });
                }
            }
            else if (request.History != null && request.History.Any())
            {
                var recentHistory = request.History.TakeLast(4).ToList();
                foreach (var msg in recentHistory)
                {
                    var cleanContent = msg.Content ?? "";
                    if (cleanContent.Length > 800)
                    {
                        cleanContent = cleanContent.Substring(0, 800) + "...";
                    }
                    llmHistory.Add(new { role = msg.Role, content = (object)cleanContent });
                }
            }
            
            // Add current user message to db
            var userMsg = new ChatMessage
            {
                ChatSession = session,
                Role = "user",
                Content = request.Message
            };
            _dbContext.ChatMessages.Add(userMsg);

            // Pass top 1 vision image to stay safely below Groq 8000 TPM limit
            var finalVisionImages = visionImages.Take(1).ToList();

            // 6. Call LLM with Vision support
            var answer = await _groqService.GetChatCompletionWithVisionAsync(systemPrompt, llmHistory, request.Message, finalVisionImages);

            // 7. Add AI response to db
            var uniqueSources = results
                .Where(chunk => chunk.Document != null)
                .GroupBy(chunk => new { chunk.DocumentId, chunk.Document!.Nama, chunk.Document!.NamaFile })
                .Select(g => new { 
                    DocumentId = g.Key.DocumentId,
                    DocumentTitle = g.Key.Nama,
                    NamaFile = g.Key.NamaFile,
                    NamaTenagaAhli = g.First().Document?.NamaTenagaAhli,
                    PeriodeLaporan = g.First().Document?.PeriodeLaporan,
                    BidangId = g.First().Document?.BidangId,
                    Bidang = g.First().Document?.Bidang?.Nama,
                    Images = relevantImages
                        .Where(img => img.DocumentId == g.Key.DocumentId)
                        .Select(img => new {
                            img.Id,
                            img.PageNumber,
                            img.FileName,
                            Url = img.FilePath,
                            img.Caption,
                            img.Width,
                            img.Height
                        })
                        .ToList()
                })
                .ToList();

            var sourcesJson = System.Text.Json.JsonSerializer.Serialize(uniqueSources);

            var aiMsg = new ChatMessage
            {
                ChatSession = session,
                Role = "assistant",
                Content = answer,
                Sources = sourcesJson
            };
            _dbContext.ChatMessages.Add(aiMsg);

            await _dbContext.SaveChangesAsync();

            await _hubContext.Clients.All.SendAsync("ChatSessionUpdated", new { SessionId = session.Id, UserId = userId });

            return Ok(ApiResponse<object>.Ok(new { 
                SessionId = session.Id,
                Answer = answer, 
                Sources = uniqueSources
            }, "Sukses"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<object>.Gagal(ex.Message));
        }
    }

    private static byte[] OptimizeImageForVision(byte[] inputBytes, int maxDimension = 900)
    {
        try
        {
            using var image = Image.Load(inputBytes);
            if (image.Width > maxDimension || image.Height > maxDimension)
            {
                var options = new ResizeOptions
                {
                    Size = new Size(maxDimension, maxDimension),
                    Mode = ResizeMode.Max
                };
                image.Mutate(x => x.Resize(options));
            }

            using var ms = new MemoryStream();
            image.SaveAsJpeg(ms, new JpegEncoder { Quality = 82 });
            return ms.ToArray();
        }
        catch
        {
            return inputBytes;
        }
    }
}
