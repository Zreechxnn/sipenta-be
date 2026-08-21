using System.ComponentModel.DataAnnotations;

namespace SIAP.Api.DTOs.Documents;

public class VectorSearchDto
{
    [Required]
    public string Query { get; set; } = string.Empty;
    
    public int TopK { get; set; } = 5;
    
    public double? SimilarityThreshold { get; set; } = 0.75;
}
