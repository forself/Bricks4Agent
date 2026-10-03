# AuditLog

> ⚠ 參考程式碼：此目錄沒有 .csproj，未被任何專案編譯或引用，CI 也不建置它；以下說明未經建置驗證。

安全稽核日誌 API 控制器 — 提供安全事件查詢、使用者活動分析、告警管理、儀表板與 CSV 匯出功能。

## 初始化方式

```csharp
// SecurityLogService 建構子必須取得這兩個 repository，否則 DI 解析失敗
builder.Services.AddSingleton<ISecurityLogRepository, InMemorySecurityLogRepository>();
builder.Services.AddSingleton<ILoginRecordRepository, InMemoryLoginRecordRepository>();
builder.Services.AddScoped<ISecurityLogService, SecurityLogService>();
// Controller 由 DI 自動注入
```

## API 列表

### 當前使用者端點

| 方法 | 路由 | 說明 | 權限 |
|---|---|---|---|
| `GET` | `/api/security-logs/my/login-history?page=1&pageSize=20` | 取得自己的登入歷史 | `[Authorize]` |
| `GET` | `/api/security-logs/my/activity?days=30` | 取得自己的活動摘要 | `[Authorize]` |
| `GET` | `/api/security-logs/my/failed-logins?count=10` | 取得自己最近的失敗登入 | `[Authorize]` |

### 管理員 — 日誌查詢

| 方法 | 路由 | 說明 | 權限 |
|---|---|---|---|
| `POST` | `/api/security-logs/query` | 依多重條件查詢安全日誌 | `Admin,SecurityAdmin` |
| `GET` | `/api/security-logs/{id}` | 依 ID 取得日誌詳情 | `Admin,SecurityAdmin` |
| `POST` | `/api/security-logs/export` | 匯出日誌為 CSV（上限 10,000 筆） | `Admin,SecurityAdmin` |

### 管理員 — 使用者活動

| 方法 | 路由 | 說明 | 權限 |
|---|---|---|---|
| `GET` | `/api/security-logs/users/{userId}/login-history` | 取得指定使用者的登入歷史 | `Admin,SecurityAdmin` |
| `GET` | `/api/security-logs/users/{userId}/activity?days=30` | 取得指定使用者的活動摘要 | `Admin,SecurityAdmin` |
| `DELETE` | `/api/security-logs/users/{userId}` | 刪除使用者日誌（GDPR） | `Admin` |

### 管理員 — IP / 可疑活動

| 方法 | 路由 | 說明 | 權限 |
|---|---|---|---|
| `GET` | `/api/security-logs/ips/{ipHash}/activity?days=30` | 取得 IP 活動摘要 | `Admin,SecurityAdmin` |
| `GET` | `/api/security-logs/suspicious-logins?hours=24&count=100` | 取得可疑登入記錄 | `Admin,SecurityAdmin` |

### 管理員 — 統計與告警

| 方法 | 路由 | 說明 | 權限 |
|---|---|---|---|
| `GET` | `/api/security-logs/statistics?days=7` | 取得安全統計（天數） | `Admin,SecurityAdmin` |
| `POST` | `/api/security-logs/statistics` | 取得安全統計（日期範圍，上限 90 天） | `Admin,SecurityAdmin` |
| `GET` | `/api/security-logs/alerts` | 取得未確認的安全告警 | `Admin,SecurityAdmin` |
| `POST` | `/api/security-logs/alerts/{alertId}/acknowledge` | 確認安全告警 | `Admin,SecurityAdmin` |

### 管理員 — 儀表板

| 方法 | 路由 | 說明 | 權限 |
|---|---|---|---|
| `GET` | `/api/security-logs/dashboard` | 取得儀表板摘要（24h + 7d 統計） | `Admin,SecurityAdmin` |

## 使用範例

### 查詢安全日誌

```http
POST /api/security-logs/query
Content-Type: application/json

{
    "startDate": "2026-03-01T00:00:00Z",
    "endDate": "2026-03-06T23:59:59Z",
    "eventTypes": ["LoginFailed"],
    "severities": ["High", "Critical"],
    "outcome": "Failure",
    "page": 1,
    "pageSize": 50,
    "sortBy": "Timestamp",
    "sortDescending": true
}
```

嚴重等級只有 `Info`、`Low`、`Medium`、`High`、`Critical`（沒有 `Warning`）。以字串傳送列舉（`eventTypes`、`severities`、`outcome`）需在主機加入 `JsonStringEnumConverter`，否則 System.Text.Json 只接受數值（如 `"severities": [3, 4]`）。

### 確認告警

```http
POST /api/security-logs/alerts/123/acknowledge
Content-Type: application/json

{
    "note": "已確認為誤判，IP 屬於內部 VPN"
}
```

### 匯出 CSV

```http
POST /api/security-logs/export
Content-Type: application/json

{
    "startDate": "2026-03-01T00:00:00Z",
    "endDate": "2026-03-06T23:59:59Z",
    "severities": ["Critical"]
}
```

回傳 `text/csv` 檔案，檔名格式：`security-logs-20260306-153045.csv`

## 依賴清單

| 依賴 | 說明 |
|---|---|
| `ISecurityLogService` | 安全日誌服務介面（實作 `SecurityLogService` 位於 `security/AuditLog`，另需註冊 `ISecurityLogRepository`、`ILoginRecordRepository`） |
| `ILogger<SecurityLogController>` | ASP.NET Core 日誌服務 |
| `Microsoft.AspNetCore.Authorization` | 授權屬性 |
| `Microsoft.AspNetCore.Mvc` | MVC 控制器基底 |

## 相關 DTO

- `SecurityLogQueryRequest` — 查詢請求（StartDate, EndDate, EventTypes, Severities, Outcome, UserId, SearchText, Tags 等）

- `DateRangeRequest` — 日期範圍請求

- `AcknowledgeAlertRequest` — 告警確認請求（Note）

- `DeleteLogsResponse` — 刪除日誌回應（DeletedCount）

- `DashboardSummary` — 儀表板摘要（24h/7d 統計、趨勢、告警數等）
