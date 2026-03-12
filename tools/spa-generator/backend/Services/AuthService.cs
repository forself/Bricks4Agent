using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using SpaGenerator.Data;
using SpaGenerator.Models;

namespace SpaGenerator.Services;

/**
 * 認證服務實作
 */
public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AuthService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<LoginResult?> LoginAsync(string email, string password)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return null;

        if (!BCryptHelper.VerifyPassword(password, user.PasswordHash))
        {
            return null;
        }

        // 更新最後登入時間
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // 產生 Token
        var accessToken = GenerateJwtToken(user);
        var refreshToken = Guid.NewGuid().ToString();

        return new LoginResult
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
        };
    }

    public async Task<RegisterResult> RegisterAsync(RegisterRequest request)
    {
        // 檢查 Email 是否已存在
        var exists = await _db.Users.AnyAsync(u => u.Email == request.Email);
        if (exists)
        {
            return new RegisterResult
            {
                Success = false,
                Message = "Email 已被使用"
            };
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

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return new RegisterResult
        {
            Success = true,
            Message = "註冊成功",
            UserId = user.Id
        };
    }

    private string GenerateJwtToken(User user)
    {
        var key = _config["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT Key 未設定。請在 appsettings.json 或環境變數中設定 Jwt:Key (至少 32 字元)");
        var issuer = _config["Jwt:Issuer"] ?? "SpaGenerator";

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
