using BrokerCore.Contracts.Transport;

namespace TransportTdxWorker.Services;

public sealed class TransportRangeAnswerBuilder
{
    public TransportQueryResponse Build(
        TransportQueryVerdict verdict,
        IReadOnlyList<Dictionary<string, object?>> records)
    {
        return new TransportQueryResponse
        {
            ResultTypeValue = TransportResultType.RangeAnswer,
            Answer = "我先依目前資訊提供較寬範圍的結果；如果你指定日期或時段，我可以再縮小。",
            MissingFields = verdict.MissingFields,
            NormalizedQuery = verdict.NormalizedQuery,
            RangeContext = new Dictionary<string, object?>
            {
                ["assumptions"] = new[] { "date=nearest_available" },
                ["scope_note"] = "以目前可查到的近期結果先回答"
            },
            Records = records.ToList()
        };
    }
}
