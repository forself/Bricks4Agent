using SpaApi.Data;
using SpaApi.Models;

namespace SpaApi.Services;

/**
 * 使用者服務實作
 */
public class UserService : IUserService
{
    private readonly AppDb _db;

    public UserService(AppDb db)
    {
        _db = db;
    }

    public Task<IEnumerable<UserDto>> GetAllAsync()
    {
        var users = _db.GetAllUsers();
        return Task.FromResult(users.Select(UserDto.FromEntity));
    }

    public Task<UserDto?> GetByIdAsync(int id)
    {
        var user = _db.GetUserById(id);
        return Task.FromResult(user == null ? null : UserDto.FromEntity(user));
    }

    public Task<UserDto> CreateAsync(CreateUserRequest request)
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

        var id = _db.CreateUser(user);
        user.Id = (int)id;

        return Task.FromResult(UserDto.FromEntity(user));
    }

    public Task<UserDto?> UpdateAsync(int id, UpdateUserRequest request)
    {
        var user = _db.GetUserById(id);
        if (user == null) return Task.FromResult<UserDto?>(null);

        if (request.Name != null) user.Name = request.Name;
        if (request.Email != null) user.Email = request.Email;
        if (request.Role != null) user.Role = request.Role;
        if (request.Status != null) user.Status = request.Status;
        if (request.Department != null) user.Department = request.Department;
        if (request.Phone != null) user.Phone = request.Phone;

        _db.UpdateUser(user);

        return Task.FromResult<UserDto?>(UserDto.FromEntity(user));
    }

    public Task<bool> DeleteAsync(int id)
    {
        var affected = _db.DeleteUser(id);
        return Task.FromResult(affected > 0);
    }
}
