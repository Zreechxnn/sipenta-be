using FluentValidation;
using SIAP.Api.DTOs.Documents;

namespace SIAP.Api.DTOs.Validators;

public class DocumentUpdateDtoValidator : AbstractValidator<DocumentUpdateDto>
{
    public DocumentUpdateDtoValidator()
    {
        // Metadata is optional
    }
}
