namespace SpaApi.Models;

/// <summary>
/// CartItem 資料模型
/// </summary>
public class CartItem
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// CartItem 建立請求
/// </summary>
public record CreateCartItemRequest(
    int UserId,
    int ProductId,
    int Quantity);

/// <summary>
/// CartItem 更新請求
/// </summary>
public record UpdateCartItemRequest(
    int? UserId,
    int? ProductId,
    int? Quantity);

/// <summary>
/// CartItem 回應
/// </summary>
public record CartItemResponse(
    int Id,
    int UserId,
    int ProductId,
    int Quantity,

    DateTime CreatedAt);
