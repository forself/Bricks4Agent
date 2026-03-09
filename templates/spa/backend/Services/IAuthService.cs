namespace SpaApi.Services;

/**
 * 認證服務介面
 */
public interface IAuthService
{
    Task<LoginResult?> LoginAsync(string email, string password);
    Task<RegisterResult> RegisterAsync(RegisterRequest request);
}

public class LoginResult
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public UserInfo User { get; set; } = new();
}

public class UserInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class RegisterResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? UserId { get; set; }
}
