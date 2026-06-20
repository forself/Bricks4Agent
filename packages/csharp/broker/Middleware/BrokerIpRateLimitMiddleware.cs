using System.Collections.Concurrent;
using Broker.Helpers;

namespace Broker.Middleware;

public sealed class BrokerIpRateLimitOptions
{
    public bool Enabled { get; set; } = true;
    public int PermitLimit { get; set; } = 120;
    public int WindowSeconds { get; set; } = 60;
    public int MaxTrackedClients { get; set; } = 4096;
}

public sealed class BrokerIpRateLimitMiddleware
{
    private static readonly PathString[] ExcludedPrefixes =
    [
        "/api/v1/health",
        "/api/v1/local-admin",
        "/api/v1/artifacts/download",
        "/api/v1/google-drive/oauth/callback",
        "/dev",
    ];

    private readonly RequestDelegate _next;
    private readonly BrokerIpRateLimitOptions _options;
    private readonly ILogger<BrokerIpRateLimitMiddleware> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, ClientWindow> _windows = new(StringComparer.Ordinal);
    private readonly object _compactGate = new();

    public BrokerIpRateLimitMiddleware(
        RequestDelegate next,
        BrokerIpRateLimitOptions options,
        ILogger<BrokerIpRateLimitMiddleware> logger,
        TimeProvider? timeProvider = null)
    {
        _next = next;
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldLimit(context))
        {
            await _next(context);
            return;
        }

        var clientId = ResolveClientId(context);
        var now = _timeProvider.GetUtcNow();
        var window = TimeSpan.FromSeconds(Math.Max(1, _options.WindowSeconds));
        var limit = Math.Max(1, _options.PermitLimit);
        CompactExpired(now, window);

        if (!_windows.ContainsKey(clientId) && _windows.Count >= Math.Max(1, _options.MaxTrackedClients))
        {
            _logger.LogWarning(
                "Broker API rate limit tracker is full: client={ClientId} path={Path} trackedClients={TrackedClients} maxTrackedClients={MaxTrackedClients}",
                clientId,
                context.Request.Path,
                _windows.Count,
                _options.MaxTrackedClients);

            await WriteRateLimitResponseAsync(context, window, "Rate limit exceeded.");
            return;
        }

        var clientWindow = _windows.AddOrUpdate(
            clientId,
            _ => new ClientWindow(now, 1),
            (_, current) => current.WindowExpired(now, window)
                ? new ClientWindow(now, 1)
                : current.Increment());

        if (clientWindow.Count <= limit)
        {
            await _next(context);
            return;
        }

        var retryAfter = clientWindow.StartedAt + window - now;
        if (retryAfter < TimeSpan.Zero)
            retryAfter = TimeSpan.FromSeconds(1);

        _logger.LogWarning(
            "Broker API rate limit exceeded: client={ClientId} path={Path} count={Count} limit={Limit}",
            clientId,
            context.Request.Path,
            clientWindow.Count,
            limit);

        await WriteRateLimitResponseAsync(context, retryAfter, "Rate limit exceeded.");
    }

    private bool ShouldLimit(HttpContext context)
    {
        if (!_options.Enabled)
            return false;

        if (!HttpMethods.IsPost(context.Request.Method))
            return false;

        if (_options.MaxTrackedClients <= 0)
            return false;

        return !ExcludedPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix));
    }

    private string ResolveClientId(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(remote))
            return remote;

        return "unknown";
    }

    private void CompactExpired(DateTimeOffset now, TimeSpan window)
    {
        if (_windows.Count < Math.Max(1, _options.MaxTrackedClients))
            return;

        lock (_compactGate)
        {
            foreach (var pair in _windows)
            {
                if (pair.Value.WindowExpired(now, window))
                    _windows.TryRemove(pair.Key, out _);
            }
        }
    }

    private static async Task WriteRateLimitResponseAsync(
        HttpContext context,
        TimeSpan retryAfter,
        string message)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        await context.Response.WriteAsJsonAsync(
            ApiResponseHelper.Error(message, StatusCodes.Status429TooManyRequests));
    }

    private readonly record struct ClientWindow(DateTimeOffset StartedAt, int Count)
    {
        public bool WindowExpired(DateTimeOffset now, TimeSpan window)
            => now - StartedAt >= window;

        public ClientWindow Increment() => this with { Count = Count + 1 };
    }
}

public static class BrokerIpRateLimitMiddlewareExtensions
{
    public static IApplicationBuilder UseBrokerIpRateLimit(
        this IApplicationBuilder builder,
        BrokerIpRateLimitOptions options)
    {
        return builder.UseMiddleware<BrokerIpRateLimitMiddleware>(options);
    }
}
