namespace SpaApi.Models;

/// <summary>
/// Product 資料模型
/// </summary>
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public int CategoryId { get; set; }
    public string Images { get; set; } = "";
    public string Status { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Product 建立請求
/// </summary>
public record CreateProductRequest(
    string Name,
    string Description,
    decimal Price,
    int Stock,
    int CategoryId,
    string Images,
    string Status);

/// <summary>
/// Product 更新請求
/// </summary>
public record UpdateProductRequest(
    string? Name,
    string? Description,
    decimal? Price,
    int? Stock,
    int? CategoryId,
    string? Images,
    string? Status);

/// <summary>
/// Product 回應
/// </summary>
public record ProductResponse(
    int Id,
    string Name,
    string Description,
    decimal Price,
    int Stock,
    int CategoryId,
    string Images,
    string Status,

    DateTime CreatedAt);
