namespace SIAP.Api.DTOs.Documents;

public class DocumentSearchDto
{
    public string? Keyword { get; set; }
    public string? JenisDokumen { get; set; }
    public string? NamaTenagaAhli { get; set; }
    public string? PeriodeLaporan { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public Guid? UserId { get; set; }
    public int? BidangId { get; set; }
    public string? Bidang { get; set; }
}
