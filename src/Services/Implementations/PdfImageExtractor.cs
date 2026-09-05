using System.Text.RegularExpressions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Xobject;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

public class PdfImageExtractor : IPdfImageExtractor
{
    private readonly ILogger<PdfImageExtractor> _logger;

    public PdfImageExtractor(ILogger<PdfImageExtractor> logger)
    {
        _logger = logger;
    }

    public async Task<List<ExtractedPdfImage>> ExtractImagesAsync(Stream pdfStream)
    {
        var extractedImages = new List<ExtractedPdfImage>();

        try
        {
            using var ms = new MemoryStream();
            await pdfStream.CopyToAsync(ms);
            ms.Position = 0;

            using var reader = new PdfReader(ms);
            using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(reader);
            int pageCount = pdfDoc.GetNumberOfPages();

            for (int pageNum = 1; pageNum <= pageCount; pageNum++)
            {
                var page = pdfDoc.GetPage(pageNum);
                string pageText = "";
                try
                {
                    pageText = PdfTextExtractor.GetTextFromPage(page) ?? "";
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to extract text from page {PageNum}", pageNum);
                }

                // 1. Extract via Combined Text & Image Spatial Listener
                var listener = new PageSpatialContentListener(pageNum, pageText, _logger);
                var processor = new PdfCanvasProcessor(listener);
                try
                {
                    processor.ProcessPageContent(page);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Processor failed on page {PageNum}", pageNum);
                }

                var pageExtracted = listener.GetExtractedImages();
                extractedImages.AddRange(pageExtracted);

                // 2. Fallback check on Page Resources XObjects if Canvas listener found 0 images
                if (!pageExtracted.Any())
                {
                    try
                    {
                        var resources = page.GetResources();
                        if (resources != null)
                        {
                            var xObjects = resources.GetResource(PdfName.XObject);
                            if (xObjects is PdfDictionary xDict)
                            {
                                foreach (var name in xDict.KeySet())
                                {
                                    var stream = xDict.GetAsStream(name);
                                    if (stream != null && PdfName.Image.Equals(stream.GetAsName(PdfName.Subtype)))
                                    {
                                        var imgObj = new PdfImageXObject(stream);
                                        byte[] bytes = imgObj.GetImageBytes();
                                        if (bytes != null && bytes.Length > 2048)
                                        {
                                            string ext = imgObj.IdentifyImageFileExtension() ?? "png";
                                            int width = (int)imgObj.GetWidth();
                                            int height = (int)imgObj.GetHeight();

                                            if (width >= 60 && height >= 60)
                                            {
                                                extractedImages.Add(new ExtractedPdfImage
                                                {
                                                    PageNumber = pageNum,
                                                    ImageBytes = bytes,
                                                    Extension = ext,
                                                    MimeType = GetMimeType(ext),
                                                    Width = width,
                                                    Height = height,
                                                    FileSize = bytes.Length,
                                                    PageText = pageText,
                                                    Caption = ExtractDefaultCaption(pageText)
                                                });
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Fallback resource extraction failed on page {PageNum}", pageNum);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract images from PDF stream using iText 7.");
        }

        return extractedImages;
    }

    private static string GetMimeType(string ext) => ext.ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => "image/jpeg",
        "png" => "image/png",
        "gif" => "image/gif",
        "bmp" => "image/bmp",
        "webp" => "image/webp",
        _ => "image/png"
    };

    private static string? ExtractDefaultCaption(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => l.Length > 3)
                        .Take(3)
                        .ToList();
        return lines.Any() ? string.Join(" - ", lines) : null;
    }

    private class PageSpatialContentListener : IEventListener
    {
        private readonly int _pageNumber;
        private readonly string _fullPageText;
        private readonly ILogger _logger;

        private class PositionedText
        {
            public string Text { get; set; } = string.Empty;
            public float StartX { get; set; }
            public float EndX { get; set; }
            public float Y { get; set; }
        }

        private class PositionedLine
        {
            public string Text { get; set; } = string.Empty;
            public float Y { get; set; }
            public bool IsDateHeader { get; set; }
            public bool IsActivityHeader { get; set; }
        }

        private class PositionedImage
        {
            public byte[] ImageBytes { get; set; } = Array.Empty<byte>();
            public string Extension { get; set; } = "png";
            public string MimeType { get; set; } = "image/png";
            public int Width { get; set; }
            public int Height { get; set; }
            public int FileSize { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float RenderedWidth { get; set; }
            public float RenderedHeight { get; set; }
        }

        private readonly List<PositionedText> _rawTexts = new();
        private readonly List<PositionedImage> _rawImages = new();

        public PageSpatialContentListener(int pageNumber, string fullPageText, ILogger logger)
        {
            _pageNumber = pageNumber;
            _fullPageText = fullPageText;
            _logger = logger;
        }

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type == EventType.RENDER_TEXT && data is TextRenderInfo textInfo)
            {
                var text = textInfo.GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    var baseline = textInfo.GetBaseline();
                    if (baseline != null)
                    {
                        var start = baseline.GetStartPoint();
                        var end = baseline.GetEndPoint();
                        _rawTexts.Add(new PositionedText
                        {
                            Text = text,
                            StartX = start.Get(0),
                            EndX = end.Get(0),
                            Y = start.Get(1)
                        });
                    }
                }
            }
            else if (type == EventType.RENDER_IMAGE && data is ImageRenderInfo renderInfo)
            {
                try
                {
                    var img = renderInfo.GetImage();
                    if (img == null) return;

                    byte[] bytes = img.GetImageBytes();
                    if (bytes == null || bytes.Length < 2048) return; // Ignore small icons

                    var ctm = renderInfo.GetImageCtm();
                    float imgX = ctm.Get(iText.Kernel.Geom.Matrix.I31);
                    float imgY = ctm.Get(iText.Kernel.Geom.Matrix.I32);
                    float imgW = Math.Abs(ctm.Get(iText.Kernel.Geom.Matrix.I11));
                    float imgH = Math.Abs(ctm.Get(iText.Kernel.Geom.Matrix.I22));

                    string ext = img.IdentifyImageFileExtension() ?? "png";
                    int width = (int)img.GetWidth();
                    int height = (int)img.GetHeight();

                    if (width >= 60 && height >= 60)
                    {
                        _rawImages.Add(new PositionedImage
                        {
                            ImageBytes = bytes,
                            Extension = ext,
                            MimeType = GetMimeType(ext),
                            Width = width,
                            Height = height,
                            FileSize = bytes.Length,
                            X = imgX,
                            Y = imgY,
                            RenderedWidth = imgW,
                            RenderedHeight = imgH
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to render image on page {PageNum}", _pageNumber);
                }
            }
        }

        private static string BuildLineText(List<PositionedText> tokens)
        {
            var ordered = tokens.OrderBy(t => t.StartX).ToList();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < ordered.Count; i++)
            {
                var cur = ordered[i];
                if (i > 0)
                {
                    var prev = ordered[i - 1];
                    if (cur.StartX - prev.EndX > 2.0f)
                    {
                        sb.Append(' ');
                    }
                }
                sb.Append(cur.Text);
            }
            return Regex.Replace(sb.ToString().Trim(), @"\s+", " ");
        }

        public List<ExtractedPdfImage> GetExtractedImages()
        {
            var results = new List<ExtractedPdfImage>();
            if (!_rawImages.Any()) return results;

            // 1. Group raw text tokens into coherent lines by Y coordinate
            var lines = new List<PositionedLine>();
            var sortedTokens = _rawTexts.OrderByDescending(t => t.Y).ThenBy(t => t.StartX).ToList();

            var currentCluster = new List<PositionedText>();
            foreach (var token in sortedTokens)
            {
                if (currentCluster.Count == 0)
                {
                    currentCluster.Add(token);
                }
                else
                {
                    var clusterAvgY = currentCluster.Average(t => t.Y);
                    if (Math.Abs(token.Y - clusterAvgY) <= 4.0f)
                    {
                        currentCluster.Add(token);
                    }
                    else
                    {
                        var lineText = BuildLineText(currentCluster);
                        if (!string.IsNullOrWhiteSpace(lineText))
                        {
                            lines.Add(new PositionedLine
                            {
                                Text = lineText,
                                Y = clusterAvgY,
                                IsDateHeader = Regex.IsMatch(lineText, @"(Hari,\s*)?Tanggal\s*:|(Senin|Selasa|Rabu|Kamis|Jumat|Sabtu|Minggu),\s*\d+\s+[A-Za-z]+\s+\d{4}", RegexOptions.IgnoreCase),
                                IsActivityHeader = Regex.IsMatch(lineText, @"Uraian\s+Kegiatan|Kegiatan\s*:", RegexOptions.IgnoreCase)
                            });
                        }
                        currentCluster.Clear();
                        currentCluster.Add(token);
                    }
                }
            }

            if (currentCluster.Any())
            {
                var lineText = BuildLineText(currentCluster);
                if (!string.IsNullOrWhiteSpace(lineText))
                {
                    lines.Add(new PositionedLine
                    {
                        Text = lineText,
                        Y = currentCluster.Average(t => t.Y),
                        IsDateHeader = Regex.IsMatch(lineText, @"(Hari,\s*)?Tanggal\s*:|(Senin|Selasa|Rabu|Kamis|Jumat|Sabtu|Minggu),\s*\d+\s+[A-Za-z]+\s+\d{4}", RegexOptions.IgnoreCase),
                        IsActivityHeader = Regex.IsMatch(lineText, @"Uraian\s+Kegiatan|Kegiatan\s*:", RegexOptions.IgnoreCase)
                    });
                }
            }

            // 2. Build structured Activity Sections from lines
            var sections = new List<ActivitySection>();
            ActivitySection? currentSection = null;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.IsDateHeader)
                {
                    if (currentSection != null)
                    {
                        currentSection.BottomY = line.Y;
                        sections.Add(currentSection);
                    }

                    currentSection = new ActivitySection
                    {
                        DateHeader = line.Text,
                        TopY = line.Y,
                        BottomY = 0f
                    };
                    currentSection.Lines.Add(line.Text);

                    // Look ahead for activity description in next 1-4 lines
                    for (int j = i + 1; j < Math.Min(lines.Count, i + 5); j++)
                    {
                        var nextLine = lines[j];
                        if (nextLine.IsDateHeader) break;
                        
                        var cleanText = Regex.Replace(nextLine.Text, @"^(No\s+Uraian\s+Kegiatan.*|\d+\.|\s*-\s*)+", "", RegexOptions.IgnoreCase).Trim();
                        if (cleanText.Length > 3 && !nextLine.IsActivityHeader && string.IsNullOrEmpty(currentSection.ActivityTitle))
                        {
                            currentSection.ActivityTitle = cleanText;
                        }
                    }
                }
                else if (currentSection != null)
                {
                    currentSection.Lines.Add(line.Text);
                }
            }

            if (currentSection != null)
            {
                sections.Add(currentSection);
            }

            // 3. For each raw image, match to its exact activity section on the page
            foreach (var rawImg in _rawImages)
            {
                float imgCenterY = rawImg.Y + (rawImg.RenderedHeight / 2.0f);
                float imgTopY = rawImg.Y + rawImg.RenderedHeight;

                string caption = "";
                string contextText = "";

                if (sections.Any())
                {
                    // Find section containing imgCenterY or the closest section above this image
                    var matchedSection = sections.FirstOrDefault(s => imgCenterY <= s.TopY + 40 && (s.BottomY == 0 || imgCenterY >= s.BottomY - 40));
                    
                    if (matchedSection == null)
                    {
                        // Fallback: take section whose TopY is immediately above this image
                        matchedSection = sections.Where(s => s.TopY >= rawImg.Y - 50)
                                                 .OrderBy(s => s.TopY - rawImg.Y)
                                                 .FirstOrDefault() ?? sections.First();
                    }

                    var datePart = matchedSection.DateHeader;
                    var actPart = !string.IsNullOrEmpty(matchedSection.ActivityTitle) ? $" - {matchedSection.ActivityTitle}" : "";
                    caption = $"{datePart}{actPart}".Trim();
                    contextText = string.Join("\n", matchedSection.Lines);
                }
                else
                {
                    // Fallback to top lines of page
                    var topLines = lines.Take(3).Select(l => l.Text).ToList();
                    caption = topLines.Any() ? string.Join(" - ", topLines) : ExtractDefaultCaption(_fullPageText) ?? "Dokumentasi Laporan";
                    contextText = _fullPageText;
                }

                results.Add(new ExtractedPdfImage
                {
                    PageNumber = _pageNumber,
                    ImageBytes = rawImg.ImageBytes,
                    Extension = rawImg.Extension,
                    MimeType = rawImg.MimeType,
                    Width = rawImg.Width,
                    Height = rawImg.Height,
                    FileSize = rawImg.FileSize,
                    PageText = contextText,
                    Caption = caption
                });
            }

            return results;
        }

        private class ActivitySection
        {
            public string DateHeader { get; set; } = string.Empty;
            public string ActivityTitle { get; set; } = string.Empty;
            public float TopY { get; set; }
            public float BottomY { get; set; }
            public List<string> Lines { get; } = new();
        }

        public ICollection<EventType> GetSupportedEvents()
        {
            return new HashSet<EventType> { EventType.RENDER_TEXT, EventType.RENDER_IMAGE };
        }
    }
}

