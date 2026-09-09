using FluentValidation;
using SIAP.Api.DTOs.Documents;

namespace SIAP.Api.DTOs.Validators;

public class DocumentCreateDtoValidator : AbstractValidator<DocumentCreateDto>
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".doc", ".txt", ".rtf", ".odt", ".xlsx", ".xls", ".csv", ".png", ".jpg", ".jpeg"
    };

    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".sh", ".ps1", ".psm1", ".vbs", ".js", ".mjs", 
        ".html", ".htm", ".xhtml", ".svg", ".xml", ".php", ".phtml", ".asp", ".aspx", 
        ".jsp", ".jar", ".war", ".scr", ".msi", ".reg", ".com", ".cpl", ".hta"
    };

    public DocumentCreateDtoValidator()
    {
        RuleFor(x => x.Files)
            .NotNull().NotEmpty().WithMessage("Minimal satu file dokumen wajib diisi.");

        RuleForEach(x => x.Files)
            .Must(file =>
            {
                if (file == null || file.Length == 0) return false;
                var ext = Path.GetExtension(file.FileName);
                if (string.IsNullOrEmpty(ext)) return false;
                if (BlockedExtensions.Contains(ext)) return false;
                return AllowedExtensions.Contains(ext);
            })
            .WithMessage("Tipe berkas yang diunggah tidak didukung atau berbahaya. Format yang didukung: PDF, DOCX, DOC, TXT, RTF, ODT, XLSX, XLS, CSV, PNG, JPG, JPEG.");
    }
}
