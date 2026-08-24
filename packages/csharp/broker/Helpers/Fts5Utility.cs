using BrokerCore.Services;

namespace Broker.Helpers;

/// <summary>
/// FTS5 查詢語法工具
/// </summary>
public static class Fts5Utility
{
    /// <summary>
    /// 將中文文字轉為 FTS5 查詢語法
    /// unicode61 tokenizer 將 CJK 字元逐字切割，所以 "退貨" → "退 貨"
    /// 用空格隔開 → FTS5 預設 AND 語意
    /// </summary>
    public static string PrepareFts5Query(string query)
        => Fts5TextNormalizer.PrepareQuery(query);
}
