# JwtHelper - JWT Token Management

Helper class for generating and validating JWT (JSON Web Tokens) for authentication and authorization.

## Features

- JWT access token generation
- Refresh token generation
- Token validation
- Claims extraction (user ID, username, email, roles)
- Token expiration checking
- Custom claims support
- Configurable via appsettings.json

## Setup

### 1. Install Required NuGet Packages

```bash
dotnet add package System.IdentityModel.Tokens.Jwt
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
```

### 2. Configure appsettings.json

```json
{
  "Jwt": {
    "SecretKey": "your-super-secret-key-that-is-at-least-32-characters-long",
    "Issuer": "YourAppName",
    "Audience": "YourAppUsers",
    "ExpirationMinutes": 60
  }
}
```

### 3. Register JWT Authentication in Program.cs

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Register JwtHelper
builder.Services.AddSingleton<JwtHelper>();

// Configure JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt");
var secretKey = jwtSettings["SecretKey"];

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.Zero
    };
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

## Usage

### Generate Access Token

```csharp
public class AuthService
{
    private readonly JwtHelper _jwtHelper;

    public AuthService(JwtHelper jwtHelper)
    {
        _jwtHelper = jwtHelper;
    }

    public string Login(User user)
    {
        // Generate token with user information
        var token = _jwtHelper.GenerateToken(
            userId: user.Id,
            username: user.Username,
            email: user.Email,
            roles: new[] { "User", "Admin" }
        );

        return token;
    }
}
```

### Generate Token with Custom Claims

```csharp
var additionalClaims = new Dictionary<string, string>
{
    { "department", "Engineering" },
    { "employeeId", "EMP-12345" },
    { "tenantId", "TENANT-001" }
};

var token = _jwtHelper.GenerateToken(
    userId: user.Id,
    username: user.Username,
    email: user.Email,
    roles: new[] { "User" },
    additionalClaims: additionalClaims
);
```

### Generate Refresh Token

```csharp
public (string accessToken, string refreshToken) Login(User user)
{
    var accessToken = _jwtHelper.GenerateToken(
        userId: user.Id,
        username: user.Username,
        email: user.Email,
        roles: user.Roles
    );

    var refreshToken = _jwtHelper.GenerateRefreshToken();

    // Store refresh token in database
    await _userRepository.SaveRefreshToken(user.Id, refreshToken);

    return (accessToken, refreshToken);
}
```

### Validate Token

```csharp
[HttpGet("validate")]
public IActionResult ValidateToken([FromHeader] string authorization)
{
    var token = authorization?.Replace("Bearer ", "");

    var principal = _jwtHelper.ValidateToken(token);

    if (principal == null)
        return Unauthorized("Invalid token");

    return Ok("Token is valid");
}
```

### Extract Claims from Token

```csharp
// Get user ID
var userId = _jwtHelper.GetUserIdFromToken(token);

// Get username
var username = _jwtHelper.GetUsernameFromToken(token);

// Get email
var email = _jwtHelper.GetEmailFromToken(token);

// Get roles
var roles = _jwtHelper.GetRolesFromToken(token);

// Get custom claim
var department = _jwtHelper.GetClaimFromToken(token, "department");

// Get all claims
var allClaims = _jwtHelper.GetAllClaims(token);
```

### Check Token Expiration

```csharp
if (_jwtHelper.IsTokenExpired(token))
{
    return Unauthorized("Token has expired");
}

var expiration = _jwtHelper.GetTokenExpiration(token);
var timeRemaining = expiration - DateTime.UtcNow;
```

### Complete Login Controller Example

```csharp
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly JwtHelper _jwtHelper;
    private readonly IUserService _userService;
    private readonly PasswordHasher _passwordHasher;

    public AuthController(JwtHelper jwtHelper, IUserService userService, PasswordHasher passwordHasher)
    {
        _jwtHelper = jwtHelper;
        _userService = userService;
        _passwordHasher = passwordHasher;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        // Find user
        var user = await _userService.GetByUsernameAsync(dto.Username);
        if (user == null)
            return Unauthorized("Invalid credentials");

        // Verify password
        if (!_passwordHasher.VerifyPassword(dto.Password, user.PasswordHash))
            return Unauthorized("Invalid credentials");

        // Generate tokens
        var accessToken = _jwtHelper.GenerateToken(
            userId: user.Id,
            username: user.Username,
            email: user.Email,
            roles: user.Roles
        );

        var refreshToken = _jwtHelper.GenerateRefreshToken();

        // Save refresh token
        await _userService.SaveRefreshTokenAsync(user.Id, refreshToken);

        return Ok(new
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = 3600 // seconds
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenDto dto)
    {
        // Validate refresh token
        var user = await _userService.GetByRefreshTokenAsync(dto.RefreshToken);
        if (user == null)
            return Unauthorized("Invalid refresh token");

        // Generate new access token
        var accessToken = _jwtHelper.GenerateToken(
            userId: user.Id,
            username: user.Username,
            email: user.Email,
            roles: user.Roles
        );

        return Ok(new { AccessToken = accessToken });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
        await _userService.RevokeRefreshTokenAsync(userId);

        return Ok("Logged out successfully");
    }
}
```

### Protecting Endpoints with JWT

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    // Require authentication
    [HttpGet]
    [Authorize]
    public IActionResult GetAll()
    {
        return Ok("Protected endpoint");
    }

    // Require specific role
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public IActionResult Delete(int id)
    {
        return Ok("Admin only endpoint");
    }

    // Require multiple roles
    [HttpPost("important")]
    [Authorize(Roles = "Admin,SuperUser")]
    public IActionResult ImportantAction()
    {
        return Ok("Admin or SuperUser endpoint");
    }
}
```

## Token Structure

Generated token includes these claims:
- `NameIdentifier` - User ID
- `Name` - Username
- `Email` - User email
- `Role` - User roles (can be multiple)
- `sub` - Subject (User ID)
- `jti` - JWT ID (unique identifier)
- `iat` - Issued at timestamp
- Custom claims (if provided)

## Security Best Practices

1. **Secret Key**: Use a strong, randomly generated secret key (at least 32 characters)
2. **HTTPS Only**: Always use HTTPS in production
3. **Token Expiration**: Set reasonable expiration times (15-60 minutes for access tokens)
4. **Refresh Tokens**: Use refresh tokens for long-lived sessions
5. **Token Storage**: Store tokens securely on client (HttpOnly cookies recommended)
6. **Token Revocation**: Implement blacklist or database check for revoked tokens
7. **Environment Variables**: Store secret key in environment variables, not appsettings.json

## Dependencies

- System.IdentityModel.Tokens.Jwt
- Microsoft.IdentityModel.Tokens
- Microsoft.AspNetCore.Authentication.JwtBearer
- .NET 6.0 or higher

## Benefits

1. **Stateless Authentication** - No server-side session storage needed
2. **Scalability** - Works well with distributed systems
3. **Cross-Domain** - Can be used across different domains
4. **Mobile-Friendly** - Easy to implement in mobile apps
5. **Type Safety** - Strongly-typed claim extraction
6. **Flexible** - Support for custom claims
7. **Secure** - Industry-standard token format
