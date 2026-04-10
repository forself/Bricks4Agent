using BaseOrm;

namespace SpaApi.Models;

[Table("OrderItems")]
public class OrderItem
{
    [Key]
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal Subtotal { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public record CreateOrderItemRequest(
    int OrderId,
    int ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal Subtotal);

public record UpdateOrderItemRequest(
    int? OrderId,
    int? ProductId,
    string? ProductName,
    decimal? UnitPrice,
    int? Quantity,
    decimal? Subtotal);

public record OrderItemResponse(
    int Id,
    int OrderId,
    int ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal Subtotal,
    DateTime CreatedAt);

public record OrderItemSummary(
    int ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal Subtotal);
