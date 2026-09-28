using FluentValidation;
using SIPENTA.Api.DTOs.Documents;

namespace SIPENTA.Api.DTOs.Validators;

public class DocumentUpdateDtoValidator : AbstractValidator<DocumentUpdateDto>
{
    public DocumentUpdateDtoValidator()
    {
        // Metadata is optional
    }
}
