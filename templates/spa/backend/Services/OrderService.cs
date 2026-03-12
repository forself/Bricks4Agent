using Microsoft.EntityFrameworkCore;
using SpaApi.Data;
using SpaApi.Models;

namespace SpaApi.Services;

/// <summary>
/// Order 服務介面
/// </summary>
public interface IOrderService
{
    Task<List<OrderResponse>> GetAllAsync();
    Task<OrderResponse?> GetByIdAsync(int id);
    Task<OrderResponse> CreateAsync(CreateOrderRequest request);
    Task<OrderResponse?> UpdateAsync(int id, UpdateOrderRequest request);
    Task<bool> DeleteAsync(int id);
}

/// <summary>
/// Order 服務實作
/// </summary>
public class OrderService : IOrderService
{
    private readonly AppDbContext _context;

    public OrderService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<OrderResponse>> GetAllAsync()
    {
        return await _context.Orders
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => ToResponse(x))
            .ToListAsync();
    }

    public async Task<OrderResponse?> GetByIdAsync(int id)
    {
        var entity = await _context.Orders.FindAsync(id);
        return entity != null ? ToResponse(entity) : null;
    }

    public async Task<OrderResponse> CreateAsync(CreateOrderRequest request)
    {
        var entity = new Order
        {
            UserId = request.UserId,
            OrderNumber = request.OrderNumber,
            TotalAmount = request.TotalAmount,
            Status = request.Status,
            ShippingAddress = request.ShippingAddress,
            Note = request.Note,
            CreatedAt = DateTime.UtcNow
        };

        _context.Orders.Add(entity);
        await _context.SaveChangesAsync();

        return ToResponse(entity);
    }

    public async Task<OrderResponse?> UpdateAsync(int id, UpdateOrderRequest request)
    {
        var entity = await _context.Orders.FindAsync(id);
        if (entity == null) return null;

        if (request.UserId != null) entity.UserId = request.UserId.Value;
        if (request.OrderNumber != null) entity.OrderNumber = request.OrderNumber;
        if (request.TotalAmount != null) entity.TotalAmount = request.TotalAmount.Value;
        if (request.Status != null) entity.Status = request.Status;
        if (request.ShippingAddress != null) entity.ShippingAddress = request.ShippingAddress;
        if (request.Note != null) entity.Note = request.Note;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return ToResponse(entity);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _context.Orders.FindAsync(id);
        if (entity == null) return false;

        _context.Orders.Remove(entity);
        await _context.SaveChangesAsync();
        return true;
    }

    private static OrderResponse ToResponse(Order entity) => new(
        entity.Id,
        entity.UserId,
        entity.OrderNumber,
        entity.TotalAmount,
        entity.Status,
        entity.ShippingAddress,
        entity.Note,

        entity.CreatedAt
    );
}
