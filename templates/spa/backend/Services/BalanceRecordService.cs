using Microsoft.EntityFrameworkCore;
using SpaApi.Data;
using SpaApi.Models;

namespace SpaApi.Services;

/// <summary>
/// BalanceRecord 服務介面
/// </summary>
public interface IBalanceRecordService
{
    Task<List<BalanceRecordResponse>> GetAllAsync();
    Task<BalanceRecordResponse?> GetByIdAsync(int id);
    Task<BalanceRecordResponse> CreateAsync(CreateBalanceRecordRequest request);
    Task<BalanceRecordResponse?> UpdateAsync(int id, UpdateBalanceRecordRequest request);
    Task<bool> DeleteAsync(int id);
}

/// <summary>
/// BalanceRecord 服務實作
/// </summary>
public class BalanceRecordService : IBalanceRecordService
{
    private readonly AppDbContext _context;

    public BalanceRecordService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<BalanceRecordResponse>> GetAllAsync()
    {
        return await _context.BalanceRecords
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => ToResponse(x))
            .ToListAsync();
    }

    public async Task<BalanceRecordResponse?> GetByIdAsync(int id)
    {
        var entity = await _context.BalanceRecords.FindAsync(id);
        return entity != null ? ToResponse(entity) : null;
    }

    public async Task<BalanceRecordResponse> CreateAsync(CreateBalanceRecordRequest request)
    {
        var entity = new BalanceRecord
        {
            UserId = request.UserId,
            Amount = request.Amount,
            Type = request.Type,
            OrderId = request.OrderId,
            Note = request.Note,
            OperatorId = request.OperatorId,
            CreatedAt = DateTime.UtcNow
        };

        _context.BalanceRecords.Add(entity);
        await _context.SaveChangesAsync();

        return ToResponse(entity);
    }

    public async Task<BalanceRecordResponse?> UpdateAsync(int id, UpdateBalanceRecordRequest request)
    {
        var entity = await _context.BalanceRecords.FindAsync(id);
        if (entity == null) return null;

        if (request.UserId != null) entity.UserId = request.UserId.Value;
        if (request.Amount != null) entity.Amount = request.Amount.Value;
        if (request.Type != null) entity.Type = request.Type;
        if (request.OrderId != null) entity.OrderId = request.OrderId.Value;
        if (request.Note != null) entity.Note = request.Note;
        if (request.OperatorId != null) entity.OperatorId = request.OperatorId.Value;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return ToResponse(entity);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _context.BalanceRecords.FindAsync(id);
        if (entity == null) return false;

        _context.BalanceRecords.Remove(entity);
        await _context.SaveChangesAsync();
        return true;
    }

    private static BalanceRecordResponse ToResponse(BalanceRecord entity) => new(
        entity.Id,
        entity.UserId,
        entity.Amount,
        entity.Type,
        entity.OrderId,
        entity.Note,
        entity.OperatorId,

        entity.CreatedAt
    );
}
