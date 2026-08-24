using System.Net;
using System.Text.Json;
using Broker.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Unit.Tests.Core;

public class BrokerHardeningMiddlewareTests
{
    [Fact]
    public async Task ExceptionHandlingMiddleware_ReturnsJson500WithoutLeakingException()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/runtime/spec";
        context.Response.Body = new MemoryStream();

        var sut = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("secret failure detail"),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await sut.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.ContentType.Should().StartWith("application/json");

        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("errorCode").GetInt32().Should().Be(500);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Internal server error.");
        doc.RootElement.ToString().Should().NotContain("secret failure detail");
    }

    [Fact]
    public async Task BrokerIpRateLimitMiddleware_Returns429AfterLimitIsExceeded()
    {
        var nextCalls = 0;
        var sut = new BrokerIpRateLimitMiddleware(
            _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            },
            new BrokerIpRateLimitOptions
            {
                Enabled = true,
                PermitLimit = 2,
                WindowSeconds = 60,
            },
            NullLogger<BrokerIpRateLimitMiddleware>.Instance);

        var first = CreatePostContext("198.51.100.20");
        var second = CreatePostContext("198.51.100.20");
        var third = CreatePostContext("198.51.100.20");

        await sut.InvokeAsync(first);
        await sut.InvokeAsync(second);
        await sut.InvokeAsync(third);

        first.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        second.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        third.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        nextCalls.Should().Be(2);
        third.Response.Headers.RetryAfter.Should().NotBeEmpty();
    }

    [Fact]
    public async Task BrokerIpRateLimitMiddleware_Returns429WhenTrackerIsFullForNewClient()
    {
        var nextCalls = 0;
        var sut = new BrokerIpRateLimitMiddleware(
            _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            },
            new BrokerIpRateLimitOptions
            {
                Enabled = true,
                PermitLimit = 10,
                WindowSeconds = 60,
                MaxTrackedClients = 1,
            },
            NullLogger<BrokerIpRateLimitMiddleware>.Instance);

        var firstClient = CreatePostContext("198.51.100.20");
        var secondClient = CreatePostContext("203.0.113.12");

        await sut.InvokeAsync(firstClient);
        await sut.InvokeAsync(secondClient);

        firstClient.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        secondClient.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        nextCalls.Should().Be(1);
        secondClient.Response.Headers.RetryAfter.Should().NotBeEmpty();
    }

    private static DefaultHttpContext CreatePostContext(string remoteIp)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/llm/chat";
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        context.Response.Body = new MemoryStream();
        return context;
    }
}
