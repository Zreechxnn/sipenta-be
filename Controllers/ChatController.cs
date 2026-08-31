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
    private readonly IHubContext<AppHub> _hubContext;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IGroqService groqService,
        IDocumentRepository repository,
        AppDbContext dbContext,
        IEmbeddingService embeddingService,
        IHubContext<AppHub> hubContext,
        ILogger<ChatController> logger)
    {
        _groqService = groqService;
        _repository = repository;
        _dbContext = dbContext;
        _embeddingService = embeddingService;
        _hubContext = hubContext;
        _logger = logger;
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
            var isPrivileged = isSuperAdmin || dbUser.Role.Name.Equals("kasubag", StringComparison.OrdinalIgnoreCase);
            if (!isPrivileged && !dbUser.IsApproved)
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

            var relevantImages = scoredImages.Take(4).ToList();

            // 2. Construct context from documents
            var contextBuilder = new System.Text.StringBuilder();
            contextBuilder.AppendLine("Berikut adalah potongan dokumen relevan dari database:");
            foreach (var chunk in results)
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
                foreach (var img in relevantImages)
                {
                    var captionText = !string.IsNullOrWhiteSpace(img.Caption) ? $" (Keterangan: {img.Caption})" : "";
                    
                    var displayPath = img.FilePath;
                    if (displayPath != null && displayPath.StartsWith("https://drive.google.com/uc?id="))
                    {
                        var fileId = displayPath.Replace("https://drive.google.com/uc?id=", "");
                        displayPath = $"/api/Documents/images/{fileId}";
                    }
                    
                    contextBuilder.AppendLine($"- [Halaman {img.PageNumber}]: {displayPath}{captionText}");
                }
            }

            var systemPrompt = $@"Anda adalah asisten AI dari aplikasi SIPENTA (Sistem Pelaporan Tenaga Ahli).
Tugas Anda adalah membantu pimpinan dan pengguna untuk menelusuri, mengevaluasi, dan menyimpulkan laporan kerja tenaga ahli berdasarkan dokumen yang ada dengan ringkas, teliti, dan akurat.

PANDUAN MENJAWAB:
1. **Pemeriksaan Tanggal, Nama, & Detail Secara Menyeluruh**:
   - Teliti seluruh nama tenaga ahli, tanggal, dan uraian kegiatan yang tercantum pada KONTEKS DOKUMEN di bawah. Perhatikan header [Dokumen: ... | Tenaga Ahli: ...] untuk mengetahui dokumen tersebut milik siapa.
   - Jika pengguna menanyakan kegiatan seseorang (misalnya 'Angel' atau 'Firman'), pastikan Anda membaca header Tenaga Ahli untuk mencocokkannya, walaupun nama tersebut mungkin tidak disebut lagi di dalam teks laporannya.
   - Jika pengguna menanyakan kegiatan pada tanggal atau periode tertentu (misalnya tanggal 5 Mei), telusuri dengan teliti apakah ada kegiatan atau tugas rutin pada tanggal/hari tersebut di dalam dokumen.
2. **Ringkas, Padat, & Tuntas**:
    - Berikan jawaban yang to the point dan tidak bertele-tele.
    - Ambil inti poin penting dari laporan tenaga ahli.
    - Pastikan setiap kalimat dan bagian jawaban diselesaikan secara utuh sampai tuntas.
3. **Bahasa Profesional & Jelas**:
    - Gunakan gaya bahasa Indonesia yang santun, profesional, dan lugas.
4. **Format Markdown Rapi**:
    - Gunakan bullet points (-) atau penomoran untuk merinci progres/poin penting.
    - Tebalkan (**kata kunci / nama / tanggal / status**) agar mudah dibaca cepat.
5. **Gambar / Foto Dokumentasi Kegiatan**:
    - Jika pada bagian GAMBAR DOKUMENTASI terdapat gambar yang relevan dengan kegiatan yang Anda jelaskan, sertakan gambar tersebut dalam format Markdown `![Foto Dokumentasi](<path_yang_diberikan>)` (isikan path URL-nya secara langsung di dalam kurung dari bagian GAMBAR DOKUMENTASI).
6. **Kepatuhan Dokumen (Anti-Halusinasi)**:
    - Jawaban harus berlandaskan pada KONTEKS DOKUMEN di bawah.
    - JANGAN mengarang laporan, progres, atau pencapaian di luar dokumen.

KONTEKS DOKUMEN:
{contextBuilder}";

            // 5. Construct conversation history for LLM
            var llmMessages = new List<object>();
            
            // Add previous history from database
            if (session.Messages != null && session.Messages.Any())
            {
                foreach (var msg in session.Messages.OrderBy(m => m.CreatedAt))
                {
                    llmMessages.Add(new { role = msg.Role, content = msg.Content });
                }
            }
            else if (request.History != null && request.History.Any())
            {
                // Fallback to request history if db is empty (for backward compatibility if needed)
                foreach (var msg in request.History)
                {
                    llmMessages.Add(new { role = msg.Role, content = msg.Content });
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
            
            llmMessages.Add(new { role = "user", content = request.Message });

            // 6. Call LLM
            var answer = await _groqService.GetChatCompletionWithHistoryAsync(systemPrompt, llmMessages);

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
}
