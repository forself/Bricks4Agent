# BaseController - API Controller Base Class

Base controller class that provides common functionality for all API controllers.

## Features

- Unified response format using ApiResponse
- Built-in user authentication helpers
- Common HTTP response methods (200, 201, 400, 401, 403, 404, 409, 500)
- ModelState validation helpers
- Action execution wrappers with error handling
- Automatic trace ID assignment

## Usage

### Basic Controller Implementation

```csharp
[Route("api/[controller]")]
public class UsersController : BaseController
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(int id)
    {
        var user = await _userService.GetByIdAsync(id);

        if (user == null)
            return NotFound("User not found");

        return Success(user, "User retrieved successfully");
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequestWithModelState();

        var user = await _userService.CreateAsync(dto);
        return Created(user, "User created successfully");
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto dto)
    {
        var user = await _userService.UpdateAsync(id, dto);

        if (user == null)
            return NotFound("User not found");

        return Success(user, "User updated successfully");
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var success = await _userService.DeleteAsync(id);

        if (!success)
            return NotFound("User not found");

        return Success("User deleted successfully");
    }
}
```

### Using Current User Information

```csharp
[HttpGet("profile")]
[Authorize]
public async Task<IActionResult> GetCurrentUserProfile()
{
    // Access current user information
    var userId = CurrentUserId;
    var username = CurrentUsername;
    var email = CurrentUserEmail;
    var roles = CurrentUserRoles;

    if (!userId.HasValue)
        return Unauthorized();

    var profile = await _userService.GetProfileAsync(userId.Value);
    return Success(profile);
}

[HttpPost("admin-only")]
[Authorize]
public IActionResult AdminOnlyAction()
{
    if (!HasRole("Admin"))
        return Forbidden("Admin access required");

    // Admin-only logic here
    return Success("Action completed");
}
```

### Using Execute Helpers

```csharp
// Execute without return value
[HttpPost("send-email")]
public async Task<IActionResult> SendEmail([FromBody] EmailDto dto)
{
    return await ExecuteActionAsync(
        async () => await _emailService.SendAsync(dto),
        "Email sent successfully"
    );
}

// Execute with return value
[HttpGet("{id}")]
public async Task<IActionResult> GetUser(int id)
{
    return await ExecuteFunctionAsync(
        async () => await _userService.GetByIdAsync(id),
        "User retrieved successfully"
    );
}
```

### Custom Error Responses

```csharp
[HttpPost("register")]
public async Task<IActionResult> Register([FromBody] RegisterDto dto)
{
    if (await _userService.UsernameExistsAsync(dto.Username))
        return Conflict("Username already exists");

    if (await _userService.EmailExistsAsync(dto.Email))
        return Conflict("Email already registered");

    var user = await _userService.RegisterAsync(dto);
    return Created(user, "Registration successful");
}
```

### Validation Errors

```csharp
[HttpPost("validate")]
public IActionResult ValidateData([FromBody] DataDto dto)
{
    var errors = new List<string>();

    if (string.IsNullOrEmpty(dto.Name))
        errors.Add("Name is required");

    if (dto.Age < 18)
        errors.Add("Age must be at least 18");

    if (errors.Any())
        return BadRequest(errors, "Validation failed");

    return Success("Validation passed");
}

// Or use ModelState
[HttpPost("validate-model")]
public IActionResult ValidateModel([FromBody] DataDto dto)
{
    if (!ModelState.IsValid)
        return BadRequestWithModelState();

    return Success("Validation passed");
}
```

## Available Response Methods

### Success Responses

- `Success<T>(T data, string message)` - 200 OK with data
- `Success(string message)` - 200 OK without data
- `Created<T>(T data, string message)` - 201 Created with data
- `NoContent()` - 204 No Content

### Error Responses

- `BadRequest(string message)` - 400 Bad Request
- `BadRequest(List<string> errors, string message)` - 400 with validation errors
- `Unauthorized(string message)` - 401 Unauthorized
- `Forbidden(string message)` - 403 Forbidden
- `NotFound(string message)` - 404 Not Found
- `Conflict(string message)` - 409 Conflict
- `InternalServerError(string message)` - 500 Internal Server Error
- `InternalServerError(Exception ex)` - 500 with exception details

## Available Properties

- `CurrentUserId` - Get current user ID from JWT claims
- `CurrentUsername` - Get current username from JWT claims
- `CurrentUserEmail` - Get current user email from JWT claims
- `CurrentUserRoles` - Get current user roles from JWT claims
- `TraceId` - Get request trace ID for logging

## Available Helper Methods

- `HasRole(string role)` - Check if current user has specific role
- `GetModelStateErrors()` - Get all ModelState validation errors
- `BadRequestWithModelState()` - Return 400 with ModelState errors
- `ExecuteAction(Action action, string successMessage)` - Execute action with error handling
- `ExecuteActionAsync(Func<Task> action, string successMessage)` - Execute async action with error handling
- `ExecuteFunction<T>(Func<T> func, string successMessage)` - Execute function with error handling
- `ExecuteFunctionAsync<T>(Func<Task<T>> func, string successMessage)` - Execute async function with error handling

## Dependencies

- Microsoft.AspNetCore.Mvc (ASP.NET Core)
- ApiResponse component
- .NET 6.0 or higher

## Benefits

1. **DRY Principle** - Eliminate repetitive response code
2. **Consistency** - All controllers return uniform responses
3. **Error Handling** - Built-in exception handling
4. **User Context** - Easy access to authenticated user information
5. **Trace ID** - Automatic request tracking for debugging
6. **Type Safety** - Strongly-typed responses
