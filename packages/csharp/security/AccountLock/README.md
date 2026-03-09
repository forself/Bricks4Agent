# Account Lock Module

帳戶臨時鎖定與解鎖模組，提供自動鎖定、漸進式鎖定、IP 封鎖等功能。

## 功能特點

- **自動鎖定**：登入失敗、MFA 失敗自動觸發鎖定
- **漸進式鎖定**：重複違規者鎖定時間遞增
- **多範圍鎖定**：帳戶、登入、MFA、敏感操作獨立鎖定
- **IP 封鎖**：可疑 IP 自動或手動封鎖
- **自動解鎖**：到期自動解除鎖定
- **完整歷史**：鎖定/解鎖歷史記錄

## 安裝

```bash
dotnet add package AccountLock
```

## 快速開始

### 1. 註冊服務

```csharp
// Program.cs
builder.Services.AddSingleton<IAccountLockRepository, InMemoryAccountLockRepository>();
builder.Services.AddSingleton<IAccountLockService, AccountLockService>();
builder.Services.AddSingleton(new AccountLockConfig
{
    MaxFailedLoginAttempts = 5,
    FailedLoginLockMinutes = 15,
    MaxFailedMfaAttempts = 5,
    FailedMfaLockMinutes = 30,
    EnableProgressiveLockout = true
});
```

### 2. 使用中介軟體（可選）

```csharp
// 在認證後、授權前加入
app.UseAuthentication();
app.UseAccountLockCheck();  // 檢查帳戶鎖定
app.UseAuthorization();
```

### 3. 整合登入流程

```csharp
public class AuthService
{
    private readonly IAccountLockService _lockService;
    private readonly AuthLockIntegration _lockIntegration;

    public AuthService(IAccountLockService lockService)
    {
        _lockService = lockService;
        _lockIntegration = new AuthLockIntegration(lockService);
    }

    public LoginResult Login(string email, string password, string ipAddress)
    {
        // 1. 先檢查是否被鎖定
        var lockCheck = _lockIntegration.CheckLoginAllowed(null, ipAddress);
        if (lockCheck.IsLocked)
        {
            return new LoginResult
            {
                Success = false,
                Error = lockCheck.Message,
                RetryAfterSeconds = lockCheck.RetryAfterSeconds
            };
        }

        // 2. 查詢用戶
        var user = _userRepository.GetByEmail(email);
        if (user != null)
        {
            // 檢查此用戶是否被鎖定
            lockCheck = _lockIntegration.CheckLoginAllowed(user.Id, ipAddress);
            if (lockCheck.IsLocked)
            {
                return new LoginResult
                {
                    Success = false,
                    Error = lockCheck.Message,
                    RetryAfterSeconds = lockCheck.RetryAfterSeconds
                };
            }
        }

        // 3. 驗證密碼
        if (!ValidatePassword(user, password))
        {
            // 記錄失敗並檢查是否觸發鎖定
            var failResult = _lockIntegration.HandleFailedLogin(
                user?.Id, email, ipAddress);

            if (failResult.IsLocked)
            {
                return new LoginResult
                {
                    Success = false,
                    Error = failResult.Message,
                    RetryAfterSeconds = failResult.RetryAfterSeconds
                };
            }

            return new LoginResult { Success = false, Error = "Invalid credentials" };
        }

        // 4. 登入成功，重置計數器
        _lockIntegration.HandleSuccessfulLogin(user.Id, ipAddress);

        return new LoginResult { Success = true, User = user };
    }
}
```

## 鎖定類型

| 類型 | 說明 | 預設持續時間 |
|------|------|-------------|
| `Manual` | 管理員手動鎖定 | 永久（直到解鎖） |
| `FailedLogin` | 登入失敗過多 | 15 分鐘 |
| `FailedMfa` | MFA 驗證失敗過多 | 30 分鐘 |
| `SuspiciousActivity` | 可疑活動 | 1 小時 |
| `RateLimitExceeded` | 超過頻率限制 | 15 分鐘 |
| `PasswordReset` | 密碼重設中 | 視流程而定 |
| `PolicyViolation` | 違反安全政策 | 永久 |
| `CompromiseDetected` | 帳戶疑似被入侵 | 永久 |
| `Inactivity` | 長期未使用 | 永久 |
| `Maintenance` | 系統維護 | 視維護而定 |

## 鎖定範圍

| 範圍 | 說明 |
|------|------|
| `Account` | 鎖定整個帳戶（最嚴格） |
| `Login` | 只鎖定登入，現有會話可用 |
| `IpAddress` | 鎖定特定 IP 對此帳戶的存取 |
| `Mfa` | 鎖定 MFA 驗證 |
| `PasswordChange` | 鎖定密碼變更 |
| `SensitiveOperations` | 鎖定敏感操作 |

## 漸進式鎖定

重複違規者的鎖定時間會遞增：

| 違規次數 | 乘數 | 實際鎖定時間（基準 15 分鐘）|
|---------|------|---------------------------|
| 第 1 次 | 1x | 15 分鐘 |
| 第 2 次 | 2x | 30 分鐘 |
| 第 3 次 | 4x | 1 小時 |
| 第 4 次 | 8x | 2 小時 |
| 第 5 次+ | 24x | 6 小時 |

```csharp
// 設定漸進式鎖定
var config = new AccountLockConfig
{
    EnableProgressiveLockout = true,
    ProgressiveMultipliers = new List<double> { 1, 2, 4, 8, 24 },
    ProgressiveWindowHours = 24  // 24 小時內計算
};
```

## API 端點

### 用戶端點

| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/api/account-locks/my/status` | 取得自己的鎖定狀態 |
| GET | `/api/account-locks/my/history` | 取得自己的鎖定歷史 |

### 管理端點（需要 Admin/SecurityAdmin 角色）

#### 帳戶鎖定管理

| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/api/account-locks/users/{userId}/check` | 檢查用戶是否被鎖定 |
| GET | `/api/account-locks/users/{userId}/status` | 取得用戶鎖定狀態 |
| GET | `/api/account-locks/users/{userId}/history` | 取得用戶鎖定歷史 |
| POST | `/api/account-locks/users/{userId}/lock` | 鎖定用戶 |
| POST | `/api/account-locks/users/{userId}/unlock` | 解鎖用戶 |
| GET | `/api/account-locks/active` | 取得所有活躍鎖定 |
| DELETE | `/api/account-locks/{lockId}` | 移除特定鎖定 |

#### IP 鎖定管理

| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/api/account-locks/ips/check?ipAddress=x` | 檢查 IP 是否被鎖定 |
| POST | `/api/account-locks/ips/lock` | 鎖定 IP |
| POST | `/api/account-locks/ips/unlock` | 解鎖 IP |
| GET | `/api/account-locks/ips/active` | 取得所有活躍 IP 鎖定 |
| DELETE | `/api/account-locks/ips/{lockId}` | 移除 IP 鎖定 |

#### 統計

| 方法 | 路徑 | 說明 |
|------|------|------|
| GET | `/api/account-locks/statistics` | 取得鎖定統計 |

## 使用範例

### 手動鎖定用戶

```csharp
// 鎖定 15 分鐘
_lockService.LockAccount(new LockAccountRequest
{
    UserId = userId,
    LockType = LockType.Manual,
    Scope = LockScope.Account,
    Reason = "Suspicious activity detected",
    DurationMinutes = 15
}, adminUserId: currentAdminId, adminUsername: currentAdminName);

// 永久鎖定
_lockService.LockAccount(new LockAccountRequest
{
    UserId = userId,
    LockType = LockType.PolicyViolation,
    Scope = LockScope.Account,
    Reason = "Terms of service violation",
    DurationMinutes = null  // 永久
}, adminUserId: currentAdminId, adminUsername: currentAdminName);
```

### 解鎖用戶

```csharp
// 解除所有鎖定
_lockService.UnlockAccount(new UnlockAccountRequest
{
    UserId = userId,
    Reason = "Customer support request"
}, adminUserId: currentAdminId, adminUsername: currentAdminName);

// 只解除登入鎖定
_lockService.UnlockAccount(new UnlockAccountRequest
{
    UserId = userId,
    Scope = LockScope.Login,
    Reason = "Password verified via phone"
}, adminUserId: currentAdminId, adminUsername: currentAdminName);
```

### 檢查鎖定狀態

```csharp
var result = _lockService.CheckLock(userId);
if (result.IsLocked)
{
    Console.WriteLine($"Account locked: {result.Message}");
    Console.WriteLine($"Unlock in: {result.RetryAfterSeconds} seconds");
}

// 取得完整狀態
var status = _lockService.GetUserLockStatus(userId);
Console.WriteLine($"Active locks: {status.ActiveLocks.Count}");
Console.WriteLine($"Total lock history: {status.TotalLockCount}");
```

### 鎖定 IP

```csharp
// 鎖定可疑 IP
_lockService.LockIpForSuspiciousActivity(
    ipAddress: "192.168.1.100",
    reason: "Brute force attack detected",
    durationMinutes: 60);

// 永久封鎖
_lockService.LockIp(new LockIpRequest
{
    IpAddress = "10.0.0.50",
    LockType = LockType.Manual,
    Reason = "Known malicious IP",
    DurationMinutes = null
}, adminUserId: adminId);
```

## 回應格式

### 被鎖定時的 API 回應

```json
{
  "error": "account_locked",
  "message": "Account temporarily locked due to multiple failed login attempts. Please try again in 14 minute(s).",
  "lockType": "FailedLogin",
  "expiresAt": "2026-01-25T12:30:00Z",
  "retryAfterSeconds": 840
}
```

HTTP 狀態碼：`423 Locked`

回應標頭：`Retry-After: 840`

## 事件通知

```csharp
var lockService = new AccountLockService(repository, config);

// 訂閱鎖定事件
lockService.OnAccountLocked += (lock) =>
{
    // 發送通知、記錄日誌等
    _notificationService.NotifyUserLocked(lock.UserId, lock.Reason);
    _auditLog.LogAccountLocked(lock);
};

lockService.OnAccountUnlocked += (lock) =>
{
    _notificationService.NotifyUserUnlocked(lock.UserId);
};

lockService.OnIpLocked += (lock) =>
{
    _alertService.SendSecurityAlert($"IP blocked: {lock.IpAddress}");
};
```

## 設定選項

```csharp
var config = new AccountLockConfig
{
    // 登入失敗設定
    MaxFailedLoginAttempts = 5,        // 最大失敗次數
    FailedLoginWindowMinutes = 15,     // 計算視窗
    FailedLoginLockMinutes = 15,       // 鎖定時間

    // MFA 失敗設定
    MaxFailedMfaAttempts = 5,
    FailedMfaLockMinutes = 30,

    // 漸進式鎖定
    EnableProgressiveLockout = true,
    ProgressiveMultipliers = new List<double> { 1, 2, 4, 8, 24 },
    ProgressiveWindowHours = 24,

    // 自動解鎖
    AutoUnlockExpired = true,

    // 閒置鎖定（可選）
    InactiveDaysBeforeLock = null  // 設定天數啟用
};
```

## 與其他模組整合

### 與 RateLimiting 模組整合

```csharp
// 當限流觸發時鎖定帳戶
if (!rateLimitResult.IsAllowed)
{
    _lockService.LockAccount(new LockAccountRequest
    {
        UserId = userId,
        LockType = LockType.RateLimitExceeded,
        Scope = LockScope.Account,
        Reason = "Rate limit exceeded",
        DurationMinutes = 15
    });
}
```

### 與 AuditLog 模組整合

```csharp
lockService.OnAccountLocked += (lock) =>
{
    _securityLogService.LogAccountEvent(
        SecurityEventType.AccountLocked,
        lock.UserId,
        lock.Username,
        lock.TriggerIpAddress,
        $"Lock type: {lock.LockType}, Scope: {lock.Scope}, Duration: {lock.ExpiresAt}"
    );
};
```

## 生產環境建議

1. **使用資料庫儲存**：替換 InMemory 實作
2. **分散式鎖定**：使用 Redis 實現跨實例同步
3. **通知整合**：鎖定時發送 Email/SMS 通知
4. **監控告警**：大量鎖定時觸發告警
5. **定期清理**：清理過期的鎖定記錄

## 相依套件

- `Microsoft.AspNetCore.Http.Abstractions` >= 2.2.0

## 授權

MIT License
