using System.Text.Json.Serialization;

namespace BrokerCore.Contracts.Transport;

public enum TransportResultType
{
    FinalAnswer,
    NeedFollowUp,
    RangeAnswer
}

public sealed class TransportQueryResponse
{
    [JsonPropertyName("resultType")]
    public string ResultType => ResultTypeValue switch
    {
        TransportResultType.FinalAnswer => "final_answer",
        TransportResultType.NeedFollowUp => "need_follow_up",
        _ => "range_answer"
    };

    [JsonIgnore]
    public TransportResultType ResultTypeValue { get; set; }
    [JsonPropertyName("answer")]
    public string Answer { get; set; } = string.Empty;
    [JsonPropertyName("normalizedQuery")]
    public Dictionary<string, object?> NormalizedQuery { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonPropertyName("missingFields")]
    public List<string> MissingFields { get; set; } = [];
    [JsonPropertyName("followUp")]
    public TransportFollowUp? FollowUp { get; set; }
    [JsonPropertyName("rangeContext")]
    public Dictionary<string, object?>? RangeContext { get; set; }
    [JsonPropertyName("records")]
    public List<Dictionary<string, object?>> Records { get; set; } = [];
    [JsonPropertyName("evidence")]
    public List<Dictionary<string, string>> Evidence { get; set; } = [];
    [JsonPropertyName("providerMetadata")]
    public Dictionary<string, object?> ProviderMetadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TransportFollowUp
{
    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;
    [JsonPropertyName("followUpToken")]
    public string FollowUpToken { get; set; } = string.Empty;
    [JsonPropertyName("options")]
    public List<TransportFollowUpOption> Options { get; set; } = [];
}
