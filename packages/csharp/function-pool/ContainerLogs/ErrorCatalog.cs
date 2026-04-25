using System.Text.RegularExpressions;

namespace FunctionPool.ContainerLogs;

/// <summary>
/// 錯誤分類目錄 — 把 raw log 訊息對應到有編號的類別，讓儀表板能：
///   1. 以「分類 + 短描述」代替冗長原文
///   2. 統計「哪類錯誤出現最多」
///   3. 讓使用者點展開看具體 raw 訊息（details）
///
/// 設計哲學：像 HTTP 狀態碼 — 使用者看到 `404 Not Found`、開發者展開看 stack trace。
///
/// 分類順序很重要：先列具體的（例如 LLM / Database），最後才放通用的
/// （Fail / Uncategorized）。Classify 會回傳第一個命中的 pattern。
/// </summary>
public static class ErrorCatalog
{
    public class Entry
    {
        public string Code        { get; init; } = "";   // ERR-001
        public string Category    { get; init; } = "";   // 人類可讀分類名（中文）
        public string Description { get; init; } = "";   // 短描述（UI 上一眼看到的）
        public string Severity    { get; init; } = "ERROR"; // ERROR / WARN
        public Regex  Pattern     { get; init; } = new(".^");  // 永不 match 的佔位
    }

    /// <summary>
    /// 目錄。新增分類就 push 到這個 list 前面（先命中原則）。
    /// </summary>
    public static readonly List<Entry> Entries = new()
    {
        // ── ERROR 類 ─────────────────────────────────────────────
        new()
        {
            Code = "ERR-001", Category = "Worker 連線中斷",
            Description = "Worker 與 broker TCP 連線斷開、正在重連",
            Pattern = new Regex(
                @"Worker connection (error|lost|closed)|Reconnecting|connection (closed|refused|reset)|Broken pipe|Transport (endpoint|error)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
        },
        new()
        {
            Code = "ERR-002", Category = "資料庫異常",
            Description = "SQLite / SQL 相關錯誤（lock、unable to open、corrupt）",
            Pattern = new Regex(
                @"SqliteException|database (is locked|disk image|is malformed)|unable to open database|SQL logic error|no such table",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
        },
        new()
        {
            Code = "ERR-003", Category = "LLM / 外部 API 限流",
            Description = "OpenAI / Gemini / 外部服務 429 / rate limit / quota",
            Pattern = new Regex(
                @"rate[ _-]?limit|quota exceeded|429|too many requests|overloaded_error|insufficient_quota",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
        },
        new()
        {
            Code = "ERR-004", Category = "認證 / 授權失敗",
            Description = "401 / 403 / invalid token / forbidden / unauthorized",
            Pattern = new Regex(
                @"\bunauthorized\b|\bforbidden\b|401|403|invalid (token|credential|signature)|authentication (failed|error)|access denied",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
        },
        new()
        {
            Code = "ERR-005", Category = "Timeout / 逾時",
            Description = "HTTP、TCP、DB 等操作超過限時",
            Pattern = new Regex(
                @"\btimed\s*out\b|\btimeout\b|deadline exceeded|operation cancelled|TaskCanceledException",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
        },
        new()
        {
            Code = "ERR-006", Category = "資源找不到",
            Description = "404 / Not Found / 檔案或路徑不存在",
            Pattern = new Regex(
                @"\b404\b|not found|FileNotFoundException|DirectoryNotFoundException|No such file or directory",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
        },
        new()
        {
            Code = "ERR-007", Category = "資料解析錯誤",
            Description = "JSON / XML / YAML 格式錯誤或欄位缺失",
            Pattern = new Regex(
                @"JsonException|JsonReaderException|deserializ\w*|parse error|invalid (json|format)|unexpected character|expected.*but got",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
        },
        new()
        {
            Code = "ERR-008", Category = "設定 / 環境變數缺失",
            Description = "必要 config / env / secret 沒提供或格式錯",
            Pattern = new Regex(
                @"missing (env|config|secret|key)|required.*not (provided|set|found)|configuration (error|missing|invalid)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
        },
        new()
        {
            Code = "ERR-009", Category = "Null / 參照錯誤",
            Description = "NullReferenceException、undefined is not a function 之類的程式 bug",
            Pattern = new Regex(
                @"NullReferenceException|object reference not set|cannot read propert(y|ies) of (null|undefined)|TypeError",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
        },
        new()
        {
            Code = "ERR-010", Category = "例外 / 未處理",
            Description = "unhandled exception、panic、traceback",
            Pattern = new Regex(
                @"unhandled (exception|error)|panic:|Traceback|System\.\w+Exception|at\s+\w+\.\w+\s*\(",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
        },

        new()
        {
            Code = "ERR-011", Category = "套件 / 更新失敗",
            Description = "npm / pip / docker pull / auto-update 等安裝或更新作業失敗",
            Pattern = new Regex(
                @"(auto[-\s]?update|npm install|pip install|docker pull|image pull|update)\s+(failed|error)|Try claude doctor",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),
        },

        // ── WARN 類 ─────────────────────────────────────────────
        new()
        {
            Code = "WRN-001", Category = "已棄用 API",
            Description = "使用中的 API 或函式被標記為 deprecated",
            Severity = "WARN",
            Pattern = new Regex(@"\bdeprecated\b|\bobsolete\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        },
        new()
        {
            Code = "WRN-002", Category = "重試 / Backoff",
            Description = "短暫失敗但會自動重試（非致命）",
            Severity = "WARN",
            Pattern = new Regex(@"retry(ing)?|backoff|transient|will retry", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        },
    };

    /// <summary>最後 fallback 類別（任何未命中的 ERROR / WARN 都會落到這裡）。</summary>
    public static readonly Entry DefaultError = new()
    {
        Code = "ERR-999", Category = "其他 / 未分類",
        Description = "符合錯誤關鍵字但未對應到已知類別",
        Severity = "ERROR",
    };

    public static readonly Entry DefaultWarn = new()
    {
        Code = "WRN-999", Category = "其他警告",
        Description = "一般警告，未對應到已知類別",
        Severity = "WARN",
    };

    /// <summary>
    /// 對一行 log 訊息進行分類。levelHint 是上游 regex 判定的等級（ERROR / WARN），
    /// 會優先找相符等級的 catalog entry。
    /// </summary>
    public static Entry Classify(string message, string levelHint)
    {
        if (string.IsNullOrWhiteSpace(message))
            return levelHint == "WARN" ? DefaultWarn : DefaultError;

        // 先找等級相符的
        foreach (var e in Entries)
        {
            if (e.Severity != levelHint) continue;
            if (e.Pattern.IsMatch(message)) return e;
        }
        // 等級不符但 pattern 命中時也回傳（讓 level 以 catalog 為準）
        foreach (var e in Entries)
        {
            if (e.Pattern.IsMatch(message)) return e;
        }
        return levelHint == "WARN" ? DefaultWarn : DefaultError;
    }

    /// <summary>給儀表板頂部顯示的目錄清單（供 UI 渲染 chip / legend）。</summary>
    public static IEnumerable<Entry> AllKnown()
    {
        foreach (var e in Entries) yield return e;
        yield return DefaultError;
        yield return DefaultWarn;
    }

    // ── 等級偵測（給即時日誌即時掃描用，不用插 DB）──────────────────────
    private static readonly Regex ErrorTokens = new(
        @"\b(ERROR|ERRORS|ERR|FATAL|CRITICAL|Exception|unhandled|panic|traceback|stack\s*trace|fail(ed|ure)?|denied|refused|timed\s*out|timeout|crashed|aborted)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WarnTokens = new(
        @"\b(WARN|WARNING|deprecated)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FalsePositive = new(
        @"\b(0\s+errors?|no\s+errors?|without\s+errors?|0\s+failures?|no\s+failures?)\b|\bsuccessfully\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 判斷一行 log 屬於 ERROR / WARN / INFO。INFO 表示沒有任何錯誤關鍵字（不是錯誤）。
    /// </summary>
    public static string DetectSeverity(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return "INFO";
        if (FalsePositive.IsMatch(line)) return "INFO";   // "0 errors" 等成功訊息排除
        if (ErrorTokens.IsMatch(line)) return "ERROR";
        if (WarnTokens.IsMatch(line)) return "WARN";
        return "INFO";
    }

    // ── ANSI escape / 控制碼清理（給 docker logs TUI 輸出整理用）──────
    private static readonly Regex AnsiCsiSequence = new(
        @"\x1b\[[\?0-9;]*[A-Za-z]",
        RegexOptions.Compiled);

    private static readonly Regex AnsiOtherEscape = new(
        @"\x1b[^\[]|\x1b$",
        RegexOptions.Compiled);

    /// <summary>
    /// 清掉 ANSI 色碼、游標控制碼、Bracketed Paste Mode 等 TUI 雜訊，
    /// 讓 docker logs 變成可讀的純文字。
    /// </summary>
    public static string StripAnsi(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var s = AnsiCsiSequence.Replace(text, "");
        s = AnsiOtherEscape.Replace(s, "");
        return s;
    }
}
