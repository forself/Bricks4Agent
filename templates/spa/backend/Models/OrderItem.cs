namespace SpaApi.Models;

/// <summary>
/// OrderItem 資料模型
/// </summary>
public class OrderItem
{
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

/// <summary>
/// OrderItem 建立請求
/// </summary>
public record CreateOrderItemRequest(
    int OrderId,
    int ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal Subtotal);

/// <summary>
/// OrderItem 更新請求
/// </summary>
public record UpdateOrderItemRequest(
    int? OrderId,
    int? ProductId,
    string? ProductName,
    decimal? UnitPrice,
    int? Quantity,
    decimal? Subtotal);

/// <summary>
/// OrderItem 回應
/// </summary>
public record OrderItemResponse(
    int Id,
    int OrderId,
    int ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal Subtotal,

    DateTime CreatedAt);
