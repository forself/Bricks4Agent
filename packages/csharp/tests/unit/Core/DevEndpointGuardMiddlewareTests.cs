using System.Net;
using Broker.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Unit.Tests.Core;

public class DevEndpointGuardMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_RejectsDevEndpointFromNonLocalAddress()
    {
        var nextCalled = false;
        var context = new DefaultHttpContext();
        context.Request.Path = "/dev/system/status";
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        var sut = new DevEndpointGuardMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await sut.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_AllowsDevEndpointFromLoopbackAddress()
    {
        var nextCalled = false;
        var context = new DefaultHttpContext();
        context.Request.Path = "/dev/system/status";
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        var sut = new DevEndpointGuardMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await sut.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }
}
