using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using SIAP.Api.Entities;
using SIAP.Api.Repositories.Interfaces;
using SIAP.Api.Services.Interfaces;
using SIAP.Api.Services.Parsers;
using SIAP.Api.Services.Chunking.Interfaces;
using SIAP.Api.Hubs;

namespace SIAP.Api.Services.Implemenations;

public class DocumentProcessingService : BackgroundService
{
    private readonly ILogger<DocumentProcessingService> _logger;
    private readonly IDocumentProcessingQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<AppHub> _hubContext;

    public DocumentProcessingService(
        ILogger<DocumentProcessingService> logger,
        IDocumentProcessingQueue queue,
        IServiceProvider serviceProvider,
        IHubContext<AppHub> hubContext)
    {
        _logger = logger;
        _queue = queue;
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DocumentProcessingService is running.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var documentId = await _queue.DequeueAsync(stoppingToken);

                _logger.LogInformation("Document {DocumentId} queued for processing", documentId);

                await ProcessDocumentAsync(documentId, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Prevent throwing if stoppingToken was signaled
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching document from queue.");
            }
        }
    }

    private async Task ProcessDocumentAsync(Guid documentId, CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var parserFactory = scope.ServiceProvider.GetRequiredService<IDocumentParserFactory>();

        var detectionService = scope.ServiceProvider.GetRequiredService<IDocumentDetectionService>();
        var ocrProvider = scope.ServiceProvider.GetRequiredService<IOcrProvider>();
        var driveService = scope.ServiceProvider.GetRequiredService<IGoogleDriveService>();

        var document = await repository.GetByIdAsync(documentId);
        if (document == null)
        {
            _logger.LogWarning("Document {DocumentId} not found in database", documentId);
            return;
        }

        var stopWatch = Stopwatch.StartNew();
        document.ProcessingStartedAt = DateTime.UtcNow;
        document.Status = DocumentStatus.Processing;
        if (document.Content != null)
        {
            document.Content.ParseStatus = ParseStatus.Parsing;
        }

        try
        {
            await repository.UpdateAsync(document);
            _logger.LogInformation("Processing started for document {DocumentId}", documentId);

            // Broadcast status change to Processing using clean DTO payload
            try
            {
                await _hubContext.Clients.All.SendAsync("DocumentUpdated", MapToDtoPayload(document));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send SignalR processing status for document {DocumentId}", documentId);
            }

            using var fileStream = await driveService.DownloadFileAsync(document.Path);
            var extension = Path.GetExtension(document.NamaFile);
            
            var detectionResult = await detectionService.DetectAsync(fileStream, extension, document.MimeType);
            fileStream.Position = 0; // Reset stream

            if (document.Content != null)
            {
                document.Content.HasTextLayer = detectionResult.HasTextLayer;
                document.Content.IsScannedDocument = detectionResult.IsScannedDocument;
            }

            if (detectionResult.HasTextLayer && !detectionResult.IsImage)
            {
                var parser = parserFactory.GetParser(extension, document.MimeType);
                if (parser == null)
                {
                    throw new Exception("No parser found for the given file type.");
                }

                var parsedResult = await parser.ParseAsync(fileStream);

                if (document.Content != null)
                {
                    document.Content.RawText = parsedResult.RawText;
                    document.Content.PageCount = parsedResult.PageCount;
                    document.Content.Language = parsedResult.Language;
                    document.Content.ParseStatus = ParseStatus.Completed;
                    document.Content.ParsedAt = DateTime.UtcNow;
                    document.Content.OcrStatus = OcrStatus.Skipped;
                }
                
                _logger.LogInformation("OCR skipped for document {DocumentId}", documentId);
            }
            else
            {
                if (document.Content != null)
                {
                    document.Content.OcrStatus = OcrStatus.Processing;
                    document.Content.OcrStartedAt = DateTime.UtcNow;
                }
                await repository.UpdateAsync(document);

                _logger.LogInformation("OCR started for document {DocumentId}", documentId);
                var ocrWatch = Stopwatch.StartNew();

                var ocrResult = await ocrProvider.ExtractTextAsync(fileStream, stoppingToken);

                ocrWatch.Stop();
                if (ocrResult.Success)
                {
                    _logger.LogInformation("[OCR] Success for document {DocumentId} in {DurationMs} ms", documentId, ocrWatch.ElapsedMilliseconds);
                    if (document.Content != null)
                    {
                        document.Content.RawText = ocrResult.RawText;
                        document.Content.OcrStatus = OcrStatus.Completed;
                        document.Content.OcrFinishedAt = DateTime.UtcNow;
                        document.Content.OcrDuration = ocrWatch.Elapsed;
                        document.Content.OcrConfidence = ocrResult.Confidence;
                        document.Content.OcrEngine = "Tesseract";
                        
                        document.Content.ParseStatus = ParseStatus.Completed;
                        document.Content.ParsedAt = DateTime.UtcNow;
                    }
                }
                else
                {
                    _logger.LogError("[OCR] Failed for document {DocumentId}: {Error}", documentId, ocrResult.ErrorMessage);
                    if (document.Content != null)
                    {
                        document.Content.OcrStatus = OcrStatus.Failed;
                        document.Content.ParseStatus = ParseStatus.Failed;
                    }
                    document.ErrorMessage = ocrResult.ErrorMessage;
                    document.Status = DocumentStatus.Failed;
                }
            }

            if (document.Status != DocumentStatus.Failed)
            {
                // 1. Try Extract/enhance Metadata using LLM first for high-accuracy typo correction & clean titles
                try 
                {
                    await ExtractMetadataWithLlmAsync(document, scope.ServiceProvider, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract metadata with LLM for document {DocumentId}", documentId);
                }

                // 2. Extract Images from PDF using iText 7
                if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var imageExtractor = scope.ServiceProvider.GetRequiredService<IPdfImageExtractor>();
                        var dbContext = scope.ServiceProvider.GetRequiredService<SIAP.Api.Data.AppDbContext>();
                        
                        fileStream.Position = 0;
                        var extractedImages = await imageExtractor.ExtractImagesAsync(fileStream);

                        if (extractedImages.Any())
                        {
                            var imagesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", "Images", document.Id.ToString());
                            if (!Directory.Exists(imagesDirectory))
                            {
                                Directory.CreateDirectory(imagesDirectory);
                            }

                            // Clean old images if any
                            var oldImages = dbContext.DocumentImages.Where(di => di.DocumentId == document.Id);
                            dbContext.DocumentImages.RemoveRange(oldImages);

                            int imgIndex = 1;
                            foreach (var img in extractedImages)
                            {
                                var imgFileName = $"p{img.PageNumber}_{imgIndex}_{Guid.NewGuid():N}.{img.Extension}";
                                var imgFullPath = Path.Combine(imagesDirectory, imgFileName);
                                await File.WriteAllBytesAsync(imgFullPath, img.ImageBytes, stoppingToken);

                                var relativeUrl = $"/uploads/images/{document.Id}/{imgFileName}";

                                var docImage = new DocumentImage
                                {
                                    Id = Guid.NewGuid(),
                                    DocumentId = document.Id,
                                    PageNumber = img.PageNumber,
                                    FileName = imgFileName,
                                    FilePath = relativeUrl,
                                    MimeType = img.MimeType,
                                    Width = img.Width,
                                    Height = img.Height,
                                    FileSize = img.FileSize,
                                    Caption = img.Caption,
                                    ContextText = img.PageText,
                                    CreatedAt = DateTime.UtcNow
                                };

                                dbContext.DocumentImages.Add(docImage);
                                imgIndex++;
                            }

                            await dbContext.SaveChangesAsync(stoppingToken);
                            _logger.LogInformation("Successfully extracted {Count} images from document {DocumentId} via iText 7.", extractedImages.Count, documentId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to extract images via iText 7 for document {DocumentId}", documentId);
                    }
                }

                // 3. Fallback/enrich any remaining empty fields with deterministic NLP/regex rules
                if (document.Content != null && !string.IsNullOrWhiteSpace(document.Content.RawText))
                {
                    SIAP.Api.Common.DocumentHelper.EnrichDocumentMetadata(document, document.Content.RawText);
                }

                // 4. Chunking
                var chunkService = scope.ServiceProvider.GetRequiredService<IChunkService>();
                await chunkService.ProcessChunksAsync(document.Id, null, stoppingToken);

                // Re-fetch document if it was modified by Chunking strategy
                document = await repository.GetByIdAsync(documentId) ?? document;

                if (string.IsNullOrWhiteSpace(document.NamaTenagaAhli)) document.NamaTenagaAhli = "-";
                if (string.IsNullOrWhiteSpace(document.JenisDokumen)) document.JenisDokumen = "Laporan Kerja";
                document.Status = DocumentStatus.Parsed;
                document.ProcessingFinishedAt = DateTime.UtcNow;
                stopWatch.Stop();
                document.ProcessingDuration = stopWatch.Elapsed;
            }
            else
            {
                stopWatch.Stop();
                document.ProcessingFinishedAt = DateTime.UtcNow;
                document.ProcessingDuration = stopWatch.Elapsed;
            }

            await repository.UpdateAsync(document);
            _logger.LogInformation("Processing completed for document {DocumentId} in {DurationMs} ms", documentId, stopWatch.ElapsedMilliseconds);

            // Broadcast real-time update using clean DTO payload (Metadata auto-generated!)
            try
            {
                await _hubContext.Clients.All.SendAsync("DocumentUpdated", MapToDtoPayload(document));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast DocumentUpdated SignalR notification for document {DocumentId}", documentId);
            }
        }
        catch (Exception ex)
        {
            stopWatch.Stop();
            _logger.LogError(ex, "Processing failed for document {DocumentId}", documentId);
            
            document.Status = DocumentStatus.Failed;
            document.ProcessingFinishedAt = DateTime.UtcNow;
            document.ProcessingDuration = stopWatch.Elapsed;
            document.ErrorMessage = ex.Message;
            
            if (document.Content != null)
            {
                document.Content.ParseStatus = ParseStatus.Failed;
                if (document.Content.OcrStatus == OcrStatus.Processing)
                {
                    document.Content.OcrStatus = OcrStatus.Failed;
                }
            }

            await repository.UpdateAsync(document);

            try
            {
                await _hubContext.Clients.All.SendAsync("DocumentUpdated", MapToDtoPayload(document));
            }
            catch { }
        }
    }

    private object MapToDtoPayload(Document document)
    {
        return new
        {
            id = document.Id,
            nama = document.Nama ?? document.NamaFile,
            namaFile = document.NamaFile,
            namaTenagaAhli = document.NamaTenagaAhli,
            jenisDokumen = document.JenisDokumen,
            periodeLaporan = document.PeriodeLaporan,
            ukuran = document.Ukuran,
            tanggalUpload = document.TanggalUpload,
            errorMessage = document.ErrorMessage
        };
    }

    private async Task ExtractMetadataWithLlmAsync(Document document, IServiceProvider serviceProvider, CancellationToken stoppingToken)
    {
        // LLM extraction is skipped for expert reports since fields are filled manually.
        await Task.CompletedTask;
    }
}
