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
            Answer = "目前資訊還不完整，我先用較寬的範圍整理可用結果；如果你補充日期或時段，我可以再縮小範圍。",
            MissingFields = verdict.MissingFields,
            NormalizedQuery = verdict.NormalizedQuery,
            RangeContext = new Dictionary<string, object?>
            {
                ["assumptions"] = new[] { "date=nearest_available" },
                ["scope_note"] = "這是依目前已知條件整理的範圍答案。"
            },
            Records = records.ToList()
        };
    }
}
