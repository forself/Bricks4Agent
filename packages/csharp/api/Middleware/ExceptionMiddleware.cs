using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Bricks4Agent.Api.Responses;

namespace Bricks4Agent.Api.Middleware
{
    /// <summary>
    /// Global exception handling middleware
    /// </summary>
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;

        /// <summary>
        /// Constructor
        /// </summary>
        public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        /// <summary>
        /// Invoke middleware
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        /// <summary>
        /// Handle exception and return formatted error response
        /// </summary>
        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            // Log the exception
            _logger.LogError(exception, "Unhandled exception occurred. TraceId: {TraceId}", context.TraceIdentifier);

            // Determine status code and message based on exception type
            var (statusCode, message, includeDetails) = GetErrorDetails(exception);

            // Create response
            var response = new ApiResponse
            {
                Success = false,
                StatusCode = (int)statusCode,
                Message = message,
                TraceId = context.TraceIdentifier,
                Timestamp = DateTime.UtcNow
            };

            // Add error details in development environment
            if (includeDetails)
            {
                response.Errors.Add(exception.Message);
#if DEBUG
                response.Errors.Add($"StackTrace: {exception.StackTrace}");
                if (exception.InnerException != null)
                {
                    response.Errors.Add($"InnerException: {exception.InnerException.Message}");
                }
#endif
            }

            // Set response
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)statusCode;

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(response, options);
            await context.Response.WriteAsync(json);
        }

        /// <summary>
        /// Get error details based on exception type
        /// </summary>
        private (HttpStatusCode statusCode, string message, bool includeDetails) GetErrorDetails(Exception exception)
        {
            return exception switch
            {
                // Custom exceptions
                ValidationException => (HttpStatusCode.BadRequest, exception.Message, true),
                NotFoundException => (HttpStatusCode.NotFound, exception.Message, true),
                UnauthorizedException => (HttpStatusCode.Unauthorized, exception.Message, false),
                ForbiddenException => (HttpStatusCode.Forbidden, exception.Message, false),
                ConflictException => (HttpStatusCode.Conflict, exception.Message, true),

                // Built-in exceptions
                ArgumentNullException => (HttpStatusCode.BadRequest, "Required parameter is missing", true),
                ArgumentException => (HttpStatusCode.BadRequest, "Invalid argument provided", true),
                InvalidOperationException => (HttpStatusCode.BadRequest, "Invalid operation", true),
                UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Unauthorized access", false),

                // Default
                _ => (HttpStatusCode.InternalServerError, "An internal server error occurred", true)
            };
        }
    }

    #region Custom Exceptions

    /// <summary>
    /// Exception for validation errors
    /// </summary>
    public class ValidationException : Exception
    {
        public ValidationException(string message) : base(message) { }
    }

    /// <summary>
    /// Exception for resource not found
    /// </summary>
    public class NotFoundException : Exception
    {
        public NotFoundException(string message) : base(message) { }
        public NotFoundException(string resourceName, object key)
            : base($"{resourceName} with key '{key}' was not found") { }
    }

    /// <summary>
    /// Exception for unauthorized access
    /// </summary>
    public class UnauthorizedException : Exception
    {
        public UnauthorizedException(string message = "Unauthorized access") : base(message) { }
    }

    /// <summary>
    /// Exception for forbidden access
    /// </summary>
    public class ForbiddenException : Exception
    {
        public ForbiddenException(string message = "Access forbidden") : base(message) { }
    }

    /// <summary>
    /// Exception for resource conflicts
    /// </summary>
    public class ConflictException : Exception
    {
        public ConflictException(string message) : base(message) { }
    }

    #endregion

    #region Extension Methods

    /// <summary>
    /// Extension methods for exception middleware registration
    /// </summary>
    public static class ExceptionMiddlewareExtensions
    {
        /// <summary>
        /// Add global exception handling middleware
        /// </summary>
        public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
        {
            return app.UseMiddleware<ExceptionMiddleware>();
        }
    }

    #endregion
}
