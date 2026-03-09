# ExceptionMiddleware - Global Exception Handling

Global exception handling middleware for ASP.NET Core applications.

## Features

- Centralized exception handling
- Custom exception types (ValidationException, NotFoundException, etc.)
- Automatic error logging
- Consistent error response format
- Environment-aware error details (development vs production)
- Automatic status code mapping
- Trace ID tracking

## Setup

### 1. Register Middleware in Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();

var app = builder.Build();

// Add global exception handler (place early in pipeline)
app.UseGlobalExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

## Usage

### Using Custom Exceptions in Controllers

```csharp
[HttpGet("{id}")]
public async Task<IActionResult> GetUser(int id)
{
    var user = await _userService.GetByIdAsync(id);

    if (user == null)
        throw new NotFoundException("User", id);
        // Returns 404 with message: "User with key '123' was not found"

    return Ok(user);
}

[HttpPost]
public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
{
    if (string.IsNullOrEmpty(dto.Email))
        throw new ValidationException("Email is required");
        // Returns 400 with validation error

    if (await _userService.EmailExistsAsync(dto.Email))
        throw new ConflictException("Email already exists");
        // Returns 409 conflict

    var user = await _userService.CreateAsync(dto);
    return Ok(user);
}

[HttpPut("{id}")]
[Authorize]
public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto dto)
{
    if (!User.Identity.IsAuthenticated)
        throw new UnauthorizedException();
        // Returns 401

    if (!HasPermission(id))
        throw new ForbiddenException("You don't have permission to update this user");
        // Returns 403

    var user = await _userService.UpdateAsync(id, dto);
    return Ok(user);
}
```

### Custom Exception Types

```csharp
// Validation error
throw new ValidationException("Invalid email format");

// Resource not found
throw new NotFoundException("User not found");
throw new NotFoundException("User", userId); // "User with key '123' was not found"

// Unauthorized access
throw new UnauthorizedException();
throw new UnauthorizedException("Invalid token");

// Forbidden access
throw new ForbiddenException();
throw new ForbiddenException("Admin access required");

// Resource conflict
throw new ConflictException("Username already exists");
```

### Built-in Exception Mapping

The middleware automatically maps built-in .NET exceptions:

```csharp
// ArgumentNullException -> 400 Bad Request
if (dto == null)
    throw new ArgumentNullException(nameof(dto));

// ArgumentException -> 400 Bad Request
if (id <= 0)
    throw new ArgumentException("ID must be positive", nameof(id));

// InvalidOperationException -> 400 Bad Request
if (!IsValidState())
    throw new InvalidOperationException("Cannot perform this operation in current state");

// UnauthorizedAccessException -> 401 Unauthorized
if (!HasAccess())
    throw new UnauthorizedAccessException();
```

### Error Response Format

All exceptions are converted to consistent ApiResponse format:

```json
{
  "success": false,
  "statusCode": 404,
  "message": "User with key '123' was not found",
  "data": null,
  "errors": [
    "User with key '123' was not found"
  ],
  "timestamp": "2026-01-23T10:30:00Z",
  "traceId": "0HMVFE42N8GJ7:00000001"
}
```

In development environment (DEBUG mode), additional details are included:

```json
{
  "success": false,
  "statusCode": 500,
  "message": "An internal server error occurred",
  "data": null,
  "errors": [
    "Object reference not set to an instance of an object.",
    "StackTrace: at MyApp.Services.UserService.GetByIdAsync(Int32 id)...",
    "InnerException: Connection to database failed"
  ],
  "timestamp": "2026-01-23T10:30:00Z",
  "traceId": "0HMVFE42N8GJ7:00000001"
}
```

## Exception Status Code Mapping

| Exception Type | HTTP Status Code | Include Details |
|----------------|------------------|-----------------|
| ValidationException | 400 Bad Request | Yes |
| NotFoundException | 404 Not Found | Yes |
| UnauthorizedException | 401 Unauthorized | No |
| ForbiddenException | 403 Forbidden | No |
| ConflictException | 409 Conflict | Yes |
| ArgumentNullException | 400 Bad Request | Yes |
| ArgumentException | 400 Bad Request | Yes |
| InvalidOperationException | 400 Bad Request | Yes |
| UnauthorizedAccessException | 401 Unauthorized | No |
| All Others | 500 Internal Server Error | Yes |

## Logging

All exceptions are automatically logged with:
- Exception details
- Stack trace
- Trace ID for request correlation

```csharp
_logger.LogError(exception, "Unhandled exception occurred. TraceId: {TraceId}", context.TraceIdentifier);
```

## Creating Custom Exception Types

```csharp
public class PaymentFailedException : Exception
{
    public string TransactionId { get; }

    public PaymentFailedException(string message, string transactionId)
        : base(message)
    {
        TransactionId = transactionId;
    }
}

// Update GetErrorDetails in ExceptionMiddleware
private (HttpStatusCode statusCode, string message, bool includeDetails) GetErrorDetails(Exception exception)
{
    return exception switch
    {
        PaymentFailedException => (HttpStatusCode.PaymentRequired, exception.Message, true),
        ValidationException => (HttpStatusCode.BadRequest, exception.Message, true),
        // ... other cases
        _ => (HttpStatusCode.InternalServerError, "An internal server error occurred", true)
    };
}
```

## Dependencies

- Microsoft.AspNetCore.Http
- Microsoft.Extensions.Logging
- System.Text.Json
- ApiResponse component
- .NET 6.0 or higher

## Benefits

1. **Centralized Handling** - All exceptions handled in one place
2. **Consistent Responses** - Uniform error format across the application
3. **Automatic Logging** - All errors logged automatically
4. **Type Safety** - Custom exception types for different scenarios
5. **Development-Friendly** - Detailed error info in development mode
6. **Production-Safe** - Minimal error exposure in production
7. **Request Tracking** - Trace ID for debugging
