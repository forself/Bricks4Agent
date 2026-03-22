namespace Broker.Services;

public sealed class HighLevelInputTrustPolicy
{
    public HighLevelTrustedParseResult Apply(HighLevelInputEnvelope envelope, HighLevelParsedInput parsed)
    {
        var trust = Evaluate(envelope, parsed);
        if (trust.Allowed)
        {
            return new HighLevelTrustedParseResult
            {
                Parsed = parsed,
                Trust = trust
            };
        }

        return new HighLevelTrustedParseResult
        {
            Parsed = new HighLevelParsedInput
            {
                Kind = parsed.Kind == HighLevelInputKind.Empty ? HighLevelInputKind.Empty : HighLevelInputKind.Conversation,
                Raw = parsed.Raw,
                Trimmed = parsed.Trimmed,
                Prefix = string.Empty,
                Body = parsed.Trimmed,
                Normalized = parsed.Normalized
            },
            Trust = trust
        };
    }

    public HighLevelInputTrustDecision Evaluate(HighLevelInputEnvelope envelope, HighLevelParsedInput parsed)
    {
        if (parsed.Kind is HighLevelInputKind.Empty or HighLevelInputKind.Conversation)
        {
            return Allow("non-command content");
        }

        if (envelope.Source != HighLevelInputSource.UserMessage)
        {
            return Deny("only raw user messages may issue commands");
        }

        if (envelope.Taint != HighLevelInputTaint.UserText && envelope.Taint != HighLevelInputTaint.TrustedControl)
        {
            return Deny("taint level does not allow command extraction");
        }

        if (envelope.Transforms.Count > 0)
        {
            return Deny("transformed content may not be promoted into commands");
        }

        return Allow("raw user input may issue commands");
    }

    private static HighLevelInputTrustDecision Allow(string reason)
        => new() { Allowed = true, Reason = reason };

    private static HighLevelInputTrustDecision Deny(string reason)
        => new() { Allowed = false, Reason = reason };
}

public sealed class HighLevelInputEnvelope
{
    public string RawText { get; set; } = string.Empty;
    public HighLevelInputSource Source { get; set; } = HighLevelInputSource.UserMessage;
    public HighLevelInputTaint Taint { get; set; } = HighLevelInputTaint.UserText;
    public List<HighLevelTransformKind> Transforms { get; set; } = new();
}

public enum HighLevelInputSource
{
    UserMessage = 0,
    ToolOutput = 1,
    RetrievedDocument = 2,
    DecodedPayload = 3,
    SystemState = 4
}

public enum HighLevelInputTaint
{
    TrustedControl = 0,
    UserText = 1,
    ExternalText = 2,
    TransformedText = 3
}

public enum HighLevelTransformKind
{
    Base64Decode = 0,
    UrlDecode = 1,
    HtmlDecode = 2,
    UnicodeNormalize = 3,
    MarkdownStrip = 4
}

public sealed class HighLevelInputTrustDecision
{
    public bool Allowed { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class HighLevelTrustedParseResult
{
    public HighLevelParsedInput Parsed { get; set; } = new();
    public HighLevelInputTrustDecision Trust { get; set; } = new();
}
