using Mapster;
using SIAP.Api.DTOs.Documents;
using SIAP.Api.Entities;

namespace SIAP.Api.Configurations;

public static class MapsterConfig
{
    public static void RegisterMappings()
    {
        TypeAdapterConfig<Document, DocumentResponseDto>.NewConfig()
            .Map(dest => dest.Bidang, src => src.Bidang != null ? src.Bidang.Nama : null)
            .Map(dest => dest.BidangKode, src => src.Bidang != null ? src.Bidang.Kode : null)
            .Map(dest => dest.ParseStatus, src => src.Content != null ? (ParseStatus?)src.Content.ParseStatus : null)
            .Map(dest => dest.PageCount, src => src.Content != null ? (int?)src.Content.PageCount : null)
            .Map(dest => dest.Language, src => src.Content != null ? src.Content.Language : null)
            .Map(dest => dest.ParsedAt, src => src.Content != null ? src.Content.ParsedAt : null);

        TypeAdapterConfig<DocumentCreateDto, Document>.NewConfig()
            .Ignore(dest => dest.Bidang)
            .Ignore(dest => dest.User)
            .Ignore(dest => dest.Content)
            .Ignore(dest => dest.Chunks)
            .Ignore(dest => dest.Accesses);

        TypeAdapterConfig<DocumentUpdateDto, Document>.NewConfig()
            .Ignore(dest => dest.Bidang)
            .Ignore(dest => dest.User)
            .Ignore(dest => dest.Content)
            .Ignore(dest => dest.Chunks)
            .Ignore(dest => dest.Accesses);
    }
}
