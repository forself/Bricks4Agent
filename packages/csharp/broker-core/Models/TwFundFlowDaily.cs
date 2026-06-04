using BaseOrm;

namespace BrokerCore.Models;

/// <summary>
/// 台股每日資金流(2026-06-04)。每檔個股每交易日一列:三大法人買賣超(TWSE T86)+ 融資融券餘額(MI_MARGN)。
///
/// 資料源(TWSE 公開、免費、無 key):
///   - 三大法人:https://www.twse.com.tw/rwd/zh/fund/T86  (單位:股)
///   - 融資融券:https://www.twse.com.tw/rwd/zh/marginTrading/MI_MARGN  (單位:張)
///
/// 只存 4 位數字代號的「普通個股 + 主要 ETF」(濾掉 6 位權證 / 牛熊證雜訊)。
/// EntryKey = "{trade_date}:{stock_code}"、同日重抓覆寫(冪等;由 service 用 DELETE-by-date + Insert 達成)。
/// 「外資」採 TWSE 慣例 = 外陸資買賣超(不含外資自營商) + 外資自營商買賣超。
/// 驗證:2330 外資(-103,827) + 投信(647,975) + 自營(226,886) = 三大法人(771,034) 完全吻合。
/// </summary>
[Table("tw_fund_flow_daily")]
public class TwFundFlowDaily
{
    [Key(AutoIncrement = false)]
    [Column("entry_key")]
    [MaxLength(40)]
    public string EntryKey { get; set; } = string.Empty;

    [Column("trade_date")]
    [Required]
    [MaxLength(10)]
    public string TradeDate { get; set; } = string.Empty;   // yyyy-MM-dd(TST 交易日)

    [Column("stock_code")]
    [Required]
    [MaxLength(10)]
    public string StockCode { get; set; } = string.Empty;

    [Column("stock_name")]
    [MaxLength(40)]
    public string StockName { get; set; } = string.Empty;

    // ── 三大法人買賣超(單位:股、正=買超 / 負=賣超)──
    [Column("foreign_net")] public long ForeignNet { get; set; }   // 外資合計(外陸資 + 外資自營商)
    [Column("trust_net")]   public long TrustNet { get; set; }     // 投信
    [Column("dealer_net")]  public long DealerNet { get; set; }    // 自營商
    [Column("total_net")]   public long TotalNet { get; set; }     // 三大法人合計

    // ── 融資融券餘額(單位:張)。無融資融券資料的個股則為 0 ──
    [Column("margin_balance")] public long MarginBalance { get; set; }   // 融資今日餘額
    [Column("margin_prev")]    public long MarginPrev { get; set; }      // 融資前日餘額
    [Column("short_balance")]  public long ShortBalance { get; set; }    // 融券今日餘額
    [Column("short_prev")]     public long ShortPrev { get; set; }       // 融券前日餘額

    /// <summary>當日收盤價(STOCK_DAY_ALL field[7]);算「買賣超金額(億)」用。0 = 該日未取得收盤(如 backfill 歷史日)。</summary>
    [Column("close_price")]
    public decimal ClosePrice { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
