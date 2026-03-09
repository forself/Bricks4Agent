using Microsoft.EntityFrameworkCore;
using SpaGenerator.Data;
using SpaGenerator.Models;

namespace SpaGenerator.Services;

/**
 * 使用者服務實作
 */
public class UserService : IUserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<UserDto>> GetAllAsync()
    {
        var users = await _db.Users
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        return users.Select(UserDto.FromEntity);
    }

    public async Task<UserDto?> GetByIdAsync(int id)
    {
        var user = await _db.Users.FindAsync(id);
        return user == null ? null : UserDto.FromEntity(user);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request)
    {
        var user = new User
        {
            Name = request.Name,
            Email = request.Email,
            PasswordHash = BCryptHelper.HashPassword(request.Password ?? "changeme"),
            Role = request.Role ?? "user",
            Department = request.Department,
            Phone = request.Phone,
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return UserDto.FromEntity(user);
    }

    public async Task<UserDto?> UpdateAsync(int id, UpdateUserRequest request)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return null;

        if (request.Name != null) user.Name = request.Name;
        if (request.Email != null) user.Email = request.Email;
        if (request.Role != null) user.Role = request.Role;
        if (request.Status != null) user.Status = request.Status;
        if (request.Department != null) user.Department = request.Department;
        if (request.Phone != null) user.Phone = request.Phone;

        await _db.SaveChangesAsync();

        return UserDto.FromEntity(user);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return false;

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        return true;
    }
}
