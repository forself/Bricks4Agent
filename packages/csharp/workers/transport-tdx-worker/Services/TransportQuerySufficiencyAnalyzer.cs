namespace TransportTdxWorker.Services;

public enum TransportQueryState
{
    Sufficient,
    PartiallySufficient,
    Insufficient
}

public sealed class TransportQueryVerdict
{
    public TransportQueryState State { get; init; }
    public List<string> MissingFields { get; init; } = [];
    public Dictionary<string, object?> NormalizedQuery { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TransportQuerySufficiencyAnalyzer
{
    public TransportQueryVerdict Analyze(string mode, string userQuery, IDictionary<string, string?> context)
    {
        var normalizedQuery = new Dictionary<string, object?>
        {
            ["transport_mode"] = mode,
            ["origin"] = Get(context, "origin"),
            ["destination"] = Get(context, "destination"),
            ["date"] = Get(context, "date"),
            ["time_range"] = Get(context, "time_range"),
            ["city"] = Get(context, "city"),
            ["route"] = Get(context, "route")
        };
        var missing = new List<string>();

        if (mode is "rail" or "hsr" or "flight" or "ship")
        {
            if (string.IsNullOrWhiteSpace(Get(context, "origin"))) missing.Add("origin");
            if (string.IsNullOrWhiteSpace(Get(context, "destination"))) missing.Add("destination");
        }
        else if (mode == "bus")
        {
            if (string.IsNullOrWhiteSpace(Get(context, "city"))) missing.Add("city");
            if (string.IsNullOrWhiteSpace(Get(context, "route"))) missing.Add("route");
        }
        else if (mode == "bike")
        {
            if (string.IsNullOrWhiteSpace(Get(context, "city")) &&
                string.IsNullOrWhiteSpace(Get(context, "geo_point")))
            {
                missing.Add("city");
            }
        }

        if (missing.Count > 0)
        {
            return new TransportQueryVerdict
            {
                State = TransportQueryState.Insufficient,
                MissingFields = missing,
                NormalizedQuery = normalizedQuery
            };
        }

        var partialMissing = new List<string>();
        if (string.IsNullOrWhiteSpace(Get(context, "date")))
        {
            partialMissing.Add("date");
        }

        return new TransportQueryVerdict
        {
            State = partialMissing.Count > 0 ? TransportQueryState.PartiallySufficient : TransportQueryState.Sufficient,
            MissingFields = partialMissing,
            NormalizedQuery = normalizedQuery
        };
    }

    private static string? Get(IDictionary<string, string?> context, string key)
        => context.TryGetValue(key, out var value) ? value : null;
}
