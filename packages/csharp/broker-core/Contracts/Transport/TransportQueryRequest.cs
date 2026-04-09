namespace BrokerCore.Contracts.Transport;

public sealed class TransportQueryRequest
{
    public string Capability { get; set; } = "transport.query";
    public string TransportMode { get; set; } = "auto";
    public string UserQuery { get; set; } = string.Empty;
    public string Locale { get; set; } = "zh-TW";
    public string Channel { get; set; } = "line";
    public Dictionary<string, string?> Context { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public TransportInteraction? Interaction { get; set; }
}

public sealed class TransportInteraction
{
    public string? ConversationId { get; set; }
    public string? FollowUpToken { get; set; }
    public string? SelectedOptionId { get; set; }
}
