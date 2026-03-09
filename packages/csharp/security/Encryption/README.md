# PasswordHasher - Secure Password Hashing

Secure password hashing utility using BCrypt algorithm with built-in password strength validation.

## Features

- BCrypt password hashing (industry-standard)
- Password verification
- Automatic salt generation
- Password strength validation
- Password strength scoring
- Random password generation
- Rehash detection for security upgrades

## Setup

### Install Required NuGet Package

```bash
dotnet add package BCrypt.Net-Next
```

### Register in Dependency Injection

```csharp
// Program.cs
builder.Services.AddSingleton<PasswordHasher>();
```

## Usage

### Basic Password Hashing

```csharp
public class UserService
{
    private readonly PasswordHasher _passwordHasher;

    public UserService(PasswordHasher passwordHasher)
    {
        _passwordHasher = passwordHasher;
    }

    public async Task<User> RegisterUser(RegisterDto dto)
    {
        // Hash the password
        var hashedPassword = _passwordHasher.HashPassword(dto.Password);

        var user = new User
        {
            Username = dto.Username,
            Email = dto.Email,
            PasswordHash = hashedPassword
        };

        await _userRepository.AddAsync(user);
        return user;
    }
}
```

### Password Verification

```csharp
public async Task<bool> Login(LoginDto dto)
{
    var user = await _userRepository.GetByUsernameAsync(dto.Username);
    if (user == null)
        return false;

    // Verify password
    var isValid = _passwordHasher.VerifyPassword(dto.Password, user.PasswordHash);

    if (!isValid)
        return false;

    // Check if password needs rehashing (security upgrade)
    if (_passwordHasher.NeedsRehash(user.PasswordHash))
    {
        // Rehash with updated work factor
        user.PasswordHash = _passwordHasher.HashPassword(dto.Password);
        await _userRepository.UpdateAsync(user);
    }

    return true;
}
```

### Password Strength Validation

```csharp
[HttpPost("validate-password")]
public IActionResult ValidatePassword([FromBody] string password)
{
    var result = _passwordHasher.ValidatePasswordStrength(password);

    if (!result.IsValid)
    {
        return BadRequest(new
        {
            Message = "Password does not meet strength requirements",
            Errors = result.Errors
        });
    }

    return Ok("Password is strong");
}
```

### Password Strength Scoring

```csharp
[HttpPost("check-strength")]
public IActionResult CheckPasswordStrength([FromBody] string password)
{
    var score = _passwordHasher.CalculatePasswordStrength(password);

    var strength = score switch
    {
        0 or 1 => "Very Weak",
        2 => "Weak",
        3 => "Medium",
        4 => "Strong",
        5 => "Very Strong",
        _ => "Unknown"
    };

    return Ok(new
    {
        Score = score,
        Strength = strength
    });
}
```

### Complete Registration Example

```csharp
[HttpPost("register")]
public async Task<IActionResult> Register([FromBody] RegisterDto dto)
{
    // Validate password strength
    var strengthResult = _passwordHasher.ValidatePasswordStrength(dto.Password);
    if (!strengthResult.IsValid)
    {
        return BadRequest(new
        {
            Message = "Password does not meet requirements",
            Errors = strengthResult.Errors
        });
    }

    // Check if username exists
    if (await _userService.UsernameExistsAsync(dto.Username))
        return Conflict("Username already exists");

    // Check if email exists
    if (await _userService.EmailExistsAsync(dto.Email))
        return Conflict("Email already registered");

    // Hash password and create user
    var hashedPassword = _passwordHasher.HashPassword(dto.Password);

    var user = new User
    {
        Username = dto.Username,
        Email = dto.Email,
        PasswordHash = hashedPassword,
        CreatedAt = DateTime.UtcNow
    };

    await _userRepository.AddAsync(user);

    return Created("User registered successfully");
}
```

### Generate Random Password

```csharp
[HttpGet("generate-password")]
public IActionResult GeneratePassword([FromQuery] int length = 16)
{
    var password = _passwordHasher.GenerateRandomPassword(length, includeSpecialChars: true);

    return Ok(new
    {
        Password = password,
        Strength = _passwordHasher.CalculatePasswordStrength(password)
    });
}
```

### Password Reset with Strength Check

```csharp
[HttpPost("reset-password")]
public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
{
    // Validate new password
    var strengthResult = _passwordHasher.ValidatePasswordStrength(dto.NewPassword);
    if (!strengthResult.IsValid)
    {
        return BadRequest(new
        {
            Message = "New password does not meet requirements",
            Errors = strengthResult.Errors
        });
    }

    // Verify reset token
    var user = await _userService.GetByResetTokenAsync(dto.ResetToken);
    if (user == null || user.ResetTokenExpiry < DateTime.UtcNow)
        return BadRequest("Invalid or expired reset token");

    // Update password
    user.PasswordHash = _passwordHasher.HashPassword(dto.NewPassword);
    user.ResetToken = null;
    user.ResetTokenExpiry = null;

    await _userRepository.UpdateAsync(user);

    return Ok("Password reset successfully");
}
```

### Change Password with Old Password Verification

```csharp
[HttpPost("change-password")]
[Authorize]
public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
{
    var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
    var user = await _userRepository.GetByIdAsync(userId);

    // Verify old password
    if (!_passwordHasher.VerifyPassword(dto.OldPassword, user.PasswordHash))
        return BadRequest("Current password is incorrect");

    // Validate new password
    var strengthResult = _passwordHasher.ValidatePasswordStrength(dto.NewPassword);
    if (!strengthResult.IsValid)
        return BadRequest(new { Errors = strengthResult.Errors });

    // Prevent reusing old password
    if (_passwordHasher.VerifyPassword(dto.NewPassword, user.PasswordHash))
        return BadRequest("New password cannot be the same as old password");

    // Update password
    user.PasswordHash = _passwordHasher.HashPassword(dto.NewPassword);
    await _userRepository.UpdateAsync(user);

    return Ok("Password changed successfully");
}
```

## Password Strength Requirements

Default validation requirements:
- Minimum 8 characters
- At least one uppercase letter (A-Z)
- At least one lowercase letter (a-z)
- At least one digit (0-9)
- At least one special character (!@#$%^&* etc.)
- Not a common password (password, 123456, etc.)

## Password Strength Scoring

Score is calculated based on:
- Length (8+ chars = +1, 12+ chars = +1)
- Uppercase letters (+1)
- Lowercase letters (+1)
- Digits (+1)
- Special characters (+1)
- Penalties for repeating characters (-1)
- Penalties for sequential numbers (-1)

Final score: 0-5
- 0-1: Very Weak
- 2: Weak
- 3: Medium
- 4: Strong
- 5: Very Strong

## BCrypt Work Factor

Default work factor: 12 (2^12 = 4,096 iterations)

Higher work factor = more secure but slower hashing:
- 10: Fast, minimal security (not recommended)
- 12: Good balance (recommended)
- 14: High security, slower
- 16: Very high security, very slow

## Security Best Practices

1. **Never Store Plain Passwords** - Always hash before storing
2. **Use BCrypt** - Industry-standard, designed for password hashing
3. **Automatic Salting** - BCrypt includes unique salt for each hash
4. **Work Factor** - Adjust based on security requirements
5. **Rehashing** - Update old hashes when work factor increases
6. **Password Policy** - Enforce minimum strength requirements
7. **Rate Limiting** - Prevent brute-force attacks on login
8. **Secure Transport** - Always use HTTPS
9. **No Password Hints** - Don't store or display password hints

## Benefits

1. **BCrypt Algorithm** - Designed specifically for password hashing
2. **Automatic Salting** - Unique salt generated for each password
3. **Slow by Design** - Resistant to brute-force attacks
4. **Future-Proof** - Work factor can be increased as hardware improves
5. **Strength Validation** - Built-in password policy enforcement
6. **Random Generation** - Generate secure temporary passwords
7. **Rehash Detection** - Automatically upgrade old hashes

## Dependencies

- BCrypt.Net-Next (NuGet package)
- .NET 6.0 or higher

## Example DTOs

```csharp
public class RegisterDto
{
    public string Username { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
}

public class LoginDto
{
    public string Username { get; set; }
    public string Password { get; set; }
}

public class ChangePasswordDto
{
    public string OldPassword { get; set; }
    public string NewPassword { get; set; }
}

public class ResetPasswordDto
{
    public string ResetToken { get; set; }
    public string NewPassword { get; set; }
}
```
