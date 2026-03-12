using Microsoft.EntityFrameworkCore;
using SpaApi.Data;
using SpaApi.Models;

namespace SpaApi.Services;

/// <summary>
/// Category 服務介面
/// </summary>
public interface ICategoryService
{
    Task<List<CategoryResponse>> GetAllAsync();
    Task<CategoryResponse?> GetByIdAsync(int id);
    Task<CategoryResponse> CreateAsync(CreateCategoryRequest request);
    Task<CategoryResponse?> UpdateAsync(int id, UpdateCategoryRequest request);
    Task<bool> DeleteAsync(int id);
}

/// <summary>
/// Category 服務實作
/// </summary>
public class CategoryService : ICategoryService
{
    private readonly AppDbContext _context;

    public CategoryService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<CategoryResponse>> GetAllAsync()
    {
        return await _context.Categories
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => ToResponse(x))
            .ToListAsync();
    }

    public async Task<CategoryResponse?> GetByIdAsync(int id)
    {
        var entity = await _context.Categories.FindAsync(id);
        return entity != null ? ToResponse(entity) : null;
    }

    public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request)
    {
        var entity = new Category
        {
            Name = request.Name,
            ParentId = request.ParentId,
            SortOrder = request.SortOrder,
            Icon = request.Icon,
            Status = request.Status,
            CreatedAt = DateTime.UtcNow
        };

        _context.Categories.Add(entity);
        await _context.SaveChangesAsync();

        return ToResponse(entity);
    }

    public async Task<CategoryResponse?> UpdateAsync(int id, UpdateCategoryRequest request)
    {
        var entity = await _context.Categories.FindAsync(id);
        if (entity == null) return null;

        if (request.Name != null) entity.Name = request.Name;
        if (request.ParentId != null) entity.ParentId = request.ParentId.Value;
        if (request.SortOrder != null) entity.SortOrder = request.SortOrder.Value;
        if (request.Icon != null) entity.Icon = request.Icon;
        if (request.Status != null) entity.Status = request.Status;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return ToResponse(entity);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _context.Categories.FindAsync(id);
        if (entity == null) return false;

        _context.Categories.Remove(entity);
        await _context.SaveChangesAsync();
        return true;
    }

    private static CategoryResponse ToResponse(Category entity) => new(
        entity.Id,
        entity.Name,
        entity.ParentId,
        entity.SortOrder,
        entity.Icon,
        entity.Status,

        entity.CreatedAt
    );
}
