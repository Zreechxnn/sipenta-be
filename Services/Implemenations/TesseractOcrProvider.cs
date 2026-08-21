using System.Diagnostics;
using System.Text;
using UglyToad.PdfPig;
using SIAP.Api.Configurations;
using Microsoft.Extensions.Options;
using SIAP.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace SIAP.Api.Services.Implemenations;

public class TesseractOcrProvider : IOcrProvider
{
    private readonly ILogger<TesseractOcrProvider> _logger;
    private readonly OcrOptions _options;

    public TesseractOcrProvider(ILogger<TesseractOcrProvider> logger, IOptions<OcrOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task<OcrResult> ExtractTextAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[OCR] Start");
        var rawTextBuilder = new StringBuilder();
        double totalConfidence = 0;
        int imageCount = 0;

        var startPosition = stream.Position;
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        
        try
        {
            var exePath = _options.ExecutablePath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                throw new InvalidOperationException($"Tesseract ExecutablePath is not configured properly or file does not exist: '{exePath}'");

            var tessdataPath = _options.DataPath;
            if (string.IsNullOrWhiteSpace(tessdataPath) || !Directory.Exists(tessdataPath))
                throw new InvalidOperationException($"Tesseract DataPath is not configured properly or directory does not exist. Path configured: '{tessdataPath}'");

            var language = string.IsNullOrWhiteSpace(_options.Language) ? "ind+eng" : _options.Language;
            
            Directory.CreateDirectory(tempDir);
            
            bool isPdf = false;
            try
            {
                using var document = PdfDocument.Open(stream);
                isPdf = true;
                
                int pageIndex = 1;
                foreach (var page in document.GetPages())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int imageIndex = 1;
                    
                    foreach (var image in page.GetImages())
                    {
                        string tempImgPath = Path.Combine(tempDir, $"img_p{pageIndex}_{imageIndex}");
                        string ext = "";
                        byte[]? bytesToSave = null;
                        
                        if (image.TryGetPng(out var pngBytes))
                        {
                            ext = ".png";
                            bytesToSave = pngBytes;
                        }
                        else if (image.TryGetBytes(out var rawBytes))
                        {
                            ext = ".raw";
                            bytesToSave = rawBytes.ToArray();
                        }
                        
                        if (bytesToSave != null)
                        {
                            string fullImgPath = tempImgPath + ext;
                            await File.WriteAllBytesAsync(fullImgPath, bytesToSave, cancellationToken);
                            var result = await RunTesseractAsync(fullImgPath, language, exePath, tessdataPath, cancellationToken);
                            
                            if (!result.Success)
                            {
                                _logger.LogError("[OCR] Failed: {Error}", result.ErrorMessage);
                                return new OcrResult { Success = false, ErrorMessage = result.ErrorMessage };
                            }
                            
                            rawTextBuilder.AppendLine(result.Text);
                            imageCount++;
                        }
                        imageIndex++;
                    }
                    pageIndex++;
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // Not a PDF or PdfPig failed to parse it. Let's process it as a direct image.
                _logger.LogDebug(ex, "Stream is not a PDF or failed PDF parsing. Treating as direct image.");
            }

            if (!isPdf)
            {
                stream.Position = startPosition;
                string tempImgPath = Path.Combine(tempDir, "direct_image.tmp");
                using var fs = new FileStream(tempImgPath, FileMode.Create);
                await stream.CopyToAsync(fs, cancellationToken);
                fs.Close();

                var result = await RunTesseractAsync(tempImgPath, language, exePath, tessdataPath, cancellationToken);
                if (!result.Success)
                {
                    _logger.LogError("[OCR] Failed: {Error}", result.ErrorMessage);
                    return new OcrResult { Success = false, ErrorMessage = result.ErrorMessage };
                }
                
                rawTextBuilder.AppendLine(result.Text);
                imageCount++;
            }
            
            _logger.LogInformation("[OCR] Success");
            return new OcrResult
            {
                Success = true,
                RawText = rawTextBuilder.ToString().TrimEnd(),
                Confidence = null // Using CLI stdout doesn't yield confidence out of the box
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OCR] Failed");
            return new OcrResult { Success = false, ErrorMessage = ex.Message };
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = startPosition;
            }
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    private async Task<(bool Success, string Text, string ErrorMessage)> RunTesseractAsync(
        string imagePath, 
        string language, 
        string exePath, 
        string tessdataPath, 
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"\"{imagePath}\" stdout -l {language} --tessdata-dir \"{tessdataPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                var errorMsg = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                return (false, string.Empty, $"Tesseract failed with exit code {process.ExitCode}. Error: {errorMsg}");
            }

            return (true, stdout, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message);
        }
    }
}
