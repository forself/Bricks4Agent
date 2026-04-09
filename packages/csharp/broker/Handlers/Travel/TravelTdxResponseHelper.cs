using System.Text.Json;
using BrokerCore.Contracts;

namespace Broker.Handlers.Travel;

internal static class TravelTdxResponseHelper
{
    public static ExecutionResult CreateSuccess(
        string requestId,
        string mode,
        string query,
        object payload,
        string sourceLabel)
        => ExecutionResult.Ok(
            requestId,
            JsonSerializer.Serialize(new
            {
                mode,
                query,
                retrieved_at = DateTimeOffset.UtcNow.ToString("O"),
                tdx = payload,
                sources_used = new[] { sourceLabel }
            }));

    public static ExecutionResult CreateEmpty(
        string requestId,
        string mode,
        string query,
        string sourceLabel)
        => ExecutionResult.Ok(
            requestId,
            JsonSerializer.Serialize(new
            {
                mode,
                query,
                retrieved_at = DateTimeOffset.UtcNow.ToString("O"),
                tdx = BuildEmptyPayload(mode, sourceLabel),
                sources_used = new[] { sourceLabel }
            }));

    private static object BuildEmptyPayload(string mode, string sourceLabel)
        => mode switch
        {
            "bus" => new
            {
                source = sourceLabel,
                origin = string.Empty,
                destination = string.Empty,
                date = DateTime.Today.ToString("yyyy-MM-dd"),
                bus_count = 0,
                buses = Array.Empty<object>()
            },
            "flight" => new
            {
                source = sourceLabel,
                origin = string.Empty,
                destination = string.Empty,
                date = DateTime.Today.ToString("yyyy-MM-dd"),
                flight_count = 0,
                flights = Array.Empty<object>()
            },
            _ => new
            {
                source = sourceLabel,
                origin = string.Empty,
                destination = string.Empty,
                date = DateTime.Today.ToString("yyyy-MM-dd"),
                train_count = 0,
                trains = Array.Empty<object>()
            }
        };
}
