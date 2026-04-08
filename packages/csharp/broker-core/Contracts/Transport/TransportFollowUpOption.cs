using System.Text.Json.Serialization;

namespace BrokerCore.Contracts.Transport;

public sealed class TransportFollowUpOption
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
}
