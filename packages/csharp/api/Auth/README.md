# Auth

> ⚠ 參考程式碼：此目錄沒有 .csproj，未被任何專案編譯或引用，CI 也不建置它；以下說明未經建置驗證。

認證 API 控制器模組 — 提供兩種認證控制器：MFA 認證（MfaAuthController）與 IP 速率限制認證（RateLimitedAuthController）。

## 初始化方式

```csharp
// 基本 MFA 認證
// （MfaAuthService 另需註冊 IUserRepository、IMfaRepository、IMfaService，見 security/Mfa/README.md）
builder.Services.AddScoped<IMfaAuthService, MfaAuthService>();

// 含速率限制的認證
builder.Services.AddScoped<IMfaAuthService, MfaAuthService>();
// IpRateLimiter 與 UserSessionService 把計數與工作階段存在記憶體中，必須是 Singleton；
// 用 AddScoped 會在每個請求重建，限流與工作階段都不會生效
builder.Services.AddSingleton<IIpRateLimiter, IpRateLimiter>();
builder.Services.AddSingleton<IConnectionInfoService, ConnectionInfoService>();
builder.Services.AddSingleton<IUserSessionService, UserSessionService>();
```

> 兩個控制器都宣告 `[Route("api/auth")]`，且 `register`、`login`、`login/mfa` 路由重複；同一個應用程式只能註冊其中一個，否則這些端點會發生路由衝突（AmbiguousMatchException）。

## 檔案說明

| 檔案 | 說明 |
|---|---|
| `MfaAuthController.cs` | 基本 MFA 認證控制器（註冊、登入、MFA 設定） |
| `RateLimitedAuthController.cs` | 獨立的進階認證控制器（直接繼承 `ControllerBase`，不繼承 MfaAuthController；提供含 IP 速率限制的註冊/登入與工作階段管理） |

## API 列表

### MfaAuthController — 基本認證

| 方法 | 路由 | 說明 | 權限 |
|---|---|---|---|
| `POST` | `/api/auth/register` | 註冊新使用者（可選啟用 MFA） | 匿名 |
| `POST` | `/api/auth/login` | 登入第一步 — 驗證帳密 | 匿名 |
| `POST` | `/api/auth/login/mfa` | 登入第二步 — 驗證 MFA 碼 | 匿名 |
| `POST` | `/api/auth/login/mfa/email` | 請求 Email OTP（尚未實作：stub，不驗證 token、不寄信，一律回傳成功訊息） | 匿名 |
| `GET` | `/api/auth/mfa/status` | 取得 MFA 狀態 | `[Authorize]` |
| `POST` | `/api/auth/mfa/enable` | 啟用 MFA | `[Authorize]` |
| `POST` | `/api/auth/mfa/verify` | 驗證 MFA 設定（回傳復原碼） | `[Authorize]` |
| `POST` | `/api/auth/mfa/disable` | 停用 MFA | `[Authorize]` |
| `POST` | `/api/auth/mfa/recovery-codes` | 重新產生復原碼 | `[Authorize]` |

### RateLimitedAuthController — 進階認證

不繼承 MfaAuthController，只提供下列端點（沒有 `login/mfa/email` 與 `mfa/*`）：

| 方法 | 路由 | 說明 | 權限 |
|---|---|---|---|
| `POST` | `/api/auth/register` | 註冊（含 IP 速率限制） | 匿名 |
| `POST` | `/api/auth/login` | 登入（含 IP 封鎖檢查 + 速率限制 + 工作階段建立） | 匿名 |
| `POST` | `/api/auth/login/mfa` | MFA 驗證（含速率限制 + 工作階段建立） | 匿名 |
| `GET` | `/api/auth/sessions` | 取得當前使用者的工作階段列表 | `[Authorize]` |
| `POST` | `/api/auth/logout` | 登出當前工作階段 | `[Authorize]` |
| `POST` | `/api/auth/logout/all` | 登出所有工作階段 | `[Authorize]` |
| `GET` | `/api/auth/connection-info` | 取得連線資訊（IP、裝置、瀏覽器） | 匿名 |
| `GET` | `/api/auth/login-history?count=10` | 取得登入歷史 | `[Authorize]` |

**速率限制行為：**
- 觸發速率限制時回傳 `429 Too Many Requests`
- 回應標頭含 `X-RateLimit-Limit`、`X-RateLimit-Remaining`、`X-RateLimit-Reset`（ISO-8601 時間）、`Retry-After`
- 登入被限流時，若該 IP 最近 20 次登入嘗試中有 15 次以上失敗，會被標記為可疑 24 小時（套用較嚴格的限流，並非封鎖）
- 被封鎖的 IP 回傳 `403 Forbidden`

## 使用範例

### 註冊

```http
POST /api/auth/register
Content-Type: application/json

{
    "email": "user@example.com",
    "password": "SecurePassword123!",
    "enableMfa": true
}
```

### 兩步驟登入

```http
# 第一步：帳密驗證
POST /api/auth/login
Content-Type: application/json

{
    "email": "user@example.com",
    "password": "SecurePassword123!"
}
# 回傳：{ requiresMfa: true, mfaToken: "..." }

# 第二步：MFA 驗證
POST /api/auth/login/mfa
Content-Type: application/json

{
    "mfaToken": "...",
    "code": "123456",
    "method": "Totp",
    "isRecoveryCode": false
}
```

### 啟用 MFA

```http
POST /api/auth/mfa/enable
Authorization: Bearer {token}
Content-Type: application/json

{
    "method": "Totp"
}
# 回傳 QR Code / Secret

POST /api/auth/mfa/verify
Authorization: Bearer {token}
Content-Type: application/json

{
    "code": "123456",
    "method": "Totp"
}
# 回傳復原碼
```

## 依賴清單

| 依賴 | 說明 |
|---|---|
| `IMfaAuthService` | MFA 認證服務介面（兩個控制器皆需要） |
| `IIpRateLimiter` | IP 速率限制服務（僅 RateLimitedAuthController） |
| `IConnectionInfoService` | 連線資訊服務（僅 RateLimitedAuthController） |
| `IUserSessionService` | 使用者工作階段服務（僅 RateLimitedAuthController） |
| `ILogger<T>` | ASP.NET Core 日誌服務 |
| `Microsoft.AspNetCore.Authorization` | 授權屬性 |
| `Microsoft.AspNetCore.Mvc` | MVC 控制器基底 |

## 安全特性

- IP 遮罩：日誌中的 IP 僅顯示前三組（如 `192.168.1.*`）

- Email 遮罩：日誌中的 Email 僅顯示首尾字元（如 `u***r@example.com`）

- Session ID 遮罩：僅顯示前後 4 碼

- 登入失敗時不回傳具體錯誤原因（防止使用者列舉）

- MFA Token 有時效性，過期自動失效
