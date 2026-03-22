namespace Broker.Services;

public sealed class HighLevelCommandParser
{
    private static readonly string[] ProjectNamePrefixes = { "#", "\uFF03" };
    private static readonly HashSet<string> ConfirmTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "\u78ba\u8a8d",
        "confirm",
        "yes",
        "y",
        "ok",
        "okay"
    };

    private static readonly HashSet<string> CancelTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "\u53d6\u6d88",
        "cancel",
        "no",
        "n"
    };

    private readonly HighLevelCoordinatorOptions _options;

    public HighLevelCommandParser(HighLevelCoordinatorOptions options)
    {
        _options = options;
    }

    public HighLevelParsedInput Parse(string? rawMessage)
    {
        var trimmed = rawMessage?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new HighLevelParsedInput
            {
                Kind = HighLevelInputKind.Empty,
                Raw = rawMessage ?? string.Empty,
                Trimmed = string.Empty,
                Normalized = string.Empty
            };
        }

        if (IsHelpCommand(trimmed))
        {
            return Create(HighLevelInputKind.Help, rawMessage!, trimmed, string.Empty, string.Empty, string.Empty);
        }

        if (TryExtractPrefixedBody(trimmed, _options.QueryPrefixes, out var queryPrefix, out var queryBody))
        {
            return Create(
                HighLevelInputKind.Query,
                rawMessage!,
                trimmed,
                queryPrefix,
                queryBody,
                Normalize(queryBody));
        }

        if (TryExtractPrefixedBody(trimmed, _options.ProductionPrefixes, out var productionPrefix, out var productionBody))
        {
            return Create(
                HighLevelInputKind.Production,
                rawMessage!,
                trimmed,
                productionPrefix,
                productionBody,
                Normalize(productionBody));
        }

        if (TryExtractPrefixedBody(trimmed, ProjectNamePrefixes, out var projectPrefix, out var projectBody))
        {
            return Create(
                HighLevelInputKind.ProjectName,
                rawMessage!,
                trimmed,
                projectPrefix,
                projectBody,
                Normalize(projectBody));
        }

        var normalized = Normalize(trimmed);
        if (ConfirmTokens.Contains(normalized))
        {
            return Create(HighLevelInputKind.Confirm, rawMessage!, trimmed, string.Empty, string.Empty, normalized);
        }

        if (CancelTokens.Contains(normalized))
        {
            return Create(HighLevelInputKind.Cancel, rawMessage!, trimmed, string.Empty, string.Empty, normalized);
        }

        return Create(HighLevelInputKind.Conversation, rawMessage!, trimmed, string.Empty, trimmed, normalized);
    }

    public bool IsHelpCommand(string message)
        => message.Trim() is "?help" or "?Help" or "\uFF1Fhelp" or "\uFF1FHelp";

    private static bool TryExtractPrefixedBody(
        string message,
        IEnumerable<string> prefixes,
        out string matchedPrefix,
        out string body)
    {
        var trimmed = message.Trim();
        foreach (var prefix in prefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                matchedPrefix = prefix;
                body = trimmed[prefix.Length..].Trim();
                return true;
            }
        }

        matchedPrefix = string.Empty;
        body = string.Empty;
        return false;
    }

    private static HighLevelParsedInput Create(
        HighLevelInputKind kind,
        string raw,
        string trimmed,
        string prefix,
        string body,
        string normalized)
        => new()
        {
            Kind = kind,
            Raw = raw,
            Trimmed = trimmed,
            Prefix = prefix,
            Body = body,
            Normalized = normalized
        };

    private static string Normalize(string value)
        => value.Trim().ToLowerInvariant();
}

public enum HighLevelInputKind
{
    Empty = 0,
    Help = 1,
    Query = 2,
    Production = 3,
    ProjectName = 4,
    Confirm = 5,
    Cancel = 6,
    Conversation = 7
}

public sealed class HighLevelParsedInput
{
    public HighLevelInputKind Kind { get; set; }
    public string Raw { get; set; } = string.Empty;
    public string Trimmed { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Normalized { get; set; } = string.Empty;
}
