using Microsoft.EntityFrameworkCore;
using SpaApi.Data;
using SpaApi.Models;

namespace SpaApi.Services;

/// <summary>
/// CartItem 服務介面
/// </summary>
public interface ICartItemService
{
    Task<List<CartItemResponse>> GetAllAsync();
    Task<CartItemResponse?> GetByIdAsync(int id);
    Task<CartItemResponse> CreateAsync(CreateCartItemRequest request);
    Task<CartItemResponse?> UpdateAsync(int id, UpdateCartItemRequest request);
    Task<bool> DeleteAsync(int id);
}

/// <summary>
/// CartItem 服務實作
/// </summary>
public class CartItemService : ICartItemService
{
    private readonly AppDbContext _context;

    public CartItemService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<CartItemResponse>> GetAllAsync()
    {
        return await _context.CartItems
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => ToResponse(x))
            .ToListAsync();
    }

    public async Task<CartItemResponse?> GetByIdAsync(int id)
    {
        var entity = await _context.CartItems.FindAsync(id);
        return entity != null ? ToResponse(entity) : null;
    }

    public async Task<CartItemResponse> CreateAsync(CreateCartItemRequest request)
    {
        var entity = new CartItem
        {
            UserId = request.UserId,
            ProductId = request.ProductId,
            Quantity = request.Quantity,
            CreatedAt = DateTime.UtcNow
        };

        _context.CartItems.Add(entity);
        await _context.SaveChangesAsync();

        return ToResponse(entity);
    }

    public async Task<CartItemResponse?> UpdateAsync(int id, UpdateCartItemRequest request)
    {
        var entity = await _context.CartItems.FindAsync(id);
        if (entity == null) return null;

        if (request.UserId != null) entity.UserId = request.UserId.Value;
        if (request.ProductId != null) entity.ProductId = request.ProductId.Value;
        if (request.Quantity != null) entity.Quantity = request.Quantity.Value;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return ToResponse(entity);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _context.CartItems.FindAsync(id);
        if (entity == null) return false;

        _context.CartItems.Remove(entity);
        await _context.SaveChangesAsync();
        return true;
    }

    private static CartItemResponse ToResponse(CartItem entity) => new(
        entity.Id,
        entity.UserId,
        entity.ProductId,
        entity.Quantity,

        entity.CreatedAt
    );
}
