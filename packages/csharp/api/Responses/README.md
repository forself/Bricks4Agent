# ApiResponse - Unified API Response Format

A generic response wrapper for consistent API responses across your application.

## Features

- Generic and non-generic versions
- Built-in factory methods for common response types
- Automatic timestamp tracking
- Support for multiple error messages
- Trace ID support for debugging
- Standard HTTP status codes

## Usage

### Basic Success Response

```csharp
// With data
var response = ApiResponse<UserDto>.SuccessResponse(
    data: userDto,
    message: "User retrieved successfully"
);

// Without data
var response = ApiResponse.SuccessResponse("Operation completed");
```

### Error Response

```csharp
// Single error
var response = ApiResponse<UserDto>.ErrorResponse(
    message: "Invalid user ID",
    statusCode: 400
);

// Multiple errors (e.g., validation errors)
var errors = new List<string>
{
    "Email is required",
    "Password must be at least 8 characters"
};
var response = ApiResponse<UserDto>.ErrorResponse(
    errors: errors,
    message: "Validation failed",
    statusCode: 400
);
```

### Common HTTP Response Types

```csharp
// 404 Not Found
var response = ApiResponse<UserDto>.NotFoundResponse("User not found");

// 401 Unauthorized
var response = ApiResponse<UserDto>.UnauthorizedResponse();

// 403 Forbidden
var response = ApiResponse<UserDto>.ForbiddenResponse("Insufficient permissions");
```

### In Controller Actions

```csharp
[HttpGet("{id}")]
public async Task<IActionResult> GetUser(int id)
{
    try
    {
        var user = await _userService.GetByIdAsync(id);

        if (user == null)
        {
            var notFoundResponse = ApiResponse<UserDto>.NotFoundResponse("User not found");
            return NotFound(notFoundResponse);
        }

        var response = ApiResponse<UserDto>.SuccessResponse(user);
        return Ok(response);
    }
    catch (Exception ex)
    {
        var errorResponse = ApiResponse<UserDto>.ErrorResponse(
            message: ex.Message,
            statusCode: 500
        );
        return StatusCode(500, errorResponse);
    }
}
```

### Adding Trace ID

```csharp
var response = ApiResponse<UserDto>.SuccessResponse(userDto);
response.TraceId = HttpContext.TraceIdentifier;
return Ok(response);
```

## Response Format

```json
{
  "success": true,
  "statusCode": 200,
  "message": "Success",
  "data": { ... },
  "errors": [],
  "timestamp": "2026-01-23T10:30:00Z",
  "traceId": "0HMVFE42N8GJ7:00000001"
}
```

## Benefits

1. **Consistency** - All API responses follow the same format
2. **Error Handling** - Standardized error response structure
3. **Debugging** - Built-in timestamp and trace ID
4. **Client-Friendly** - Easy to parse and handle on client side
5. **Type Safety** - Generic type support for strongly-typed data

## Dependencies

- .NET 6.0 or higher
- No external NuGet packages required
