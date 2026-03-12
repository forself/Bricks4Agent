namespace SpaApi.Models;

/// <summary>
/// BalanceRecord 資料模型
/// </summary>
public class BalanceRecord
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public decimal Amount { get; set; }
    public string Type { get; set; } = "";
    public int OrderId { get; set; }
    public string Note { get; set; } = "";
    public int OperatorId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// BalanceRecord 建立請求
/// </summary>
public record CreateBalanceRecordRequest(
    int UserId,
    decimal Amount,
    string Type,
    int OrderId,
    string Note,
    int OperatorId);

/// <summary>
/// BalanceRecord 更新請求
/// </summary>
public record UpdateBalanceRecordRequest(
    int? UserId,
    decimal? Amount,
    string? Type,
    int? OrderId,
    string? Note,
    int? OperatorId);

/// <summary>
/// BalanceRecord 回應
/// </summary>
public record BalanceRecordResponse(
    int Id,
    int UserId,
    decimal Amount,
    string Type,
    int OrderId,
    string Note,
    int OperatorId,

    DateTime CreatedAt);
