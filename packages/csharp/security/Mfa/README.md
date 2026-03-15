# MFA Authentication 多因子驗證模組

完整的多因子驗證 (MFA) 解決方案，支援 TOTP、Email OTP 與復原碼。

## 特點

- **TOTP 支援** - 相容 Google Authenticator、Microsoft Authenticator、Authy 等
- **Email OTP** - 透過電子郵件發送一次性驗證碼
- **復原碼** - 10 組一次性備用碼，用於緊急存取
- **安全設計** - 常數時間比較、帳號鎖定、速率限制
- **兩步驟登入** - 先驗證密碼，再驗證 MFA
- **彈性整合** - 可自訂儲存庫與郵件服務

## 快速開始

### 1. 服務註冊 (Program.cs)

```csharp
using YourNamespace.Security.Mfa;

var builder = WebApplication.CreateBuilder(args);

// MFA 服務
builder.Services.AddSingleton<IMfaRepository, InMemoryMfaRepository>();
builder.Services.AddSingleton<IUserRepository, InMemoryUserRepository>();
builder.Services.AddSingleton<IEmailService, SmtpEmailService>(); // 或自訂實作
builder.Services.AddSingleton<IMfaService, MfaService>();
builder.Services.AddSingleton<IMfaAuthService, MfaAuthService>();

// JWT 認證
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => { /* 配置 */ });

var app = builder.Build();
app.MapControllers();
app.Run();
```

### 2. 配置 (appsettings.json)

```json
{
  "Jwt": {
    "Key": "YourSuperSecretKeyAtLeast32Characters!",
    "Issuer": "YourApp",
    "ExpirationMinutes": 60
  },
  "Mfa": {
    "AppName": "MyApp",
    "TokenExpirationMinutes": 5,
    "EnforceMfa": false
  }
}
```

## API 端點

### 註冊

```http
POST /api/auth/register
Content-Type: application/json

{
  "name": "John Doe",
  "email": "john@example.com",
  "password": "SecurePass123!",
  "enableMfa": true,
  "mfaMethod": 1  // 1=TOTP, 2=Email
}
```

回應 (啟用 MFA 時):
```json
{
  "success": true,
  "userId": 1,
  "mfaSetup": {
    "success": true,
    "method": 1,
    "totpSecret": "JBSWY3DPEHPK3PXP",
    "qrCodeUri": "otpauth://totp/MyApp:john@example.com?secret=JBSWY3DPEHPK3PXP&issuer=MyApp"
  }
}
```

### 登入 (步驟 1 - 驗證密碼)

```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "john@example.com",
  "password": "SecurePass123!"
}
```

回應 (需要 MFA):
```json
{
  "success": true,
  "requiresMfa": true,
  "mfaToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "availableMethods": [1, 99]  // 1=TOTP, 99=復原碼
}
```

### 登入 (步驟 2 - 驗證 MFA)

```http
POST /api/auth/login/mfa
Content-Type: application/json

{
  "mfaToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "code": "123456",
  "method": 1
}
```

回應:
```json
{
  "success": true,
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "...",
  "user": {
    "id": 1,
    "name": "John Doe",
    "email": "john@example.com",
    "role": "user",
    "mfaEnabled": true
  }
}
```

### 使用復原碼登入

```http
POST /api/auth/login/mfa
Content-Type: application/json

{
  "mfaToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "code": "ABCD-EFGH-IJKL",
  "isRecoveryCode": true
}
```

### 啟用 MFA

```http
POST /api/auth/mfa/enable
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "method": 1
}
```

回應:
```json
{
  "success": true,
  "method": 1,
  "totpSecret": "JBSWY3DPEHPK3PXP",
  "qrCodeUri": "otpauth://totp/MyApp:john@example.com?secret=..."
}
```

### 驗證 MFA 設定

```http
POST /api/auth/mfa/verify
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "code": "123456",
  "method": 1
}
```

回應 (包含復原碼):
```json
{
  "success": true,
  "recoveryCodes": [
    "ABCD-EFGH-IJKL",
    "MNOP-QRST-UVWX",
    "..."
  ],
  "message": "MFA enabled successfully. Please save your recovery codes."
}
```

### 停用 MFA

```http
POST /api/auth/mfa/disable
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "code": "123456"
}
```

### 重新產生復原碼

```http
POST /api/auth/mfa/recovery-codes
Authorization: Bearer {accessToken}
Content-Type: application/json

{
  "code": "123456"
}
```

### 取得 MFA 狀態

```http
GET /api/auth/mfa/status
Authorization: Bearer {accessToken}
```

## TOTP (Google Authenticator)

### 產生 QR Code

使用回傳的 `qrCodeUri` 產生 QR Code:

```csharp
// 使用 QRCoder 套件
using QRCoder;

var qrGenerator = new QRCodeGenerator();
var qrCodeData = qrGenerator.CreateQrCode(qrCodeUri, QRCodeGenerator.ECCLevel.Q);
var qrCode = new PngByteQRCode(qrCodeData);
var qrBytes = qrCode.GetGraphic(20);
```

或在前端使用 JavaScript:

```javascript
// 使用 qrcode.js
QRCode.toDataURL(qrCodeUri, (err, url) => {
  document.getElementById('qrcode').src = url;
});
```

### 手動輸入

如果使用者無法掃描 QR Code，可以手動輸入:

- **帳號**: 使用者 Email
- **金鑰**: `totpSecret` (Base32 編碼)
- **時間基礎**: 30 秒
- **演算法**: SHA1

## 安全機制

### 帳號鎖定

- 連續失敗 5 次後鎖定 15 分鐘
- 成功驗證後重置計數器

### 常數時間比較

所有驗證碼比較都使用常數時間演算法，防止計時攻擊:

```csharp
private static bool ConstantTimeEquals(string a, string b)
{
    if (a.Length != b.Length) return false;
    var result = 0;
    for (int i = 0; i < a.Length; i++)
    {
        result |= a[i] ^ b[i];
    }
    return result == 0;
}
```

### MFA Token

MFA Token 是短期 JWT (5 分鐘)，只能用於完成 MFA 驗證:

```json
{
  "mfa_user_id": "123",
  "mfa_pending": "true",
  "exp": 1234567890
}
```

### 復原碼

- 10 組一次性復原碼
- 格式: `XXXX-XXXX-XXXX` (12 個英數字)
- 使用後立即失效
- 儲存時使用 SHA256 雜湊

## 自訂實作

### 資料庫儲存

實作 `IMfaRepository` 介面:

```csharp
public class SqlMfaRepository : IMfaRepository
{
    private readonly AppDb _db;

    public SqlMfaRepository(AppDb db)
    {
        _db = db;
    }

    public UserMfaConfig GetUserMfaConfig(int userId)
    {
        return _db.QueryFirst<UserMfaConfig>(
            "SELECT * FROM UserMfaConfigs WHERE UserId = @UserId",
            new { UserId = userId });
    }

    public void SaveUserMfaConfig(UserMfaConfig config)
    {
        if (config.Id == 0)
            _db.Insert(config);
        else
            _db.Update(config);
    }

    // ... 實作其他方法
}
```

### 郵件服務

實作 `IEmailService` 介面:

```csharp
public class SmtpEmailService : IEmailService
{
    private readonly SmtpClient _client;

    public SmtpEmailService(IConfiguration config)
    {
        _client = new SmtpClient(config["Smtp:Host"], int.Parse(config["Smtp:Port"]));
        _client.Credentials = new NetworkCredential(
            config["Smtp:Username"],
            config["Smtp:Password"]);
        _client.EnableSsl = true;
    }

    public bool SendEmail(string to, string subject, string body)
    {
        try
        {
            var message = new MailMessage("noreply@yourapp.com", to, subject, body);
            _client.Send(message);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
```

## 前端整合範例

### React

```jsx
import { useState } from 'react';

function Login() {
  const [step, setStep] = useState(1);
  const [mfaToken, setMfaToken] = useState('');
  const [mfaCode, setMfaCode] = useState('');

  const handleLogin = async (email, password) => {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password })
    });
    const data = await res.json();

    if (data.requiresMfa) {
      setMfaToken(data.mfaToken);
      setStep(2);
    } else if (data.accessToken) {
      // 登入成功
      localStorage.setItem('token', data.accessToken);
    }
  };

  const handleMfaVerify = async () => {
    const res = await fetch('/api/auth/login/mfa', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        mfaToken,
        code: mfaCode,
        method: 1
      })
    });
    const data = await res.json();

    if (data.accessToken) {
      localStorage.setItem('token', data.accessToken);
    }
  };

  return step === 1 ? (
    <LoginForm onSubmit={handleLogin} />
  ) : (
    <MfaForm
      code={mfaCode}
      onChange={setMfaCode}
      onSubmit={handleMfaVerify}
    />
  );
}
```

## 資料庫結構

### UserMfaConfigs

```sql
CREATE TABLE UserMfaConfigs (
    Id INT PRIMARY KEY IDENTITY,
    UserId INT NOT NULL UNIQUE,
    IsEnabled BIT NOT NULL DEFAULT 0,
    PrimaryMethod INT NOT NULL DEFAULT 1,
    TotpSecret NVARCHAR(100),
    TotpVerified BIT NOT NULL DEFAULT 0,
    OtpEmail NVARCHAR(254),
    OtpPhone NVARCHAR(20),
    RecoveryCodesRemaining INT NOT NULL DEFAULT 0,
    EnabledAt DATETIME2,
    LastVerifiedAt DATETIME2,
    FailedAttempts INT NOT NULL DEFAULT 0,
    LockedUntil DATETIME2,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);
```

### MfaRecoveryCodes

```sql
CREATE TABLE MfaRecoveryCodes (
    Id INT PRIMARY KEY IDENTITY,
    UserId INT NOT NULL,
    CodeHash NVARCHAR(100) NOT NULL,
    IsUsed BIT NOT NULL DEFAULT 0,
    UsedAt DATETIME2,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    INDEX IX_UserId (UserId)
);
```

### MfaOtpCodes

```sql
CREATE TABLE MfaOtpCodes (
    Id INT PRIMARY KEY IDENTITY,
    UserId INT NOT NULL,
    CodeHash NVARCHAR(100) NOT NULL,
    Method INT NOT NULL,
    Destination NVARCHAR(254),
    ExpiresAt DATETIME2 NOT NULL,
    IsUsed BIT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    INDEX IX_UserId_ExpiresAt (UserId, ExpiresAt)
);
```

## License

MIT
