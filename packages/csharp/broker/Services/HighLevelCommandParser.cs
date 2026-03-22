namespace Broker.Services;

public sealed class HighLevelCommandParser
{
    private static readonly string[] ProjectNamePrefixes = { "#", "\uFF03" };
    private static readonly HashSet<string> QuerySearchCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "search",
        "\u641c\u5c0b"
    };
    private static readonly HashSet<string> QueryRailCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "rail",
        "train",
        "\u706b\u8eca",
        "\u53f0\u9435",
        "\u9ad8\u9435"
    };
    private static readonly HashSet<string> QueryBusCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "bus",
        "\u516c\u8eca",
        "\u5ba2\u904b"
    };
    private static readonly HashSet<string> QueryFlightCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "flight",
        "flights",
        "\u822a\u73ed",
        "\u6a5f\u7968"
    };
    private static readonly HashSet<string> QueryProfileCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "profile",
        "me",
        "whoami"
    };
    private static readonly HashSet<string> ProductionNameCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "display-name",
        "displayname",
        "\u7a31\u547c"
    };
    private static readonly HashSet<string> ProductionIdCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "user-id",
        "userid",
        "code"
    };
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
            var (queryCommand, queryArgument) = ParseQueryBody(queryBody);
            return Create(
                HighLevelInputKind.Query,
                rawMessage!,
                trimmed,
                queryPrefix,
                queryBody,
                Normalize(queryBody),
                queryCommand,
                queryArgument);
        }

        if (TryExtractPrefixedBody(trimmed, _options.ProductionPrefixes, out var productionPrefix, out var productionBody))
        {
            var (productionCommand, productionArgument) = ParseProductionBody(productionBody);
            return Create(
                HighLevelInputKind.Production,
                rawMessage!,
                trimmed,
                productionPrefix,
                productionBody,
                Normalize(productionBody),
                productionCommand: productionCommand,
                productionArgument: productionArgument);
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
        string normalized,
        string queryCommand = "",
        string queryArgument = "",
        string productionCommand = "",
        string productionArgument = "")
        => new()
        {
            Kind = kind,
            Raw = raw,
            Trimmed = trimmed,
            Prefix = prefix,
            Body = body,
            Normalized = normalized,
            QueryCommand = queryCommand,
            QueryArgument = queryArgument,
            ProductionCommand = productionCommand,
            ProductionArgument = productionArgument
        };

    private static (string QueryCommand, string QueryArgument) ParseQueryBody(string body)
    {
        var trimmed = body.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return (string.Empty, string.Empty);

        var parts = trimmed.Split(new[] { ' ', '\t', '\r', '\n' }, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return (string.Empty, string.Empty);

        var candidate = Normalize(parts[0]);
        if (QuerySearchCommands.Contains(candidate))
        {
            var argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            return ("search", argument);
        }

        if (QueryRailCommands.Contains(candidate))
        {
            var argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            return ("rail", argument);
        }

        if (QueryBusCommands.Contains(candidate))
        {
            var argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            return ("bus", argument);
        }

        if (QueryFlightCommands.Contains(candidate))
        {
            var argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            return ("flight", argument);
        }

        if (QueryProfileCommands.Contains(candidate))
        {
            var argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            return ("profile", argument);
        }

        return (string.Empty, trimmed);
    }

    private static (string ProductionCommand, string ProductionArgument) ParseProductionBody(string body)
    {
        var trimmed = body.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return (string.Empty, string.Empty);

        var parts = trimmed.Split(new[] { ' ', '\t', '\r', '\n' }, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return (string.Empty, string.Empty);

        var candidate = Normalize(parts[0]);
        var argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        if (ProductionNameCommands.Contains(candidate))
            return ("name", argument);

        if (ProductionIdCommands.Contains(candidate))
            return ("id", argument);

        return (string.Empty, trimmed);
    }

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
    public string QueryCommand { get; set; } = string.Empty;
    public string QueryArgument { get; set; } = string.Empty;
    public string ProductionCommand { get; set; } = string.Empty;
    public string ProductionArgument { get; set; } = string.Empty;
}
