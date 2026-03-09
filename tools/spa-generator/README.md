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

建議優先使用 repo 內建的 C# 靜態伺服器。

從 repo 根目錄啟動：

```bash
npm run serve
```

或直接啟動靜態伺服器專案：

```bash
dotnet run --project ../static-server/StaticServer.csproj -- ./frontend 3080
```

如果只需要純靜態檔案伺服器，也可以使用：

```bash
cd frontend
npx serve -l 3080
```

然後開啟 `http://localhost:3080`。

> `server.js` 目前仍是 CommonJS 風格入口。若你的工作樹仍保留 root `package.json` 的 `"type": "module"` 設定，請先完成 CLI/server 的模組制式對齊，再使用 `node server.js`。

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
├── start.bat
├── start.sh
└── project.json
```

## Security Notes

- PBKDF2 password hashing
- JWT authentication
- XSS mitigation
- CORS control
- security audit logging

## Extending

新增頁面時，至少同步更新：

1. `frontend/pages/` 內的頁面模組
2. `frontend/pages/routes.js`
3. `backend/Models/`、`AppDbContext.cs`、`Program.cs` 中的後端對應項目

這個工具使用 repo 內的 `templates/spa/` 範本產生專案。
