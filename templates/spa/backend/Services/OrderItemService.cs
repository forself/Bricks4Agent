using Microsoft.EntityFrameworkCore;
using SpaApi.Data;
using SpaApi.Models;

namespace SpaApi.Services;

/// <summary>
/// OrderItem 服務介面
/// </summary>
public interface IOrderItemService
{
    Task<List<OrderItemResponse>> GetAllAsync();
    Task<OrderItemResponse?> GetByIdAsync(int id);
    Task<OrderItemResponse> CreateAsync(CreateOrderItemRequest request);
    Task<OrderItemResponse?> UpdateAsync(int id, UpdateOrderItemRequest request);
    Task<bool> DeleteAsync(int id);
}

/// <summary>
/// OrderItem 服務實作
/// </summary>
public class OrderItemService : IOrderItemService
{
    private readonly AppDbContext _context;

    public OrderItemService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<OrderItemResponse>> GetAllAsync()
    {
        return await _context.OrderItems
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => ToResponse(x))
            .ToListAsync();
    }

    public async Task<OrderItemResponse?> GetByIdAsync(int id)
    {
        var entity = await _context.OrderItems.FindAsync(id);
        return entity != null ? ToResponse(entity) : null;
    }

    public async Task<OrderItemResponse> CreateAsync(CreateOrderItemRequest request)
    {
        var entity = new OrderItem
        {
            OrderId = request.OrderId,
            ProductId = request.ProductId,
            ProductName = request.ProductName,
            UnitPrice = request.UnitPrice,
            Quantity = request.Quantity,
            Subtotal = request.Subtotal,
            CreatedAt = DateTime.UtcNow
        };

        _context.OrderItems.Add(entity);
        await _context.SaveChangesAsync();

        return ToResponse(entity);
    }

    public async Task<OrderItemResponse?> UpdateAsync(int id, UpdateOrderItemRequest request)
    {
        var entity = await _context.OrderItems.FindAsync(id);
        if (entity == null) return null;

        if (request.OrderId != null) entity.OrderId = request.OrderId.Value;
        if (request.ProductId != null) entity.ProductId = request.ProductId.Value;
        if (request.ProductName != null) entity.ProductName = request.ProductName;
        if (request.UnitPrice != null) entity.UnitPrice = request.UnitPrice.Value;
        if (request.Quantity != null) entity.Quantity = request.Quantity.Value;
        if (request.Subtotal != null) entity.Subtotal = request.Subtotal.Value;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return ToResponse(entity);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _context.OrderItems.FindAsync(id);
        if (entity == null) return false;

        _context.OrderItems.Remove(entity);
        await _context.SaveChangesAsync();
        return true;
    }

    private static OrderItemResponse ToResponse(OrderItem entity) => new(
        entity.Id,
        entity.OrderId,
        entity.ProductId,
        entity.ProductName,
        entity.UnitPrice,
        entity.Quantity,
        entity.Subtotal,

        entity.CreatedAt
    );
}
