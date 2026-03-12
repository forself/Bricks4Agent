# SPA Generator

Bricks4Agent SPA Generator 的 Web UI 與 backend 範本位於這個目錄。

## Quick Start

### Backend API

```bash
cd backend
dotnet restore
dotnet run
```

預設 API 位址為 `https://localhost:5002`。

### Frontend Web UI

**建議使用 Node server.js**（完整支援 API 路由與元件庫路徑）：

```bash
node server.js
```

或使用啟動腳本（自動選擇最佳伺服器）：

```bash
# Windows
start.bat

# macOS/Linux
./start.sh
```

然後開啟 `http://localhost:3080`。


## Default Admin

- Email: `admin@generator.local`
- Password: 請以 `backend/appsettings.json` 內的設定為準

## Structure

```text
spa-generator/
├── backend/                 # .NET 8 Minimal API
│   ├── Data/
│   ├── Models/
│   ├── Services/
│   ├── Program.cs
│   └── appsettings.json
├── frontend/                # Vanilla JS SPA
│   ├── core/
│   ├── pages/
│   ├── styles/
│   └── index.html
├── server.js                # Node dev server (recommended)
├── start.bat
├── start.sh
└── project.json
```

## Security Notes

- PBKDF2 password hashing
- JWT authentication with Bearer token header
- Token stored in localStorage (client-side)
- XSS mitigation via security headers
- CORS control
- Rate limiting on auth endpoints
- Security audit logging

> **生產環境注意**: 務必透過環境變數設定 `Jwt:Key`，
> 不要使用 `appsettings.json` 中的開發預設值。
> 考慮將 token 改為 HttpOnly Cookie 以增強 XSS 防護。

## Extending

新增頁面時，至少同步更新：

1. `frontend/pages/` 內的頁面模組
2. `frontend/pages/routes.js`
3. `backend/Models/`、`AppDbContext.cs`、`Program.cs` 中的後端對應項目

這個工具使用 repo 內的 `templates/spa/` 範本產生專案。
