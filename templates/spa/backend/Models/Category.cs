using BaseOrm;

namespace SpaApi.Models;

[Table("Categories")]
public class Category
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int ParentId { get; set; }
    public int SortOrder { get; set; }
    public string Icon { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public record CreateCategoryRequest(
    string Name,
    int ParentId,
    int SortOrder,
    string Icon,
    string Status);

public record UpdateCategoryRequest(
    string? Name,
    int? ParentId,
    int? SortOrder,
    string? Icon,
    string? Status);

public record CategoryResponse(
    int Id,
    string Name,
    int ParentId,
    int SortOrder,
    string Icon,
    string Status,
    DateTime CreatedAt);
