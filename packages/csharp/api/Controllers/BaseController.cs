using Microsoft.AspNetCore.Mvc;
using Bricks4Agent.Api.Responses;
using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace Bricks4Agent.Api.Controllers
{
    /// <summary>
    /// Base API controller with common functionality
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public abstract class BaseController : ControllerBase
    {
        /// <summary>
        /// Get current user ID from claims
        /// </summary>
        protected int? CurrentUserId
        {
            get
            {
                var userIdClaim = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(userIdClaim, out int userId))
                {
                    return userId;
                }
                return null;
            }
        }

        /// <summary>
        /// Get current username from claims
        /// </summary>
        protected string CurrentUsername => User?.FindFirst(ClaimTypes.Name)?.Value;

        /// <summary>
        /// Get current user email from claims
        /// </summary>
        protected string CurrentUserEmail => User?.FindFirst(ClaimTypes.Email)?.Value;

        /// <summary>
        /// Get current user roles
        /// </summary>
        protected IEnumerable<string> CurrentUserRoles
        {
            get
            {
                return User?.FindAll(ClaimTypes.Role)?.Select(c => c.Value) ?? new List<string>();
            }
        }

        /// <summary>
        /// Check if current user has specific role
        /// </summary>
        /// <param name="role">Role name</param>
        /// <returns>True if user has the role</returns>
        protected bool HasRole(string role)
        {
            return User?.IsInRole(role) ?? false;
        }

        /// <summary>
        /// Get trace ID for request tracking
        /// </summary>
        protected string TraceId => HttpContext?.TraceIdentifier;

        #region Success Responses

        /// <summary>
        /// Return 200 OK with data
        /// </summary>
        protected IActionResult Success<T>(T data, string message = "Success")
        {
            var response = ApiResponse<T>.SuccessResponse(data, message);
            response.TraceId = TraceId;
            return Ok(response);
        }

        /// <summary>
        /// Return 200 OK without data
        /// </summary>
        protected IActionResult Success(string message = "Success")
        {
            var response = ApiResponse.SuccessResponse(message);
            response.TraceId = TraceId;
            return Ok(response);
        }

        /// <summary>
        /// Return 201 Created with data
        /// </summary>
        protected IActionResult Created<T>(T data, string message = "Resource created successfully")
        {
            var response = ApiResponse<T>.SuccessResponse(data, message, 201);
            response.TraceId = TraceId;
            return StatusCode(201, response);
        }

        /// <summary>
        /// Return 204 No Content
        /// </summary>
        protected IActionResult NoContent()
        {
            return StatusCode(204);
        }

        #endregion

        #region Error Responses

        /// <summary>
        /// Return 400 Bad Request
        /// </summary>
        protected IActionResult BadRequest(string message)
        {
            var response = ApiResponse.ErrorResponse(message, 400);
            response.TraceId = TraceId;
            return StatusCode(400, response);
        }

        /// <summary>
        /// Return 400 Bad Request with validation errors
        /// </summary>
        protected IActionResult BadRequest(List<string> errors, string message = "Validation failed")
        {
            var response = ApiResponse.ErrorResponse(errors, message, 400);
            response.TraceId = TraceId;
            return StatusCode(400, response);
        }

        /// <summary>
        /// Return 401 Unauthorized
        /// </summary>
        protected IActionResult Unauthorized(string message = "Unauthorized access")
        {
            var response = ApiResponse.UnauthorizedResponse(message);
            response.TraceId = TraceId;
            return StatusCode(401, response);
        }

        /// <summary>
        /// Return 403 Forbidden
        /// </summary>
        protected IActionResult Forbidden(string message = "Access forbidden")
        {
            var response = ApiResponse.ForbiddenResponse(message);
            response.TraceId = TraceId;
            return StatusCode(403, response);
        }

        /// <summary>
        /// Return 404 Not Found
        /// </summary>
        protected IActionResult NotFound(string message = "Resource not found")
        {
            var response = ApiResponse.NotFoundResponse(message);
            response.TraceId = TraceId;
            return StatusCode(404, response);
        }

        /// <summary>
        /// Return 409 Conflict
        /// </summary>
        protected IActionResult Conflict(string message = "Resource conflict")
        {
            var response = ApiResponse.ErrorResponse(message, 409);
            response.TraceId = TraceId;
            return StatusCode(409, response);
        }

        /// <summary>
        /// Return 500 Internal Server Error
        /// </summary>
        protected IActionResult InternalServerError(string message = "Internal server error")
        {
            var response = ApiResponse.ErrorResponse(message, 500);
            response.TraceId = TraceId;
            return StatusCode(500, response);
        }

        /// <summary>
        /// Return 500 Internal Server Error from exception
        /// </summary>
        protected IActionResult InternalServerError(Exception ex)
        {
            var message = ex.Message;
#if DEBUG
            // Include stack trace in development
            message = $"{ex.Message}\n{ex.StackTrace}";
#endif
            var response = ApiResponse.ErrorResponse(message, 500);
            response.TraceId = TraceId;
            return StatusCode(500, response);
        }

        #endregion

        #region ModelState Helpers

        /// <summary>
        /// Get validation errors from ModelState
        /// </summary>
        protected List<string> GetModelStateErrors()
        {
            var errors = new List<string>();
            foreach (var state in ModelState.Values)
            {
                foreach (var error in state.Errors)
                {
                    errors.Add(error.ErrorMessage);
                }
            }
            return errors;
        }

        /// <summary>
        /// Return 400 Bad Request with ModelState errors
        /// </summary>
        protected IActionResult BadRequestWithModelState()
        {
            var errors = GetModelStateErrors();
            return BadRequest(errors, "Validation failed");
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Execute an action and return appropriate response
        /// </summary>
        protected IActionResult ExecuteAction(Action action, string successMessage = "Success")
        {
            try
            {
                action();
                return Success(successMessage);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        /// <summary>
        /// Execute an async action and return appropriate response
        /// </summary>
        protected async Task<IActionResult> ExecuteActionAsync(Func<Task> action, string successMessage = "Success")
        {
            try
            {
                await action();
                return Success(successMessage);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        /// <summary>
        /// Execute a function and return appropriate response with data
        /// </summary>
        protected IActionResult ExecuteFunction<T>(Func<T> func, string successMessage = "Success")
        {
            try
            {
                var result = func();
                if (result == null)
                {
                    return NotFound();
                }
                return Success(result, successMessage);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        /// <summary>
        /// Execute an async function and return appropriate response with data
        /// </summary>
        protected async Task<IActionResult> ExecuteFunctionAsync<T>(Func<Task<T>> func, string successMessage = "Success")
        {
            try
            {
                var result = await func();
                if (result == null)
                {
                    return NotFound();
                }
                return Success(result, successMessage);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        #endregion
    }
}
