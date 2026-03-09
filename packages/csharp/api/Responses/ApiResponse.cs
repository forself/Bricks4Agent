using System;
using System.Collections.Generic;

namespace Bricks4Agent.Api.Responses
{
    /// <summary>
    /// Generic API response wrapper for consistent response format
    /// </summary>
    /// <typeparam name="T">Type of data payload</typeparam>
    public class ApiResponse<T>
    {
        /// <summary>
        /// Indicates whether the request was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// HTTP status code
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// Response message (success or error description)
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Response data payload
        /// </summary>
        public T Data { get; set; }

        /// <summary>
        /// List of error messages (for validation errors)
        /// </summary>
        public List<string> Errors { get; set; }

        /// <summary>
        /// Timestamp of the response
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Request trace ID for debugging
        /// </summary>
        public string TraceId { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public ApiResponse()
        {
            Timestamp = DateTime.UtcNow;
            Errors = new List<string>();
        }

        /// <summary>
        /// Create a success response
        /// </summary>
        /// <param name="data">Response data</param>
        /// <param name="message">Success message</param>
        /// <param name="statusCode">HTTP status code (default: 200)</param>
        /// <returns>ApiResponse instance</returns>
        public static ApiResponse<T> SuccessResponse(T data, string message = "Success", int statusCode = 200)
        {
            return new ApiResponse<T>
            {
                Success = true,
                StatusCode = statusCode,
                Message = message,
                Data = data
            };
        }

        /// <summary>
        /// Create an error response
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="statusCode">HTTP status code (default: 400)</param>
        /// <returns>ApiResponse instance</returns>
        public static ApiResponse<T> ErrorResponse(string message, int statusCode = 400)
        {
            return new ApiResponse<T>
            {
                Success = false,
                StatusCode = statusCode,
                Message = message,
                Errors = new List<string> { message }
            };
        }

        /// <summary>
        /// Create an error response with multiple error messages
        /// </summary>
        /// <param name="errors">List of error messages</param>
        /// <param name="message">General error message</param>
        /// <param name="statusCode">HTTP status code (default: 400)</param>
        /// <returns>ApiResponse instance</returns>
        public static ApiResponse<T> ErrorResponse(List<string> errors, string message = "Validation failed", int statusCode = 400)
        {
            return new ApiResponse<T>
            {
                Success = false,
                StatusCode = statusCode,
                Message = message,
                Errors = errors
            };
        }

        /// <summary>
        /// Create a not found response
        /// </summary>
        /// <param name="message">Not found message</param>
        /// <returns>ApiResponse instance</returns>
        public static ApiResponse<T> NotFoundResponse(string message = "Resource not found")
        {
            return new ApiResponse<T>
            {
                Success = false,
                StatusCode = 404,
                Message = message,
                Errors = new List<string> { message }
            };
        }

        /// <summary>
        /// Create an unauthorized response
        /// </summary>
        /// <param name="message">Unauthorized message</param>
        /// <returns>ApiResponse instance</returns>
        public static ApiResponse<T> UnauthorizedResponse(string message = "Unauthorized access")
        {
            return new ApiResponse<T>
            {
                Success = false,
                StatusCode = 401,
                Message = message,
                Errors = new List<string> { message }
            };
        }

        /// <summary>
        /// Create a forbidden response
        /// </summary>
        /// <param name="message">Forbidden message</param>
        /// <returns>ApiResponse instance</returns>
        public static ApiResponse<T> ForbiddenResponse(string message = "Access forbidden")
        {
            return new ApiResponse<T>
            {
                Success = false,
                StatusCode = 403,
                Message = message,
                Errors = new List<string> { message }
            };
        }
    }

    /// <summary>
    /// Non-generic API response wrapper (for responses without data payload)
    /// </summary>
    public class ApiResponse : ApiResponse<object>
    {
        /// <summary>
        /// Create a success response without data
        /// </summary>
        /// <param name="message">Success message</param>
        /// <param name="statusCode">HTTP status code (default: 200)</param>
        /// <returns>ApiResponse instance</returns>
        public static new ApiResponse SuccessResponse(string message = "Success", int statusCode = 200)
        {
            return new ApiResponse
            {
                Success = true,
                StatusCode = statusCode,
                Message = message
            };
        }

        /// <summary>
        /// Create an error response without data
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="statusCode">HTTP status code (default: 400)</param>
        /// <returns>ApiResponse instance</returns>
        public static new ApiResponse ErrorResponse(string message, int statusCode = 400)
        {
            return new ApiResponse
            {
                Success = false,
                StatusCode = statusCode,
                Message = message,
                Errors = new List<string> { message }
            };
        }
    }
}
