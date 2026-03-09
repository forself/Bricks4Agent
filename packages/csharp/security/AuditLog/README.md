# Security Audit Log Module

安全稽核日誌模組，提供完整的登入紀錄管理、安全事件追蹤、統計分析與警示功能。

## 功能特點

- **安全事件記錄**：完整的安全事件類型與嚴重等級分類
- **登入歷史追蹤**：詳細的登入紀錄，包含裝置、位置、MFA 狀態
- **新裝置偵測**：自動識別用戶首次使用的裝置
- **可疑活動標記**：自動偵測並標記異常登入行為
- **統計分析**：豐富的統計報表與趨勢分析
- **警示系統**：可配置的安全警示規則
- **GDPR 合規**：支援用戶資料刪除

## 安裝

```bash
dotnet add package SecurityAuditLog
```

## 快速開始

### 1. 註冊服務

```csharp
// Program.cs 或 Startup.cs
builder.Services.AddSingleton<ISecurityLogRepository, InMemorySecurityLogRepository>();
builder.Services.AddSingleton<ILoginRecordRepository, InMemoryLoginRecordRepository>();
builder.Services.AddSingleton<ISecurityAlertRepository, InMemorySecurityAlertRepository>();
builder.Services.AddSingleton<ISecurityLogService, SecurityLogService>();
```

### 2. 記錄登入事件

```csharp
public class AuthService
{
    private readonly ISecurityLogService _logService;

    public AuthService(ISecurityLogService logService)
    {
        _logService = logService;
    }

    public async Task<LoginResult> Login(string email, string password, HttpContext context)
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString();
        var userAgent = context.Request.Headers["User-Agent"].ToString();

        // 驗證密碼...
        var isValid = ValidatePassword(email, password);

        if (isValid)
        {
            _logService.LogLoginSuccess(
                userId: user.Id,
                username: email,
                ipAddress: ipAddress,
                userAgent: userAgent,
                sessionId: session.Id,
                mfaVerified: true,
                mfaMethod: "TOTP"
            );
        }
        else
        {
            _logService.LogLoginFailed(
                username: email,
                ipAddress: ipAddress,
                userAgent: userAgent,
                reason: "Invalid password"
            );
        }

        return result;
    }
}
```

### 3. 使用 Fluent Builder 記錄事件

```csharp
_logService.Log(builder => builder
    .WithEventType(SecurityEventType.PasswordChanged)
    .WithSeverity(SecuritySeverity.Medium)
    .WithOutcome(EventOutcome.Success)
    .WithUser(userId, username)
    .WithIp(ipAddress)
    .WithMessage("User changed their password")
    .WithTag("account")
    .WithTag("security"));
```

### 4. 查詢日誌

```csharp
// 查詢過去 7 天的失敗登入
var result = _logService.Query(new SecurityLogQuery
{
    StartDate = DateTime.UtcNow.AddDays(-7),
    EventTypes = new List<SecurityEventType> { SecurityEventType.LoginFailed },
    Page = 1,
    PageSize = 50,
    SortDescending = true
});

// 取得用戶登入歷史
var loginHistory = _logService.GetUserLoginHistory(userId, page: 1, pageSize: 20);

// 取得用戶活動摘要
var activity = _logService.GetUserActivity(userId, since: DateTime.UtcNow.AddDays(-30));
```

## 安全事件類型

### 認證事件 (1xx)

| 類型 | 代碼 | 說明 |
|------|------|------|
| `LoginSuccess` | 100 | 登入成功 |
| `LoginFailed` | 101 | 登入失敗 |
| `LogoutSuccess` | 102 | 登出成功 |
| `SessionExpired` | 103 | 會話過期 |
| `SessionInvalidated` | 104 | 會話被撤銷 |

### MFA 事件 (2xx)

| 類型 | 代碼 | 說明 |
|------|------|------|
| `MfaEnabled` | 200 | MFA 已啟用 |
| `MfaDisabled` | 201 | MFA 已停用 |
| `MfaVerifySuccess` | 202 | MFA 驗證成功 |
| `MfaVerifyFailed` | 203 | MFA 驗證失敗 |
| `MfaRecoveryCodeUsed` | 204 | 使用復原碼 |
| `MfaRecoveryCodesRegenerated` | 205 | 重新生成復原碼 |

### 帳戶事件 (3xx)

| 類型 | 代碼 | 說明 |
|------|------|------|
| `AccountCreated` | 300 | 帳戶建立 |
| `AccountUpdated` | 301 | 帳戶更新 |
| `AccountDeleted` | 302 | 帳戶刪除 |
| `AccountLocked` | 303 | 帳戶鎖定 |
| `AccountUnlocked` | 304 | 帳戶解鎖 |
| `PasswordChanged` | 305 | 密碼變更 |
| `PasswordResetRequested` | 306 | 請求密碼重設 |
| `PasswordResetCompleted` | 307 | 密碼重設完成 |
| `EmailChanged` | 308 | 電子郵件變更 |
| `EmailVerified` | 309 | 電子郵件已驗證 |

### 安全事件 (4xx)

| 類型 | 代碼 | 說明 |
|------|------|------|
| `RateLimitExceeded` | 400 | 超過頻率限制 |
| `SuspiciousActivity` | 401 | 可疑活動 |
| `IpBlocked` | 402 | IP 被封鎖 |
| `IpUnblocked` | 403 | IP 解除封鎖 |
| `BruteForceDetected` | 404 | 偵測到暴力破解 |
| `UnauthorizedAccess` | 405 | 未授權存取 |
| `TokenRevoked` | 406 | 權杖被撤銷 |
| `InvalidToken` | 407 | 無效權杖 |

### 管理事件 (5xx)

| 類型 | 代碼 | 說明 |
|------|------|------|
| `AdminLogin` | 500 | 管理員登入 |
| `AdminAction` | 501 | 管理員操作 |
| `PermissionChanged` | 502 | 權限變更 |
| `RoleAssigned` | 503 | 角色指派 |
| `RoleRevoked` | 504 | 角色撤銷 |
| `SystemConfigChanged` | 505 | 系統設定變更 |

## 嚴重等級

| 等級 | 說明 |
|------|------|
| `Info` | 一般資訊，正常操作 |
| `Low` | 低風險安全事件 |
| `Medium` | 中等風險，需要注意 |
| `High` | 高風險，需要關注 |
| `Critical` | 嚴重，需要立即處理 |

## API 端點

### 用戶端點（已登入用戶）

| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/api/security-logs/my/login-history` | 取得自己的登入歷史 |
| GET | `/api/security-logs/my/activity` | 取得自己的活動摘要 |
| GET | `/api/security-logs/my/failed-logins` | 取得自己的登入失敗記錄 |

### 管理端點（需要 Admin/SecurityAdmin 角色）

| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/api/security-logs/query` | 查詢安全日誌 |
| GET | `/api/security-logs/{id}` | 取得單筆日誌 |
| GET | `/api/security-logs/users/{userId}/login-history` | 取得指定用戶登入歷史 |
| GET | `/api/security-logs/users/{userId}/activity` | 取得指定用戶活動摘要 |
| GET | `/api/security-logs/ips/{ipHash}/activity` | 取得指定 IP 活動摘要 |
| GET | `/api/security-logs/suspicious-logins` | 取得可疑登入記錄 |
| GET | `/api/security-logs/statistics` | 取得安全統計 |
| GET | `/api/security-logs/dashboard` | 取得儀表板資料 |
| GET | `/api/security-logs/alerts` | 取得未確認警示 |
| POST | `/api/security-logs/alerts/{id}/acknowledge` | 確認警示 |
| POST | `/api/security-logs/export` | 匯出日誌為 CSV |
| DELETE | `/api/security-logs/users/{userId}` | 刪除用戶日誌 (GDPR) |

## 統計報表

```csharp
// 取得過去 7 天的統計
var stats = _logService.GetStatistics(days: 7);

Console.WriteLine($"登入嘗試: {stats.TotalLoginAttempts}");
Console.WriteLine($"登入成功: {stats.SuccessfulLogins}");
Console.WriteLine($"登入失敗: {stats.FailedLogins}");
Console.WriteLine($"成功率: {stats.LoginSuccessRate:F1}%");
Console.WriteLine($"MFA 驗證: {stats.MfaVerifications}");
Console.WriteLine($"可疑活動: {stats.SuspiciousActivities}");
Console.WriteLine($"限流觸發: {stats.RateLimitExceeded}");

// 依嚴重等級統計
foreach (var (severity, count) in stats.BySeverity)
{
    Console.WriteLine($"{severity}: {count}");
}

// 失敗登入最多的 IP
foreach (var ip in stats.TopFailedLoginIps)
{
    Console.WriteLine($"IP: {ip.IpAddressMasked}, 失敗: {ip.FailedAttempts}");
}
```

## 警示設定

```csharp
var alertConfig = new SecurityAlertConfig
{
    Name = "暴力破解警示",
    Description = "同一 IP 5 分鐘內超過 10 次登入失敗",
    EventType = SecurityEventType.LoginFailed,
    MinSeverity = SecuritySeverity.Low,
    ThresholdCount = 10,
    ThresholdMinutes = 5,
    IsEnabled = true,
    NotificationChannels = "[\"email\", \"slack\"]",
    Recipients = "[\"security@example.com\"]"
};

_alertRepository.AddAlertConfig(alertConfig);
```

## 隱私保護

### IP 遮蔽

```
原始: 192.168.1.100
遮蔽: 192.168.1.***
```

### 用戶名遮蔽

```
原始: john.doe@example.com
遮蔽: j*****e@example.com
```

### 設定選項

```csharp
var options = new SecurityLogOptions
{
    MaskIpAddress = true,      // 遮蔽 IP 地址
    MaskUsername = true,       // 遮蔽用戶名
    RetentionDays = 90,        // 保留天數
    MaxEntries = 100000        // 最大記錄數
};

services.AddSingleton(options);
```

## 與其他模組整合

### 與 MFA 模組整合

```csharp
// 在 MfaAuthService 中記錄 MFA 事件
_logService.LogMfaEvent(
    eventType: SecurityEventType.MfaVerifySuccess,
    userId: userId,
    username: email,
    ipAddress: ipAddress,
    success: true,
    method: "TOTP"
);
```

### 與 RateLimiting 模組整合

```csharp
// 在限流觸發時記錄
if (!rateLimitResult.IsAllowed)
{
    _logService.LogSecurityEvent(
        eventType: SecurityEventType.RateLimitExceeded,
        ipAddress: clientIp,
        userAgent: userAgent,
        message: $"Rate limit exceeded for rule: {ruleName}",
        severity: SecuritySeverity.Medium
    );
}
```

## 資料保留與清理

系統會自動清理過期的日誌記錄：

- 預設保留 90 天
- 每小時執行一次清理
- 可透過 `DeleteOlderThan()` 手動清理

```csharp
// 手動清理 30 天前的記錄
var cutoff = DateTime.UtcNow.AddDays(-30);
var deletedCount = _logRepository.DeleteOlderThan(cutoff);
```

## 生產環境建議

1. **使用資料庫儲存**：替換 InMemory 實作為 SQL Server/PostgreSQL 實作
2. **索引優化**：在 Timestamp、UserId、IpAddressHash 建立索引
3. **分區表**：考慮按月分區以提升查詢效能
4. **非同步寫入**：使用訊息佇列非同步寫入日誌
5. **備份策略**：定期備份安全日誌

## 相依套件

- `System.Text.Json` >= 8.0.0

## 授權

MIT License
