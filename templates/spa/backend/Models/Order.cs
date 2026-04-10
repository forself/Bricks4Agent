using BaseOrm;

namespace SpaApi.Models;

[Table("Orders")]
public class Order
{
    [Key]
    public int Id { get; set; }
    public int UserId { get; set; }
    public string OrderNumber { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "";
    public string ShippingAddress { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public record CreateOrderRequest(
    int UserId,
    string OrderNumber,
    decimal TotalAmount,
    string Status,
    string ShippingAddress,
    string Note);

public record UpdateOrderRequest(
    int? UserId,
    string? OrderNumber,
    decimal? TotalAmount,
    string? Status,
    string? ShippingAddress,
    string? Note);

public record OrderResponse(
    int Id,
    int UserId,
    string OrderNumber,
    decimal TotalAmount,
    string Status,
    string ShippingAddress,
    string Note,
    DateTime CreatedAt,
    IReadOnlyList<OrderItemSummary> Items);

public record CreateShopOrderRequest(
    int ProductId,
    int Quantity,
    string ShippingAddress,
    string Note);
