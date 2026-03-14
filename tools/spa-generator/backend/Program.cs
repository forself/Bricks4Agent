using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using SpaGenerator.Data;
using SpaGenerator.Services;

/**
 * SPA API - ASP.NET Core 8 Minimal API
 * 使用 SQLite + BaseOrm
 *
 * 資安加強:
 * - 速率限制 (Rate Limiting)
 * - 輸入驗證
 * - 安全標頭
 * - CORS 限制
 */

var builder = WebApplication.CreateBuilder(args);

// ===== 服務註冊 =====

// SQLite 資料庫 (BaseOrm)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=generator.db";
builder.Services.AddSingleton(new AppDb(connectionString));

// JWT 認證 - 生產環境必須設定 Jwt:Key (透過環境變數 Jwt__Key)
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrEmpty(jwtKey) || jwtKey.Length < 32)
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "JWT Key 未設定或長度不足。請透過環境變數 Jwt__Key 設定 (至少 32 字元)");
    }
    // 開發環境使用預設值 (僅供開發，寫回 Configuration 讓 AuthService 也能讀取)
    jwtKey = "DevOnlyKey_DoNotUseInProduction_32chars!";
    builder.Configuration["Jwt:Key"] = jwtKey;
    Console.WriteLine("警告: 使用開發環境預設 JWT Key，請勿在生產環境使用！");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "SpaGenerator";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtIssuer,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1) // 減少時鐘偏差容忍度
        };
    });

builder.Services.AddAuthorization();

// 速率限制 - 防止暴力破解
builder.Services.AddRateLimiter(options =>
{
    // 登入端點: 每分鐘最多 5 次嘗試
    options.AddFixedWindowLimiter("login", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    // 註冊端點: 每小時最多 10 次
    options.AddFixedWindowLimiter("register", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromHours(1);
        opt.QueueLimit = 0;
    });

    // 一般 API: 每分鐘最多 100 次
    options.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 2;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// CORS - 生產環境務必設定具體的允許來源
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:3080" };

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
        }
    });
});

// 服務
builder.Services.AddSingleton<IUserService, UserService>();
builder.Services.AddSingleton<IAuthService, AuthService>();

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// ===== 初始化資料庫 =====
{
    var db = app.Services.GetRequiredService<AppDb>();
    var config = app.Services.GetRequiredService<IConfiguration>();
    DbInitializer.Initialize(db, config);
}

// ===== 中介軟體 =====

// 安全標頭
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

    if (!app.Environment.IsDevelopment())
    {
        context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
    }

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    // 生產環境不暴露詳細錯誤
    app.UseExceptionHandler(appError =>
    {
        appError.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "Internal server error" });
        });
    });
}

app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// ===== API 端點 =====

// 健康檢查
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    database = "SQLite"
})).WithName("HealthCheck");

// 認證端點 - 帶速率限制
app.MapPost("/api/auth/login", async (LoginRequest request, IAuthService authService) =>
{
    // 輸入驗證
    var validation = ValidateLoginRequest(request);
    if (!validation.IsValid)
    {
        return Results.BadRequest(new { error = validation.Error });
    }

    var result = await authService.LoginAsync(request.Email.Trim().ToLowerInvariant(), request.Password);
    if (result == null)
    {
        // 不透露是 Email 還是密碼錯誤
        return Results.Unauthorized();
    }
    return Results.Ok(result);
}).WithName("Login").AllowAnonymous().RequireRateLimiting("login");

app.MapPost("/api/auth/register", async (RegisterRequest request, IAuthService authService) =>
{
    // 輸入驗證
    var validation = ValidateRegisterRequest(request);
    if (!validation.IsValid)
    {
        return Results.BadRequest(new { error = validation.Error });
    }

    var result = await authService.RegisterAsync(request with
    {
        Email = request.Email.Trim().ToLowerInvariant(),
        Name = request.Name.Trim()
    });

    if (!result.Success)
    {
        return Results.BadRequest(new { error = result.Message });
    }
    return Results.Created($"/api/users/{result.UserId}", result);
}).WithName("Register").AllowAnonymous().RequireRateLimiting("register");

// 使用者端點 - 帶速率限制
app.MapGet("/api/users", async (IUserService userService) =>
{
    var users = await userService.GetAllAsync();
    return Results.Ok(users);
}).WithName("GetUsers").RequireAuthorization().RequireRateLimiting("api");

app.MapGet("/api/users/{id:int}", async (int id, IUserService userService) =>
{
    if (id <= 0) return Results.BadRequest(new { error = "Invalid user ID" });

    var user = await userService.GetByIdAsync(id);
    if (user == null)
    {
        return Results.NotFound(new { error = "User not found" });
    }
    return Results.Ok(user);
}).WithName("GetUserById").RequireAuthorization().RequireRateLimiting("api");

app.MapPost("/api/users", async (CreateUserRequest request, IUserService userService) =>
{
    // 輸入驗證
    var validation = ValidateCreateUserRequest(request);
    if (!validation.IsValid)
    {
        return Results.BadRequest(new { error = validation.Error });
    }

    var user = await userService.CreateAsync(request with
    {
        Email = request.Email.Trim().ToLowerInvariant(),
        Name = request.Name.Trim()
    });
    return Results.Created($"/api/users/{user.Id}", user);
}).WithName("CreateUser").RequireAuthorization().RequireRateLimiting("api");

app.MapPut("/api/users/{id:int}", async (int id, UpdateUserRequest request, IUserService userService) =>
{
    if (id <= 0) return Results.BadRequest(new { error = "Invalid user ID" });

    var user = await userService.UpdateAsync(id, request);
    if (user == null)
    {
        return Results.NotFound(new { error = "User not found" });
    }
    return Results.Ok(user);
}).WithName("UpdateUser").RequireAuthorization().RequireRateLimiting("api");

app.MapDelete("/api/users/{id:int}", async (int id, IUserService userService) =>
{
    if (id <= 0) return Results.BadRequest(new { error = "Invalid user ID" });

    var success = await userService.DeleteAsync(id);
    if (!success)
    {
        return Results.NotFound(new { error = "User not found" });
    }
    return Results.Ok(new { message = "User deleted", id });
}).WithName("DeleteUser").RequireAuthorization().RequireRateLimiting("api");

// 儀表板端點
app.MapGet("/api/dashboard", (AppDb db) =>
{
    var userCount = db.GetUserCount();
    return Results.Ok(new
    {
        stats = new
        {
            users = userCount,
            orders = 0,
            revenue = 0
        },
        activities = new[]
        {
            new { id = 1, action = "System started", user = "System", time = DateTime.UtcNow.ToString("g") }
        }
    });
}).WithName("GetDashboard").RequireAuthorization().RequireRateLimiting("api");

app.Run();

// ===== 輸入驗證函數 =====

static ValidationResult ValidateLoginRequest(LoginRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Email))
        return ValidationResult.Fail("Email 不能為空");

    if (!IsValidEmail(request.Email))
        return ValidationResult.Fail("Email 格式不正確");

    if (string.IsNullOrWhiteSpace(request.Password))
        return ValidationResult.Fail("密碼不能為空");

    if (request.Password.Length > 128)
        return ValidationResult.Fail("密碼長度不能超過 128 字元");

    return ValidationResult.Success();
}

static ValidationResult ValidateRegisterRequest(RegisterRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return ValidationResult.Fail("姓名不能為空");

    if (request.Name.Length > 100)
        return ValidationResult.Fail("姓名長度不能超過 100 字元");

    if (string.IsNullOrWhiteSpace(request.Email))
        return ValidationResult.Fail("Email 不能為空");

    if (!IsValidEmail(request.Email))
        return ValidationResult.Fail("Email 格式不正確");

    if (string.IsNullOrWhiteSpace(request.Password))
        return ValidationResult.Fail("密碼不能為空");

    if (request.Password.Length < 8)
        return ValidationResult.Fail("密碼長度至少 8 個字元");

    if (request.Password.Length > 128)
        return ValidationResult.Fail("密碼長度不能超過 128 字元");

    return ValidationResult.Success();
}

static ValidationResult ValidateCreateUserRequest(CreateUserRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return ValidationResult.Fail("姓名不能為空");

    if (request.Name.Length > 100)
        return ValidationResult.Fail("姓名長度不能超過 100 字元");

    if (string.IsNullOrWhiteSpace(request.Email))
        return ValidationResult.Fail("Email 不能為空");

    if (!IsValidEmail(request.Email))
        return ValidationResult.Fail("Email 格式不正確");

    var validRoles = new[] { "user", "editor", "admin" };
    if (!validRoles.Contains(request.Role?.ToLowerInvariant()))
        return ValidationResult.Fail("無效的角色");

    return ValidationResult.Success();
}

static bool IsValidEmail(string email)
{
    if (string.IsNullOrWhiteSpace(email) || email.Length > 254)
        return false;

    // RFC 5322 簡化版本
    return Regex.IsMatch(email.Trim(),
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
}

// ===== Request/Response DTOs =====
public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Name, string Email, string Password);
public record CreateUserRequest(string Name, string Email, string? Password, string Role, string? Department, string? Phone);
public record UpdateUserRequest(string? Name, string? Email, string? Role, string? Status, string? Department, string? Phone);

public record ValidationResult(bool IsValid, string? Error)
{
    public static ValidationResult Success() => new(true, null);
    public static ValidationResult Fail(string error) => new(false, error);
}
