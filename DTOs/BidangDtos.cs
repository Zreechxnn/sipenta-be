namespace SIAP.Api.DTOs;

public class BidangDto
{
    public int Id { get; set; }
    public string Nama { get; set; } = string.Empty;
    public string? Kode { get; set; }
    public string? Deskripsi { get; set; }
    public int UserCount { get; set; }
    public int DocumentCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateBidangRequest
{
    public string Nama { get; set; } = string.Empty;
    public string? Kode { get; set; }
    public string? Deskripsi { get; set; }
}

public class UpdateBidangRequest
{
    public string Nama { get; set; } = string.Empty;
    public string? Kode { get; set; }
    public string? Deskripsi { get; set; }
}
