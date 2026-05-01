using BrokerTdxApiService = Broker.Services.TdxApiService;
using BrokerTdxOptions = Broker.Services.TdxOptions;
using Microsoft.Extensions.Logging.Abstractions;
using TransportTdxApiService = TransportTdxWorker.Services.TdxApiService;
using TransportTdxOptions = TransportTdxWorker.Services.TdxOptions;
using Xunit;
using FluentAssertions;

namespace Unit.Tests.Core;

public class TdxApiServiceConfigurationTests
{
    [Fact]
    public void BrokerTdxApiService_IsConfigured_TreatsPlaceholderSecretAsUnset()
    {
        var service = new BrokerTdxApiService(
            new BrokerTdxOptions
            {
                ClientId = "client-id",
                ClientSecret = "REPLACE_WITH_TDX_CLIENT_SECRET"
            },
            new HttpClient(),
            NullLogger<BrokerTdxApiService>.Instance);

        service.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void BrokerTdxApiService_IsConfigured_TreatsPlaceholderClientIdAsUnset()
    {
        var service = new BrokerTdxApiService(
            new BrokerTdxOptions
            {
                ClientId = "REPLACE_WITH_TDX_CLIENT_ID",
                ClientSecret = "client-secret"
            },
            new HttpClient(),
            NullLogger<BrokerTdxApiService>.Instance);

        service.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void TransportTdxApiService_IsConfigured_TreatsPlaceholderSecretAsUnset()
    {
        var service = new TransportTdxApiService(
            new TransportTdxOptions
            {
                ClientId = "client-id",
                ClientSecret = "REPLACE_WITH_TDX_CLIENT_SECRET"
            },
            new HttpClient(),
            NullLogger<TransportTdxApiService>.Instance);

        service.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void TransportTdxApiService_IsConfigured_TreatsPlaceholderClientIdAsUnset()
    {
        var service = new TransportTdxApiService(
            new TransportTdxOptions
            {
                ClientId = "REPLACE_WITH_TDX_CLIENT_ID",
                ClientSecret = "client-secret"
            },
            new HttpClient(),
            NullLogger<TransportTdxApiService>.Instance);

        service.IsConfigured.Should().BeFalse();
    }
}
