namespace SpaApi.Models;

/// <summary>
/// Order 資料模型
/// </summary>
public class Order
{
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

/// <summary>
/// Order 建立請求
/// </summary>
public record CreateOrderRequest(
    int UserId,
    string OrderNumber,
    decimal TotalAmount,
    string Status,
    string ShippingAddress,
    string Note);

/// <summary>
/// Order 更新請求
/// </summary>
public record UpdateOrderRequest(
    int? UserId,
    string? OrderNumber,
    decimal? TotalAmount,
    string? Status,
    string? ShippingAddress,
    string? Note);

/// <summary>
/// Order 回應
/// </summary>
public record OrderResponse(
    int Id,
    int UserId,
    string OrderNumber,
    decimal TotalAmount,
    string Status,
    string ShippingAddress,
    string Note,

    DateTime CreatedAt);
