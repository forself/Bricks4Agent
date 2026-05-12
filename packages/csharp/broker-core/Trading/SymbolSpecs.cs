namespace BrokerCore.Trading;

/// <summary>
/// 交易所合約規格表 — 在 approval / dispatch 前做 pre-flight 檢查。
///
/// 設計動機（user request）：之前的流程是「下單 → approval → 派發 → 交易所拒（min order
/// 不足 / 槓桿超範圍等）→ 報錯 → 推通知」。每筆失敗都產生一筆 approval、admin 還得手動
/// 拒掉、整個 chain 很吵。改成預先在 broker 端攔截：條件不過直接 400 給 caller、不創建
/// approval、不打交易所、不產 error log。
///
/// Phase 1（本檔）：硬編 BingX USDT-M perp 已知主流 symbol min order。
/// Phase 2（之後）：由 trading-worker 新增 capability `trading.perpetual/get_contract_info`、
///                   broker 端 cache 並 fallback 到此表。
///
/// 資料來源：BingX `/openApi/swap/v2/quote/contracts` 公開 endpoint（手動 sample）。
/// 這些值不常變、年級數據維護成本低。
/// </summary>
public static class SymbolSpecs
{
    public class Spec
    {
        public decimal MinQty       { get; init; }   // 最小單量
        public decimal QtyStep      { get; init; }   // 數量精度（≥ MinQty 且每次增加 ≥ QtyStep）
        public decimal MinNotional  { get; init; }   // 最小名目（USDT）
        public int     MaxLeverage  { get; init; }   // 該 symbol 容許最高槓桿
    }

    // BingX USDT-M Perpetual 已知合約規格、依交易量排前 ~20。
    // 之後動態 fetch 後可整批替換。
    private static readonly Dictionary<string, Spec> BingxSpecs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTC-USDT"]    = new() { MinQty = 0.0001m, QtyStep = 0.0001m, MinNotional = 2m, MaxLeverage = 125 },
        ["ETH-USDT"]    = new() { MinQty = 0.01m,   QtyStep = 0.01m,   MinNotional = 5m, MaxLeverage = 125 },
        ["SOL-USDT"]    = new() { MinQty = 0.1m,    QtyStep = 0.1m,    MinNotional = 5m, MaxLeverage = 75  },
        ["XRP-USDT"]    = new() { MinQty = 1m,      QtyStep = 1m,      MinNotional = 5m, MaxLeverage = 75  },
        ["BNB-USDT"]    = new() { MinQty = 0.01m,   QtyStep = 0.01m,   MinNotional = 5m, MaxLeverage = 75  },
        ["DOGE-USDT"]   = new() { MinQty = 1m,      QtyStep = 1m,      MinNotional = 5m, MaxLeverage = 75  },
        ["ADA-USDT"]    = new() { MinQty = 1m,      QtyStep = 1m,      MinNotional = 5m, MaxLeverage = 75  },
        ["LINK-USDT"]   = new() { MinQty = 0.1m,    QtyStep = 0.1m,    MinNotional = 5m, MaxLeverage = 75  },
        ["AVAX-USDT"]   = new() { MinQty = 0.1m,    QtyStep = 0.1m,    MinNotional = 5m, MaxLeverage = 75  },
        ["MATIC-USDT"]  = new() { MinQty = 1m,      QtyStep = 1m,      MinNotional = 5m, MaxLeverage = 75  },
        ["DOT-USDT"]    = new() { MinQty = 0.1m,    QtyStep = 0.1m,    MinNotional = 5m, MaxLeverage = 75  },
        ["LTC-USDT"]    = new() { MinQty = 0.01m,   QtyStep = 0.01m,   MinNotional = 5m, MaxLeverage = 75  },
        ["TRX-USDT"]    = new() { MinQty = 1m,      QtyStep = 1m,      MinNotional = 5m, MaxLeverage = 75  },
        ["ATOM-USDT"]   = new() { MinQty = 0.1m,    QtyStep = 0.1m,    MinNotional = 5m, MaxLeverage = 75  },
        ["UNI-USDT"]    = new() { MinQty = 0.1m,    QtyStep = 0.1m,    MinNotional = 5m, MaxLeverage = 75  },
    };

    // Phase 2：dynamic cache（key = "{exchange}:{symbol}"、case-insensitive）。
    // 由 SymbolSpecsService 啟動 + 每 12h 從 trading-worker 拉、用 ReplaceCache 整批替換。
    // 沒命中就 fallback 到 hardcoded BingxSpecs，所以新上架的小幣不會被誤擋。
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Spec> _dynamicCache
        = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _cacheUpdatedAt = DateTime.MinValue;

    public static DateTime CacheUpdatedAt => _cacheUpdatedAt;
    public static int CacheCount => _dynamicCache.Count;

    /// <summary>整批替換某交易所的 spec 快取。傳空 list 等於清空該交易所。</summary>
    public static void ReplaceCache(string exchange, IEnumerable<(string Symbol, Spec Spec)> entries)
    {
        if (string.IsNullOrEmpty(exchange)) return;
        var prefix = $"{exchange.ToLowerInvariant()}:";

        // 先移除該交易所的舊條目
        foreach (var key in _dynamicCache.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            _dynamicCache.TryRemove(key, out _);

        foreach (var (sym, spec) in entries)
        {
            if (string.IsNullOrEmpty(sym) || spec == null) continue;
            _dynamicCache[$"{prefix}{sym}"] = spec;
        }
        _cacheUpdatedAt = DateTime.UtcNow;
    }

    public static Spec? GetSpec(string exchange, string symbol)
    {
        if (string.IsNullOrEmpty(exchange) || string.IsNullOrEmpty(symbol)) return null;

        // Cache 優先（動態 fetch、跟著交易所上下架）
        if (_dynamicCache.TryGetValue($"{exchange.ToLowerInvariant()}:{symbol}", out var cached))
            return cached;

        // 沒命中、回 hardcoded fallback（保證 BTC/ETH 等主流仍能 pre-flight）
        if (exchange.Equals("bingx", StringComparison.OrdinalIgnoreCase))
            return BingxSpecs.TryGetValue(symbol, out var s) ? s : null;
        return null;
    }

    /// <summary>
    /// 開倉前 pre-flight 檢查（user request：先擋下、不要走進 approval / dispatch 才爆）。
    /// 回傳 (Ok, ErrorMessage)。未知 symbol：回 Ok=true（缺資料不阻擋、由交易所端最終判斷）、
    /// 但加 warning 字串給 caller 決定是否要照舊推。
    /// </summary>
    public static (bool Ok, string? Error, string? Warning) PreflightOrder(
        string exchange, string symbol, decimal qty, int leverage, decimal? markPrice = null)
    {
        if (qty <= 0m) return (false, $"qty must be > 0 (got {qty})", null);
        if (leverage < 1 || leverage > 125)
            return (false, $"leverage {leverage} out of range [1, 125]", null);

        var spec = GetSpec(exchange, symbol);
        if (spec == null)
        {
            return (true, null, $"no spec table for {exchange}:{symbol}; pre-flight only checked qty/leverage range");
        }

        if (qty < spec.MinQty)
            return (false, $"{symbol} qty {qty} below minimum {spec.MinQty}", null);
        if (leverage > spec.MaxLeverage)
            return (false, $"{symbol} leverage {leverage} exceeds max {spec.MaxLeverage}", null);
        if (markPrice.HasValue && markPrice.Value > 0m)
        {
            var notional = qty * markPrice.Value;
            if (notional < spec.MinNotional)
                return (false, $"{symbol} notional {notional:F2} USDT below minimum {spec.MinNotional} USDT", null);
        }
        return (true, null, null);
    }
}
