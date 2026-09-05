namespace SIAP.Api.DTOs.Documents;

public class DocumentUpdateDto
{
    public string? Nama { get; set; }
    public string? NamaTenagaAhli { get; set; }
    public string? JenisDokumen { get; set; }
    public string? PeriodeLaporan { get; set; }
    public int? BidangId { get; set; }
    public string? Bidang { get; set; }
}
