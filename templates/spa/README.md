# SPA 應用程式範本

完整的單頁應用程式 (SPA) 範本，包含前端和後端。

## 目錄結構

```
spa/
├── frontend/                    # 前端 (Vanilla JS)
│   ├── index.html              # 入口頁面
│   ├── core/                   # 核心模組
│   │   ├── App.js              # 應用程式入口
│   │   ├── Router.js           # 路由系統
│   │   ├── Store.js            # 狀態管理
│   │   ├── ApiService.js       # API 服務
│   │   ├── Layout.js           # 佈局元件
│   │   ├── BasePage.js         # 頁面基礎類別
│   │   └── NestedPage.js       # 巢狀頁面類別
│   ├── pages/                  # 頁面元件
│   │   ├── routes.js           # 路由配置
│   │   ├── HomePage.js         # 首頁
│   │   ├── LoginPage.js        # 登入頁
│   │   ├── SettingsPage.js     # 設定頁
│   │   └── users/              # 使用者管理 (巢狀路由範例)
│   │       ├── UsersPage.js    # 使用者頁面容器
│   │       ├── UserListPage.js # 使用者列表
│   │       ├── UserDetailPage.js
│   │       └── UserCreatePage.js
│   ├── components/             # 可複用元件
│   └── styles/                 # CSS 樣式
│       ├── main.css            # 主樣式 (變數、重置)
│       ├── layout.css          # 佈局樣式
│       └── components.css      # 元件樣式
│
└── backend/                    # 後端 (C# .NET 8)
    ├── Program.cs              # 應用程式入口
    ├── SpaApi.csproj           # 專案檔
    ├── appsettings.json        # 設定檔
    ├── Data/
    │   └── AppDbContext.cs     # EF Core DbContext
    ├── Models/
    │   └── User.cs             # 使用者實體
    └── Services/               # 服務層
        ├── IUserService.cs
        ├── UserService.cs
        ├── IAuthService.cs
        └── AuthService.cs
```

## 快速開始

### 前端

選擇以下任一方式啟動本地伺服器：

**方式一：C# 靜態伺服器 (推薦)**
```bash
cd frontend
dotnet run --project ../tools/static-server -- . 3000
```

**方式二：Node.js**
```bash
cd frontend
npx serve -l 3000
```

**方式三：Python**
```bash
cd frontend
python -m http.server 3000
```

**方式四：VS Code Live Server**
- 安裝 Live Server 擴充套件
- 右鍵點擊 index.html → Open with Live Server

然後在瀏覽器開啟 http://localhost:3000

### 後端

1. 確保已安裝 .NET 8 SDK

2. 執行後端 API：

```bash
cd backend
dotnet restore
dotnet run
```

3. API 將在 `http://localhost:5000` 啟動

4. 預設管理員帳號：
   - Email: `admin@example.com`
   - Password: `admin123`

## 前端架構

### 核心模組

#### Router (路由系統)
- Hash 模式路由 (#/path)
- 支援動態參數 (/users/:id)
- 巢狀路由支援
- 路由守衛 (beforeEach, afterEach)

```javascript
// 路由配置範例
const routes = [
    { path: '/', component: HomePage },
    {
        path: '/users',
        component: UsersPage,
        children: [
            { path: '/', component: UserListPage },
            { path: '/:id', component: UserDetailPage }
        ]
    }
];
```

#### Store (狀態管理)
- 響應式狀態
- 訂閱/取消訂閱
- 持久化支援

```javascript
// 使用範例
store.set('user', userData);
store.subscribe('user', (user) => console.log(user));
```

#### ApiService (API 服務)
- RESTful 請求封裝
- JWT Token 管理
- 請求/響應攔截器
- 快取支援

```javascript
// 使用範例
const users = await api.get('/users');
await api.post('/users', { name: 'John' });
```

### 頁面類別

#### BasePage
所有頁面的基礎類別，提供：
- 生命週期鉤子 (onInit, onMounted, onDestroy)
- 響應式資料 (this.data)
- 事件綁定 (events())
- 工具方法 ($, $$, navigate, showMessage)

```javascript
class MyPage extends BasePage {
    async onInit() {
        this._data = { items: [] };
        const items = await this.api.get('/items');
        this.data.items = items;
    }

    template() {
        return `<ul>${this._data.items.map(i => `<li>${i.name}</li>`).join('')}</ul>`;
    }

    events() {
        return {
            'click .item': 'onItemClick'
        };
    }
}
```

#### NestedPage
用於包含子路由的頁面：

```javascript
class UsersPage extends NestedPage {
    getSubNav() {
        return [
            { path: '/users', label: '列表', exact: true },
            { path: '/users/create', label: '新增' }
        ];
    }

    template() {
        return `
            <h1>使用者管理</h1>
            ${this.renderSubNav()}
            ${this.renderOutlet()}
        `;
    }
}
```

## 後端架構

### 技術棧
- ASP.NET Core 8 Minimal API
- Entity Framework Core + SQLite
- JWT 認證

### API 端點

| 方法 | 路徑 | 說明 | 認證 |
|------|------|------|------|
| GET | /health | 健康檢查 | 否 |
| POST | /api/auth/login | 登入 | 否 |
| POST | /api/auth/register | 註冊 | 否 |
| GET | /api/users | 取得所有使用者 | 是 |
| GET | /api/users/:id | 取得單一使用者 | 是 |
| POST | /api/users | 建立使用者 | 是 |
| PUT | /api/users/:id | 更新使用者 | 是 |
| DELETE | /api/users/:id | 刪除使用者 | 是 |
| GET | /api/dashboard | 取得儀表板資料 | 是 |

### 資料庫

使用 SQLite，資料庫檔案為 `spa_app.db`，首次執行時自動建立。

## 主題系統

支援淺色/深色主題切換：

```css
/* 使用 CSS 變數 */
:root {
    --color-bg: #f5f7fa;
    --color-text: #1a1a2e;
}

[data-theme="dark"] {
    --color-bg: #1a1a2e;
    --color-text: #e4e4e7;
}
```

## 擴充指南

### 新增頁面

1. 在 `frontend/pages/` 建立頁面檔案：

```javascript
// pages/AboutPage.js
import { BasePage } from '../core/BasePage.js';

export class AboutPage extends BasePage {
    template() {
        return `<h1>關於我們</h1>`;
    }
}
```

2. 在 `routes.js` 新增路由：

```javascript
import { AboutPage } from './AboutPage.js';

export const routes = [
    // ...existing routes
    { path: '/about', component: AboutPage }
];
```

### 新增 API 端點

在 `Program.cs` 新增端點：

```csharp
app.MapGet("/api/items", async (AppDbContext db) =>
{
    var items = await db.Items.ToListAsync();
    return Results.Ok(items);
}).RequireAuthorization();
```

### 新增資料表

1. 在 `Models/` 建立實體類別
2. 在 `AppDbContext.cs` 加入 DbSet
3. 執行 EF Core Migration 或讓 EnsureCreated() 建立

## 部署

### 前端
將 `frontend/` 目錄部署到任何靜態檔案伺服器 (Nginx, Apache, CDN)。

### 後端
```bash
dotnet publish -c Release -o publish
```

將 `publish/` 目錄部署到伺服器，並確保：
1. 設定正確的 JWT Key
2. 設定資料庫連線字串
3. 設定 CORS 允許的來源
