using BrokerCore.Contracts.Transport;

namespace TransportTdxWorker.Services;

public sealed class TransportFollowUpBuilder
{
    public TransportFollowUp Build(IReadOnlyList<string> missingFields)
    {
        if (missingFields.Contains("date"))
        {
            return new TransportFollowUp
            {
                Question = "請問你要查哪一天？",
                FollowUpToken = Guid.NewGuid().ToString("N"),
                Options =
                [
                    new TransportFollowUpOption { Id = "today", Label = "今天" },
                    new TransportFollowUpOption { Id = "tomorrow", Label = "明天" },
                    new TransportFollowUpOption { Id = "custom_date", Label = "指定日期" },
                    new TransportFollowUpOption { Id = "nearest_available", Label = "先看最近可用班次" }
                ]
            };
        }

        return new TransportFollowUp
        {
            Question = "目前資訊不足，請補充查詢條件。",
            FollowUpToken = Guid.NewGuid().ToString("N"),
            Options =
            [
                new TransportFollowUpOption { Id = "restatement", Label = "我重新描述" }
            ]
        };
    }
}
