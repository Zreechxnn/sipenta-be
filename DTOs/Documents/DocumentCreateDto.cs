using Microsoft.AspNetCore.Http;

namespace SIAP.Api.DTOs.Documents;

public class DocumentCreateDto
{
    public List<IFormFile> Files { get; set; } = new List<IFormFile>();
    public string? Nama { get; set; }
    public string? NamaTenagaAhli { get; set; }
    public string? JenisDokumen { get; set; }
    public string? PeriodeLaporan { get; set; }
    public Guid? UserId { get; set; }
    public int? BidangId { get; set; }
    public string? Bidang { get; set; }
}
