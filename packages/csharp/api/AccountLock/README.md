# AccountLock

> ⚠ 參考程式碼：此目錄沒有 .csproj，未被任何專案編譯或引用，CI 也不建置它；以下說明未經建置驗證。
>
> 已知無法編譯：`AccountLockController.cs:121` 與 `:206` 的 `typeof(AccountLock)` 會觸發 CS0118（`AccountLock` 是命名空間而非型別）。

帳號鎖定管理 API 控制器 — 提供使用者帳號與 IP 位址的鎖定/解鎖管理端點。

## 初始化方式

```csharp
// IAccountLockService / AccountLockService 已由 security/AccountLock 提供，不需自行實作。
// 單獨 AddScoped<IAccountLockService, AccountLockService>() 會在 DI 解析時失敗（未註冊 IAccountLockRepository），
// 且 scoped 生命週期會讓內建的 InMemoryFailedAttemptTracker 每個請求重建，失敗次數永遠累計不到門檻、鎖定不會觸發。
builder.Services.AddSingleton<IAccountLockRepository, InMemoryAccountLockRepository>();
builder.Services.AddSingleton<IAccountLockService, AccountLockService>();
// Controller 由 DI 自動注入
```

## API 列表

### 當前使用者端點

| 方法 | 路由 | 說明 | 權限 |
|---|---|---|---|
| `GET` | `/api/account-locks/my/status` | 取得自己的鎖定狀態 | `[Authorize]` |
| `GET` | `/api/account-locks/my/history?limit=20` | 取得自己的鎖定歷史 | `[Authorize]` |

### 管理員 — 帳號鎖定

| 方法 | 路由 | 說明 | 權限 |
|---|---|---|---|
| `GET` | `/api/account-locks/users/{userId}/check` | 檢查使用者是否被鎖定 | `Admin,SecurityAdmin` |
| `GET` | `/api/account-locks/users/{userId}/status` | 取得使用者鎖定狀態 | `Admin,SecurityAdmin` |
| `GET` | `/api/account-locks/users/{userId}/history?limit=50` | 取得使用者鎖定歷史 | `Admin,SecurityAdmin` |
| `POST` | `/api/account-locks/users/{userId}/lock` | 鎖定使用者帳號 | `Admin,SecurityAdmin` |
| `POST` | `/api/account-locks/users/{userId}/unlock` | 解鎖使用者帳號 | `Admin,SecurityAdmin` |
| `GET` | `/api/account-locks/active?page=1&pageSize=50` | 取得所有生效中的鎖定 | `Admin,SecurityAdmin` |
| `GET` | `/api/account-locks/{lockId}` | 依 ID 取得鎖定詳情（尚未實作：TODO stub，一律回傳 404） | `Admin,SecurityAdmin` |
| `DELETE` | `/api/account-locks/{lockId}` | 依 ID 解除鎖定 | `Admin,SecurityAdmin` |

### 管理員 — IP 鎖定

| 方法 | 路由 | 說明 | 權限 |
|---|---|---|---|
| `GET` | `/api/account-locks/ips/check?ipAddress=...` | 檢查 IP 是否被鎖定 | `Admin,SecurityAdmin` |
| `POST` | `/api/account-locks/ips/lock` | 鎖定 IP 位址 | `Admin,SecurityAdmin` |
| `POST` | `/api/account-locks/ips/unlock` | 解鎖 IP 位址 | `Admin,SecurityAdmin` |
| `GET` | `/api/account-locks/ips/active?page=1&pageSize=50` | 取得所有生效中的 IP 鎖定 | `Admin,SecurityAdmin` |
| `DELETE` | `/api/account-locks/ips/{lockId}` | 依 ID 解除 IP 鎖定 | `Admin,SecurityAdmin` |

### 統計

| 方法 | 路由 | 說明 | 權限 |
|---|---|---|---|
| `GET` | `/api/account-locks/statistics?days=7` | 取得鎖定統計（1-90 天） | `Admin,SecurityAdmin` |

## 使用範例

### 鎖定使用者帳號

```http
POST /api/account-locks/users/42/lock
Content-Type: application/json

{
    "lockType": "Manual",
    "scope": "Account",
    "reason": "多次登入失敗",
    "durationMinutes": 30,
    "invalidateSessions": true,
    "notifyUser": true
}
```

### 解鎖使用者帳號

```http
POST /api/account-locks/users/42/unlock
Content-Type: application/json

{
    "reason": "確認非惡意行為"
}
```

### 鎖定 IP

```http
POST /api/account-locks/ips/lock
Content-Type: application/json

{
    "ipAddress": "192.168.1.100",
    "lockType": "Manual",
    "reason": "可疑活動",
    "durationMinutes": 60
}
```

## 依賴清單

| 依賴 | 說明 |
|---|---|
| `IAccountLockService` | 帳號鎖定服務介面（實作 `AccountLockService` 位於 `security/AccountLock`，另需註冊 `IAccountLockRepository`） |
| `ILogger<AccountLockController>` | ASP.NET Core 日誌服務 |
| `Microsoft.AspNetCore.Authorization` | 授權屬性 |
| `Microsoft.AspNetCore.Mvc` | MVC 控制器基底 |

## 相關 DTO

- `LockUserRequest` — 鎖定使用者請求（LockType, Scope, Reason, DurationMinutes 等）

- `UnlockUserRequest` — 解鎖使用者請求（LockId, Scope, Reason）

- `LockIpApiRequest` — 鎖定 IP 請求（IpAddress, LockType, Reason, DurationMinutes）

- `UnlockIpRequest` — 解鎖 IP 請求（IpAddress）

- `UnlockResponse` — 解鎖回應（UnlockedCount）
