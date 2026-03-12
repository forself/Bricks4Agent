using Microsoft.EntityFrameworkCore;
using SpaApi.Data;
using SpaApi.Models;

namespace SpaApi.Services;

/// <summary>
/// Product 服務介面
/// </summary>
public interface IProductService
{
    Task<List<ProductResponse>> GetAllAsync();
    Task<ProductResponse?> GetByIdAsync(int id);
    Task<ProductResponse> CreateAsync(CreateProductRequest request);
    Task<ProductResponse?> UpdateAsync(int id, UpdateProductRequest request);
    Task<bool> DeleteAsync(int id);
}

/// <summary>
/// Product 服務實作
/// </summary>
public class ProductService : IProductService
{
    private readonly AppDbContext _context;

    public ProductService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<ProductResponse>> GetAllAsync()
    {
        return await _context.Products
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => ToResponse(x))
            .ToListAsync();
    }

    public async Task<ProductResponse?> GetByIdAsync(int id)
    {
        var entity = await _context.Products.FindAsync(id);
        return entity != null ? ToResponse(entity) : null;
    }

    public async Task<ProductResponse> CreateAsync(CreateProductRequest request)
    {
        var entity = new Product
        {
            Name = request.Name,
            Description = request.Description,
            Price = request.Price,
            Stock = request.Stock,
            CategoryId = request.CategoryId,
            Images = request.Images,
            Status = request.Status,
            CreatedAt = DateTime.UtcNow
        };

        _context.Products.Add(entity);
        await _context.SaveChangesAsync();

        return ToResponse(entity);
    }

    public async Task<ProductResponse?> UpdateAsync(int id, UpdateProductRequest request)
    {
        var entity = await _context.Products.FindAsync(id);
        if (entity == null) return null;

        if (request.Name != null) entity.Name = request.Name;
        if (request.Description != null) entity.Description = request.Description;
        if (request.Price != null) entity.Price = request.Price.Value;
        if (request.Stock != null) entity.Stock = request.Stock.Value;
        if (request.CategoryId != null) entity.CategoryId = request.CategoryId.Value;
        if (request.Images != null) entity.Images = request.Images;
        if (request.Status != null) entity.Status = request.Status;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return ToResponse(entity);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _context.Products.FindAsync(id);
        if (entity == null) return false;

        _context.Products.Remove(entity);
        await _context.SaveChangesAsync();
        return true;
    }

    private static ProductResponse ToResponse(Product entity) => new(
        entity.Id,
        entity.Name,
        entity.Description,
        entity.Price,
        entity.Stock,
        entity.CategoryId,
        entity.Images,
        entity.Status,

        entity.CreatedAt
    );
}
