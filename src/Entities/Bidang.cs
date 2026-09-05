namespace SIAP.Api.Entities;

public class Bidang
{
    public int Id { get; set; }
    public string Nama { get; set; } = string.Empty;
    public string? Kode { get; set; }
    public string? Deskripsi { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Document> Documents { get; set; } = new List<Document>();
}
