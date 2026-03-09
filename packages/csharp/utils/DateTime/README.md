# DateTime

日期時間工具類別 — 提供 Unix Timestamp 轉換、ISO 8601 處理、相對時間、期間計算、工作日等擴充方法。

## 初始化方式

```csharp
using YourNamespace.Utils.DateTime;
// 靜態類別，直接以擴充方法呼叫
```

## API 列表

### Unix Timestamp

| 方法 | 參數 | 回傳 | 說明 |
|------|------|------|------|
| `.ToUnixTimestamp()` | - | `long` | 轉為 Unix 秒數 |
| `.ToUnixTimestampMs()` | - | `long` | 轉為 Unix 毫秒數 |
| `FromUnixTimestamp(timestamp)` | `long` | `DateTime` | Unix 秒數轉 DateTime |
| `FromUnixTimestampMs(timestamp)` | `long` | `DateTime` | Unix 毫秒數轉 DateTime |

### ISO 8601

| 方法 | 參數 | 回傳 | 說明 |
|------|------|------|------|
| `.ToIso8601()` | - | `string` | 轉為 ISO 8601 字串 |
| `FromIso8601(iso8601)` | `string` | `DateTime` | ISO 8601 字串轉 DateTime |
| `TryParseIso8601(iso8601, out result)` | `string` | `bool` | 嘗試解析 ISO 8601 |

### 格式化

| 方法 | 回傳 | 範例輸出 |
|------|------|----------|
| `.ToShortDate()` | `string` | `2026-01-23` |
| `.ToLongDate()` | `string` | `2026-01-23 15:30:45` |
| `.ToReadableDate()` | `string` | `January 23, 2026` |
| `.ToReadableDateTime()` | `string` | `Jan 23, 2026 3:30 PM` |

### 相對時間

| 方法 | 參數 | 回傳 | 說明 |
|------|------|------|------|
| `.ToRelativeTime()` | - | `string` | 相對於現在（如 `2 hours ago`） |
| `.ToRelativeTime(referenceTime)` | `DateTime` | `string` | 相對於指定時間 |

### 期間起迄

| 方法 | 說明 |
|------|------|
| `.StartOfDay()` | 當天 00:00:00 |
| `.EndOfDay()` | 當天 23:59:59.999 |
| `.StartOfWeek(firstDay)` | 本週起始（預設週一） |
| `.EndOfWeek(firstDay)` | 本週結束 |
| `.StartOfMonth()` | 本月第一天 |
| `.EndOfMonth()` | 本月最後一天 |
| `.StartOfYear()` | 本年 1/1 |
| `.EndOfYear()` | 本年 12/31 |
| `.StartOfQuarter()` | 本季第一天 |
| `.EndOfQuarter()` | 本季最後一天 |
| `.GetQuarter()` | 取得季度（1-4） |

### 年齡計算

| 方法 | 參數 | 回傳 | 說明 |
|------|------|------|------|
| `.GetAge()` | - | `int` | 計算至今日的年齡 |
| `.GetAge(atDate)` | `DateTime` | `int` | 計算至指定日期的年齡 |

### 工作日

| 方法 | 參數 | 回傳 | 說明 |
|------|------|------|------|
| `.IsWeekend()` | - | `bool` | 是否為週末 |
| `.IsWeekday()` | - | `bool` | 是否為工作日 |
| `.AddBusinessDays(days)` | `int` | `DateTime` | 加減工作日（跳過週末） |
| `GetBusinessDays(start, end)` | `DateTime, DateTime` | `int` | 計算兩日期間的工作日數 |

### 比較

| 方法 | 回傳 | 說明 |
|------|------|------|
| `.IsToday()` | `bool` | 是否為今天 |
| `.IsYesterday()` | `bool` | 是否為昨天 |
| `.IsTomorrow()` | `bool` | 是否為明天 |
| `.IsPast()` | `bool` | 是否已過去 |
| `.IsFuture()` | `bool` | 是否在未來 |
| `.IsSameDay(other)` | `bool` | 是否為同一天 |
| `.IsBetween(start, end)` | `bool` | 是否在範圍內（含頭尾） |
| `.Clamp(min, max)` | `DateTime` | 限制在範圍內 |

### 截斷

| 方法 | 說明 |
|------|------|
| `.TruncateToSeconds()` | 移除毫秒 |
| `.TruncateToMinutes()` | 移除秒與毫秒 |
| `.TruncateToHours()` | 移除分、秒與毫秒 |

### 時區

| 方法 | 參數 | 說明 |
|------|------|------|
| `.ToTimeZone(timeZoneId)` | `string` | 轉換至指定時區 |
| `.ToUtc()` | - | 轉換至 UTC |

## 使用範例

```csharp
using YourNamespace.Utils.DateTime;

// Unix Timestamp
var now = System.DateTime.UtcNow;
long ts = now.ToUnixTimestamp();       // 1709712345
var dt = DateTimeHelper.FromUnixTimestamp(ts);

// 相對時間
var created = System.DateTime.UtcNow.AddHours(-3);
string relative = created.ToRelativeTime();  // "3 hours ago"

// 工作日計算
var start = new System.DateTime(2026, 3, 1);
var end = new System.DateTime(2026, 3, 31);
int bizDays = DateTimeHelper.GetBusinessDays(start, end);

// 期間
var monthStart = now.StartOfMonth();
var quarterEnd = now.EndOfQuarter();

// 年齡
var birthday = new System.DateTime(1990, 5, 15);
int age = birthday.GetAge();  // 35
```

## 依賴清單

| 依賴 | 說明 |
|------|------|
| `System` | .NET 基礎命名空間 |
| `System.Globalization` | ISO 8601 解析支援 |

無第三方依賴。
