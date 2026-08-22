namespace SIAP.Api.DTOs.Dashboard;

public class DashboardSummaryDto
{
    public int TotalUsers { get; set; }
    public int PendingUsers { get; set; }
    public int TotalDocuments { get; set; }
    public long TotalStorageBytes { get; set; }
    public List<DocumentsByBidangDto> DocumentsByBidang { get; set; } = new();
    public List<RecentDocumentDto> RecentDocuments { get; set; } = new();
}

public class DocumentsByBidangDto
{
    public string BidangName { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class RecentDocumentDto
{
    public string Id { get; set; } = string.Empty;
    public string Nama { get; set; } = string.Empty;
    public string UploaderName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
