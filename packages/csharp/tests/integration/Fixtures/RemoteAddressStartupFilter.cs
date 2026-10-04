using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Integration.Tests.Fixtures;

/// <summary>
/// Test-host-only startup filter. TestServer leaves <c>Connection.RemoteIpAddress</c> null,
/// so a test that depends on the caller's address names it in <see cref="HeaderName"/>;
/// the filter applies it before any broker middleware runs and removes the header.
/// Requests without the header keep the TestServer default (null).
/// </summary>
public sealed class RemoteAddressStartupFilter : IStartupFilter
{
    public const string HeaderName = "X-Integration-Remote-Address";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(HeaderName, out var values)
                    && IPAddress.TryParse(values.ToString(), out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                    context.Request.Headers.Remove(HeaderName);
                }

                await nextMiddleware(context);
            });

            next(app);
        };
    }
}
