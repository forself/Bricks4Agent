using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using SpaApi.Data;
using SpaApi.Models;

namespace SpaApi.Services;

/**
 * 認證服務實作
 */
public class AuthService : IAuthService
{
    private readonly AppDb _db;
    private readonly IConfiguration _config;

    public AuthService(AppDb db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public Task<LoginResult?> LoginAsync(string email, string password)
    {
        var user = _db.GetUserByEmail(email);
        if (user == null) return Task.FromResult<LoginResult?>(null);

        if (!BCryptHelper.VerifyPassword(password, user.PasswordHash))
        {
            return Task.FromResult<LoginResult?>(null);
        }

        // 更新最後登入時間
        _db.UpdateLastLogin(user.Id);

        // 產生 Token
        var accessToken = GenerateJwtToken(user);
        var refreshToken = Guid.NewGuid().ToString();

        return Task.FromResult<LoginResult?>(new LoginResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            User = new UserInfo
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role
            }
        });
    }

    public Task<RegisterResult> RegisterAsync(RegisterRequest request)
    {
        // 檢查 Email 是否已存在
        if (_db.EmailExists(request.Email))
        {
            return Task.FromResult(new RegisterResult
            {
                Success = false,
                Message = "Email 已被使用"
            });
        }

        var user = new User
        {
            Name = request.Name,
            Email = request.Email,
            PasswordHash = BCryptHelper.HashPassword(request.Password),
            Role = "user",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };

        var id = _db.CreateUser(user);

        return Task.FromResult(new RegisterResult
        {
            Success = true,
            Message = "註冊成功",
            UserId = (int)id
        });
    }

    private string GenerateJwtToken(User user)
    {
        var key = _config["Jwt:Key"] ?? "YourSuperSecretKeyHere_AtLeast32Characters!";
        var issuer = _config["Jwt:Issuer"] ?? "SpaApi";

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(24),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
