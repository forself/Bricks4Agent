using System.Net;

namespace Broker.Middleware;

public sealed class DevEndpointGuardMiddleware
{
    private readonly RequestDelegate _next;

    public DevEndpointGuardMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsDevEndpoint(context.Request.Path) && !IsLocalRequest(context))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await _next(context);
    }

    private static bool IsDevEndpoint(PathString path)
        => path.StartsWithSegments("/dev", StringComparison.OrdinalIgnoreCase);

    private static bool IsLocalRequest(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp == null)
            return true;

        if (IPAddress.IsLoopback(remoteIp))
            return true;

        var localIp = context.Connection.LocalIpAddress;
        return localIp != null && remoteIp.Equals(localIp);
    }
}

public static class DevEndpointGuardMiddlewareExtensions
{
    public static IApplicationBuilder UseDevEndpointGuard(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<DevEndpointGuardMiddleware>();
    }
}
