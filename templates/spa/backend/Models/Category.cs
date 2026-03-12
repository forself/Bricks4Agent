namespace SpaApi.Models;

/// <summary>
/// Category 資料模型
/// </summary>
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int ParentId { get; set; }
    public int SortOrder { get; set; }
    public string Icon { get; set; } = "";
    public string Status { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Category 建立請求
/// </summary>
public record CreateCategoryRequest(
    string Name,
    int ParentId,
    int SortOrder,
    string Icon,
    string Status);

/// <summary>
/// Category 更新請求
/// </summary>
public record UpdateCategoryRequest(
    string? Name,
    int? ParentId,
    int? SortOrder,
    string? Icon,
    string? Status);

/// <summary>
/// Category 回應
/// </summary>
public record CategoryResponse(
    int Id,
    string Name,
    int ParentId,
    int SortOrder,
    string Icon,
    string Status,

    DateTime CreatedAt);
