# IP Rate Limiting & Connection Info Module

IP 限流與連線資訊模組，提供滑動視窗演算法的 IP 限流、連線資訊擷取、用戶會話管理等功能。

## 功能特點

- **IP 限流**：滑動視窗演算法，精確控制請求頻率
- **連線資訊**：自動擷取客戶端 IP、User-Agent、裝置指紋
- **會話管理**：追蹤用戶登入會話與裝置
- **可疑 IP 偵測**：自動標記異常行為的 IP
- **登入歷史**：記錄登入嘗試供安全分析

## 安裝

```bash
dotnet add package IpRateLimiting
```

## 快速開始

### 1. 註冊服務

```csharp
// Program.cs 或 Startup.cs
builder.Services.AddSingleton<IIpRateLimiter, IpRateLimiter>();
builder.Services.AddSingleton<IConnectionInfoService, ConnectionInfoService>();
builder.Services.AddSingleton<IUserSessionService, UserSessionService>();
```

### 2. 使用 IP 限流

```csharp
public class MyController : ControllerBase
{
    private readonly IIpRateLimiter _rateLimiter;
    private readonly IConnectionInfoService _connectionInfo;

    public MyController(
        IIpRateLimiter rateLimiter,
        IConnectionInfoService connectionInfo)
    {
        _rateLimiter = rateLimiter;
        _connectionInfo = connectionInfo;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var clientIp = _connectionInfo.GetClientIp(HttpContext);

        // 檢查並增加計數
        var result = _rateLimiter.CheckAndIncrement(clientIp, "login");

        if (!result.IsAllowed)
        {
            Response.Headers["Retry-After"] = result.RetryAfterSeconds.ToString();
            return StatusCode(429, new {
                error = "Too many requests",
                retryAfter = result.RetryAfterSeconds
            });
        }

        // 執行登入邏輯...
        return Ok();
    }
}
```

### 3. 取得連線資訊

```csharp
var connectionInfo = _connectionInfo.GetConnectionInfo(HttpContext);

Console.WriteLine($"IP: {connectionInfo.IpAddress}");
Console.WriteLine($"Browser: {connectionInfo.UserAgentInfo?.Browser}");
Console.WriteLine($"OS: {connectionInfo.UserAgentInfo?.OperatingSystem}");
Console.WriteLine($"Device: {connectionInfo.UserAgentInfo?.DeviceType}");
Console.WriteLine($"Fingerprint: {connectionInfo.Fingerprint}");
```

### 4. 會話管理

```csharp
// 建立會話
var session = _sessionService.CreateSession(
    userId: 123,
    connectionInfo: connectionInfo,
    duration: TimeSpan.FromDays(7)
);

// 取得用戶所有會話
var sessions = _sessionService.GetUserSessions(userId);

// 登出其他裝置
_sessionService.InvalidateOtherSessions(userId, currentSessionId);
```

## 限流規則

### 預設規則

| 規則名稱 | 限制 | 視窗 | 鎖定時間 |
|---------|------|------|---------|
| `login` | 5 次 | 1 分鐘 | 15 分鐘 |
| `register` | 3 次 | 10 分鐘 | 1 小時 |
| `api` | 100 次 | 1 分鐘 | 5 分鐘 |
| `password_reset` | 3 次 | 15 分鐘 | 30 分鐘 |
| `mfa_verify` | 5 次 | 5 分鐘 | 15 分鐘 |

### 自訂規則

```csharp
var rateLimiter = new IpRateLimiter();

// 新增自訂規則
rateLimiter.AddRule("custom_api", new RateLimitRule
{
    MaxRequests = 50,
    WindowSeconds = 60,
    LockoutSeconds = 300
});

// 使用自訂規則
var result = rateLimiter.CheckAndIncrement(clientIp, "custom_api");
```

## 可疑 IP 管理

### 自動偵測

系統會在以下情況自動標記可疑 IP：

- 連續多次登入失敗（預設 15 次）
- 短時間內大量請求
- 嘗試攻擊行為

### 手動管理

```csharp
// 標記可疑 IP（降低限制為 50%）
_rateLimiter.MarkSuspicious(
    ip: "192.168.1.100",
    duration: TimeSpan.FromHours(24),
    reason: "Multiple failed login attempts"
);

// 檢查是否可疑
bool isSuspicious = _rateLimiter.IsSuspicious("192.168.1.100");

// 封鎖 IP
_rateLimiter.BlockIp("192.168.1.100", "Confirmed attack");

// 解除封鎖
_rateLimiter.UnblockIp("192.168.1.100");

// 取得 IP 統計
var stats = _rateLimiter.GetStatistics("192.168.1.100");
```

## 回應標頭

控制器會自動設定以下回應標頭：

| 標頭 | 說明 |
|------|------|
| `X-RateLimit-Limit` | 視窗內允許的最大請求數 |
| `X-RateLimit-Remaining` | 剩餘請求數 |
| `X-RateLimit-Reset` | 重設時間（Unix timestamp） |
| `Retry-After` | 被限流時，等待秒數 |

## API 端點

使用 `RateLimitedAuthController` 提供以下端點：

### 認證

| 方法 | 路徑 | 說明 |
|------|------|------|
| POST | `/api/auth/register` | 註冊（含限流） |
| POST | `/api/auth/login` | 登入（含限流） |
| POST | `/api/auth/login/mfa` | MFA 驗證 |
| POST | `/api/auth/logout` | 登出目前會話 |
| POST | `/api/auth/logout/all` | 登出所有會話 |

### 會話管理

| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/api/auth/sessions` | 取得所有登入裝置 |
| DELETE | `/api/auth/sessions/{id}` | 登出指定裝置 |

### 資訊查詢

| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/api/auth/connection-info` | 取得連線資訊 |
| GET | `/api/auth/login-history` | 登入歷史 |

## 連線資訊欄位

### ClientConnectionInfo

```csharp
public class ClientConnectionInfo
{
    public string IpAddress { get; set; }      // 客戶端 IP
    public string UserAgent { get; set; }       // User-Agent 原始值
    public UserAgentInfo UserAgentInfo { get; set; }  // 解析後的資訊
    public string Fingerprint { get; set; }     // 裝置指紋
    public string AcceptLanguage { get; set; }  // 偏好語言
    public string Referer { get; set; }         // 來源頁面
    public bool IsSecure { get; set; }          // 是否 HTTPS
}
```

### UserAgentInfo

```csharp
public class UserAgentInfo
{
    public string Browser { get; set; }         // 瀏覽器名稱
    public string BrowserVersion { get; set; }  // 瀏覽器版本
    public string OperatingSystem { get; set; } // 作業系統
    public string DeviceType { get; set; }      // 裝置類型
    public bool IsBot { get; set; }             // 是否為機器人
    public bool IsMobile { get; set; }          // 是否為行動裝置
}
```

## 進階設定

### IP 擷取優先順序

支援反向代理環境，IP 擷取優先順序：

1. `CF-Connecting-IP` (Cloudflare)
2. `X-Real-IP`
3. `X-Forwarded-For` (第一個 IP)
4. `RemoteIpAddress`

### 裝置指紋生成

指紋基於以下資訊生成 SHA256 雜湊：

- IP 地址
- User-Agent
- Accept-Language
- Accept-Encoding

## 安全建議

1. **生產環境**：使用 Redis 或分散式快取取代記憶體儲存
2. **代理設定**：確保正確設定 `ForwardedHeaders` 取得真實 IP
3. **日誌脫敏**：IP 地址在日誌中應部分遮蔽
4. **定期清理**：設定合理的過期時間避免記憶體洩漏

## 與 MFA 模組整合

此模組可與 `YourNamespace.Security.Mfa` 模組無縫整合：

```csharp
// 使用 RateLimitedAuthController 已整合 MFA + 限流
services.AddScoped<IMfaAuthService, MfaAuthService>();
services.AddSingleton<IIpRateLimiter, IpRateLimiter>();
services.AddSingleton<IConnectionInfoService, ConnectionInfoService>();
services.AddSingleton<IUserSessionService, UserSessionService>();
```

## 相依套件

- `Microsoft.AspNetCore.Http.Abstractions` >= 2.2.0

## 授權

MIT License
