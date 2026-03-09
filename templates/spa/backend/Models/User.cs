using BaseOrm;

namespace SpaApi.Models;

/**
 * 使用者實體
 */
[Table("Users")]
public class User
{
    [Key]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "user";
    public string Status { get; set; } = "active";
    public string? Department { get; set; }
    public string? Phone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}

/**
 * 使用者 DTO (不含敏感資訊)
 */
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Phone { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public static UserDto FromEntity(User user) => new()
    {
        Id = user.Id,
        Name = user.Name,
        Email = user.Email,
        Role = user.Role,
        Status = user.Status,
        Department = user.Department,
        Phone = user.Phone,
        CreatedAt = user.CreatedAt,
        LastLoginAt = user.LastLoginAt
    };
}
