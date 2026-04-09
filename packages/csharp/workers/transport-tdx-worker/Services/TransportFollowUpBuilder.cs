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
                Question = "請問你要查哪一天的交通資訊？",
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

        if (missingFields.Contains("city"))
        {
            return new TransportFollowUp
            {
                Question = "請問你要查哪個城市的公車？",
                FollowUpToken = Guid.NewGuid().ToString("N"),
                Options =
                [
                    new TransportFollowUpOption { Id = "taipei", Label = "臺北市" },
                    new TransportFollowUpOption { Id = "new_taipei", Label = "新北市" },
                    new TransportFollowUpOption { Id = "taoyuan", Label = "桃園市" },
                    new TransportFollowUpOption { Id = "other_city", Label = "其他，我重新描述" }
                ]
            };
        }

        return new TransportFollowUp
        {
            Question = "我還缺少必要資訊，請補充查詢條件。",
            FollowUpToken = Guid.NewGuid().ToString("N"),
            Options =
            [
                new TransportFollowUpOption { Id = "restatement", Label = "重新描述需求" }
            ]
        };
    }
}
