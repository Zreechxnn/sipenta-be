using FluentValidation;
using SIAP.Api.DTOs.Documents;

namespace SIAP.Api.DTOs.Validators;

public class DocumentCreateDtoValidator : AbstractValidator<DocumentCreateDto>
{
    public DocumentCreateDtoValidator()
    {
        // Metadata is now optional
        RuleFor(x => x.Files).NotNull().NotEmpty().WithMessage("Minimal satu file dokumen wajib diisi.");
    }
}
