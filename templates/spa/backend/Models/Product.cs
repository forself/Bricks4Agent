using BaseOrm;

namespace SpaApi.Models;

[Table("Products")]
public class Product
{
    [Key]
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

public record CreateProductRequest(
    string Name,
    string Description,
    decimal Price,
    int Stock,
    int CategoryId,
    string Images,
    string Status);

public record UpdateProductRequest(
    string? Name,
    string? Description,
    decimal? Price,
    int? Stock,
    int? CategoryId,
    string? Images,
    string? Status);

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
