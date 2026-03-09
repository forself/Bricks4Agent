#!/usr/bin/env node
/**
 * SPA 專案生成器
 *
 * 用法:
 *   node create-project.js
 *   node create-project.js --config project.json
 *   node create-project.js --name my-app --output ../../../projects
 *
 * @module create-project
 */

const fs = require('fs');
const path = require('path');
const readline = require('readline');

// ===== 配置 =====
const TEMPLATE_DIR = path.join(__dirname, '..');
const DEFAULT_OUTPUT = path.join(__dirname, '..', '..', '..', '..', 'projects');

// ===== 工具函數 =====

/**
 * 建立 readline 介面
 */
function createPrompt() {
    return readline.createInterface({
        input: process.stdin,
        output: process.stdout
    });
}

/**
 * 提問並取得回答
 */
function ask(rl, question, defaultValue = '') {
    const defaultHint = defaultValue ? ` [${defaultValue}]` : '';
    return new Promise(resolve => {
        rl.question(`${question}${defaultHint}: `, answer => {
            resolve(answer.trim() || defaultValue);
        });
    });
}

/**
 * 提問是/否
 */
async function askYesNo(rl, question, defaultYes = true) {
    const hint = defaultYes ? '[Y/n]' : '[y/N]';
    const answer = await ask(rl, `${question} ${hint}`, defaultYes ? 'y' : 'n');
    return answer.toLowerCase() === 'y';
}

/**
 * 產生隨機字串
 */
function generateRandomString(length = 32) {
    const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*';
    let result = '';
    for (let i = 0; i < length; i++) {
        result += chars.charAt(Math.floor(Math.random() * chars.length));
    }
    return result;
}

/**
 * 複製目錄
 */
function copyDir(src, dest, replacements = {}) {
    if (!fs.existsSync(dest)) {
        fs.mkdirSync(dest, { recursive: true });
    }

    const entries = fs.readdirSync(src, { withFileTypes: true });

    for (const entry of entries) {
        const srcPath = path.join(src, entry.name);
        const destPath = path.join(dest, entry.name);

        // 跳過 scripts 目錄和特定檔案
        if (entry.name === 'scripts' || entry.name === 'node_modules' || entry.name === '.git') {
            continue;
        }

        if (entry.isDirectory()) {
            copyDir(srcPath, destPath, replacements);
        } else {
            copyFile(srcPath, destPath, replacements);
        }
    }
}

/**
 * 複製並替換檔案內容
 */
function copyFile(src, dest, replacements = {}) {
    let content = fs.readFileSync(src, 'utf8');

    // 執行替換
    for (const [key, value] of Object.entries(replacements)) {
        const regex = new RegExp(escapeRegex(key), 'g');
        content = content.replace(regex, value);
    }

    fs.writeFileSync(dest, content, 'utf8');
}

/**
 * 轉義正則表達式特殊字元
 */
function escapeRegex(str) {
    return str.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/**
 * 解析命令列參數
 */
function parseArgs() {
    const args = {};
    const argv = process.argv.slice(2);

    for (let i = 0; i < argv.length; i++) {
        if (argv[i].startsWith('--')) {
            const key = argv[i].slice(2);
            const value = argv[i + 1] && !argv[i + 1].startsWith('--') ? argv[i + 1] : true;
            args[key] = value;
            if (value !== true) i++;
        }
    }

    return args;
}

// ===== 主程式 =====

async function main() {
    console.log('');
    console.log('╔════════════════════════════════════════╗');
    console.log('║      SPA 專案生成器 v1.0.0             ║');
    console.log('║      SQLite + .NET 8 + Vanilla JS      ║');
    console.log('╚════════════════════════════════════════╝');
    console.log('');

    const args = parseArgs();

    // 檢查是否有配置檔
    if (args.config) {
        const configPath = path.resolve(args.config);
        if (fs.existsSync(configPath)) {
            const config = JSON.parse(fs.readFileSync(configPath, 'utf8'));
            await createProject(config);
            return;
        } else {
            console.error(`錯誤: 找不到配置檔 ${configPath}`);
            process.exit(1);
        }
    }

    const rl = createPrompt();

    try {
        // 收集專案資訊
        console.log('請輸入專案資訊:\n');

        const config = {
            project: {},
            backend: {},
            frontend: {},
            security: {},
            admin: {}
        };

        // 專案基本資訊
        config.project.name = await ask(rl, '專案名稱', args.name || 'my-spa-app');
        config.project.displayName = await ask(rl, '顯示名稱', config.project.name);
        config.project.description = await ask(rl, '專案描述', '基於 SPA 範本建立的應用程式');
        config.project.outputDir = await ask(rl, '輸出目錄', args.output || DEFAULT_OUTPUT);

        console.log('');

        // 後端配置
        console.log('後端配置:\n');
        config.backend.dbName = await ask(rl, 'SQLite 資料庫檔名', `${config.project.name}.db`);
        config.backend.apiPort = await ask(rl, 'API 埠號', '5001');

        console.log('');

        // 前端配置
        console.log('前端配置:\n');
        config.frontend.devPort = await ask(rl, '開發伺服器埠號', '3000');
        config.frontend.apiBaseUrl = await ask(rl, 'API 基礎 URL', `https://localhost:${config.backend.apiPort}/api`);

        console.log('');

        // 安全性配置
        console.log('安全性配置:\n');
        const generateJwtKey = await askYesNo(rl, '自動產生 JWT 金鑰?', true);
        if (generateJwtKey) {
            config.security.jwtKey = generateRandomString(64);
            console.log(`  已產生 JWT 金鑰: ${config.security.jwtKey.substring(0, 20)}...`);
        } else {
            config.security.jwtKey = await ask(rl, 'JWT 金鑰 (至少 32 字元)');
        }
        config.security.jwtIssuer = await ask(rl, 'JWT Issuer', config.project.name);

        console.log('');

        // CORS 配置
        const corsOrigins = await ask(rl, 'CORS 允許來源 (逗號分隔)', `http://localhost:${config.frontend.devPort}`);
        config.security.corsOrigins = corsOrigins.split(',').map(s => s.trim());

        console.log('');

        // 管理員帳號
        console.log('初始管理員帳號:\n');
        config.admin.email = await ask(rl, 'Email', 'admin@example.com');
        config.admin.password = await ask(rl, '密碼', 'Admin@123');
        config.admin.name = await ask(rl, '姓名', 'Admin');

        console.log('');

        // 確認
        console.log('專案配置摘要:');
        console.log('─'.repeat(40));
        console.log(`專案名稱: ${config.project.name}`);
        console.log(`輸出路徑: ${path.join(config.project.outputDir, config.project.name)}`);
        console.log(`資料庫: ${config.backend.dbName}`);
        console.log(`API URL: https://localhost:${config.backend.apiPort}`);
        console.log(`管理員: ${config.admin.email}`);
        console.log('─'.repeat(40));
        console.log('');

        const confirm = await askYesNo(rl, '確認建立專案?', true);

        if (!confirm) {
            console.log('已取消。');
            rl.close();
            return;
        }

        rl.close();

        // 建立專案
        await createProject(config);

    } catch (error) {
        console.error('錯誤:', error.message);
        rl.close();
        process.exit(1);
    }
}

/**
 * 建立專案
 */
async function createProject(config) {
    const projectPath = path.join(config.project.outputDir, config.project.name);

    console.log('');
    console.log('正在建立專案...');
    console.log('');

    // 檢查目錄是否存在
    if (fs.existsSync(projectPath)) {
        console.error(`錯誤: 目錄已存在 ${projectPath}`);
        process.exit(1);
    }

    // 建立目錄
    fs.mkdirSync(projectPath, { recursive: true });

    // 定義替換規則
    const replacements = {
        // appsettings.json
        '"Data Source=spa_app.db"': `"Data Source=${config.backend.dbName}"`,
        '"Key": ""': `"Key": "${config.security.jwtKey}"`,
        '"Issuer": "SpaApi"': `"Issuer": "${config.security.jwtIssuer}"`,
        '"AdminEmail": "admin@example.com"': `"AdminEmail": "${config.admin.email}"`,
        '"AdminPassword": "Admin@123"': `"AdminPassword": "${config.admin.password}"`,
        '"AdminName": "Admin"': `"AdminName": "${config.admin.name}"`,

        // CORS origins
        '"http://localhost:3000"': `"${config.security.corsOrigins[0]}"`,

        // 專案名稱
        'SpaApi': config.project.name,
        'SPA 應用程式': config.project.displayName,
        '歡迎使用 SPA 應用程式': `歡迎使用 ${config.project.displayName}`,
    };

    // 複製 backend
    console.log('  [1/5] 複製後端範本...');
    copyDir(
        path.join(TEMPLATE_DIR, 'backend'),
        path.join(projectPath, 'backend'),
        replacements
    );

    // 更新 .csproj 檔案名稱
    const oldCsproj = path.join(projectPath, 'backend', 'SpaApi.csproj');
    const newCsproj = path.join(projectPath, 'backend', `${config.project.name}.csproj`);
    if (fs.existsSync(oldCsproj)) {
        fs.renameSync(oldCsproj, newCsproj);
    }

    // 複製 frontend
    console.log('  [2/5] 複製前端範本...');
    copyDir(
        path.join(TEMPLATE_DIR, 'frontend'),
        path.join(projectPath, 'frontend'),
        {
            ...replacements,
            "'https://localhost:5001/api'": `'${config.frontend.apiBaseUrl}'`
        }
    );

    // 複製工具
    console.log('  [3/5] 複製工具...');
    const toolsDir = path.join(__dirname, '..', '..', '..', 'tools', 'static-server');
    if (fs.existsSync(toolsDir)) {
        const destToolsDir = path.join(projectPath, 'tools', 'static-server');
        fs.mkdirSync(destToolsDir, { recursive: true });
        copyFile(
            path.join(toolsDir, 'StaticServer.cs'),
            path.join(destToolsDir, 'StaticServer.cs')
        );
        copyFile(
            path.join(toolsDir, 'StaticServer.csproj'),
            path.join(destToolsDir, 'StaticServer.csproj')
        );
    }

    // 建立啟動腳本
    console.log('  [4/5] 建立啟動腳本...');
    createStartupScripts(projectPath, config);

    // 建立 README
    console.log('  [5/5] 建立 README...');
    createReadme(projectPath, config);

    // 儲存配置
    const configPath = path.join(projectPath, 'project.json');
    const safeConfig = { ...config };
    safeConfig.security = { jwtIssuer: config.security.jwtIssuer }; // 不儲存金鑰
    safeConfig.admin = { email: config.admin.email, name: config.admin.name }; // 不儲存密碼
    fs.writeFileSync(configPath, JSON.stringify(safeConfig, null, 2), 'utf8');

    console.log('');
    console.log('╔════════════════════════════════════════╗');
    console.log('║          專案建立完成!                 ║');
    console.log('╚════════════════════════════════════════╝');
    console.log('');
    console.log(`專案路徑: ${projectPath}`);
    console.log('');
    console.log('下一步:');
    console.log('');
    console.log('  1. 啟動後端:');
    console.log(`     cd ${projectPath}/backend`);
    console.log('     dotnet restore');
    console.log('     dotnet run');
    console.log('');
    console.log('  2. 啟動前端 (任選一種):');
    console.log('     # C# 靜態伺服器');
    console.log(`     dotnet run --project ${projectPath}/tools/static-server -- ${projectPath}/frontend 3000`);
    console.log('     # Node.js');
    console.log(`     cd ${projectPath}/frontend && npx serve -l 3000`);
    console.log('     # Python');
    console.log(`     cd ${projectPath}/frontend && python -m http.server 3000`);
    console.log('');
    console.log('  3. 開啟瀏覽器: http://localhost:3000');
    console.log('');
    console.log(`  4. 登入管理員帳號: ${config.admin.email}`);
    console.log('');
}

/**
 * 建立啟動腳本
 */
function createStartupScripts(projectPath, config) {
    // Windows batch - 自動偵測可用工具
    const batContent = `@echo off
chcp 65001 >nul
echo Starting ${config.project.displayName}...
echo.

:: 啟動後端
cd /d "%~dp0backend"
start "API Server" dotnet run

:: 啟動前端 - 自動偵測可用工具
cd /d "%~dp0"
echo 正在偵測前端伺服器...

:: 優先使用 C# 靜態伺服器
if exist "tools\\static-server\\StaticServer.csproj" (
    echo 使用 C# 靜態伺服器
    start "Frontend" dotnet run --project tools\\static-server -- frontend 3000
    goto :started
)

:: 嘗試 Node.js
where node >nul 2>&1
if %errorlevel% equ 0 (
    echo 使用 Node.js (npx serve)
    cd frontend
    start "Frontend" npx serve -l 3000
    goto :started
)

:: 嘗試 Python
where python >nul 2>&1
if %errorlevel% equ 0 (
    echo 使用 Python http.server
    cd frontend
    start "Frontend" python -m http.server 3000
    goto :started
)

echo 警告: 找不到可用的前端伺服器
echo 請安裝 Node.js 或 Python，或編譯 C# 靜態伺服器
pause
exit /b 1

:started
echo.
echo API Server: https://localhost:${config.backend.apiPort}
echo Frontend:   http://localhost:3000
echo.
timeout /t 3 >nul
start http://localhost:3000
`;
    fs.writeFileSync(path.join(projectPath, 'start.bat'), batContent, 'utf8');

    // Unix shell - 自動偵測可用工具
    const shContent = `#!/bin/bash
echo "Starting ${config.project.displayName}..."
echo

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

# 啟動後端
cd "$SCRIPT_DIR/backend"
dotnet run &
BACKEND_PID=$!

# 啟動前端 - 自動偵測可用工具
cd "$SCRIPT_DIR"

start_frontend() {
    # 優先使用 C# 靜態伺服器
    if [ -f "tools/static-server/StaticServer.csproj" ]; then
        echo "使用 C# 靜態伺服器"
        dotnet run --project tools/static-server -- frontend 3000 &
        return 0
    fi

    # 嘗試 Node.js
    if command -v node &> /dev/null; then
        echo "使用 Node.js (npx serve)"
        cd frontend && npx serve -l 3000 &
        return 0
    fi

    # 嘗試 Python 3
    if command -v python3 &> /dev/null; then
        echo "使用 Python http.server"
        cd frontend && python3 -m http.server 3000 &
        return 0
    fi

    # 嘗試 Python
    if command -v python &> /dev/null; then
        echo "使用 Python http.server"
        cd frontend && python -m http.server 3000 &
        return 0
    fi

    echo "警告: 找不到可用的前端伺服器"
    echo "請安裝 Node.js 或 Python"
    return 1
}

start_frontend

echo
echo "API Server: https://localhost:${config.backend.apiPort}"
echo "Frontend:   http://localhost:3000"
echo

sleep 3
open http://localhost:3000 2>/dev/null || xdg-open http://localhost:3000 2>/dev/null || true

# 等待後端結束
wait $BACKEND_PID
`;
    fs.writeFileSync(path.join(projectPath, 'start.sh'), shContent, 'utf8');
}

/**
 * 建立 README
 */
function createReadme(projectPath, config) {
    const content = `# ${config.project.displayName}

${config.project.description}

## 快速開始

### 啟動後端 API

\`\`\`bash
cd backend
dotnet restore
dotnet run
\`\`\`

API 將運行於 https://localhost:${config.backend.apiPort}

### 啟動前端

選擇以下任一方式：

**C# 靜態伺服器 (推薦)**
\`\`\`bash
dotnet run --project tools/static-server -- frontend 3000
\`\`\`

**Node.js**
\`\`\`bash
cd frontend && npx serve -l 3000
\`\`\`

**Python**
\`\`\`bash
cd frontend && python -m http.server 3000
\`\`\`

前端將運行於 http://localhost:3000

### 預設管理員帳號

- Email: ${config.admin.email}
- 密碼: (請查看 appsettings.json 或建立時設定的密碼)

**重要**: 首次登入後請立即更改密碼！

## 專案結構

\`\`\`
${config.project.name}/
├── backend/                 # .NET 8 Minimal API
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   └── DbInitializer.cs
│   ├── Models/
│   ├── Services/
│   ├── Program.cs
│   └── appsettings.json
│
├── frontend/                # Vanilla JS SPA
│   ├── core/               # 核心模組
│   ├── pages/              # 頁面元件
│   ├── styles/             # 樣式表
│   └── index.html
│
├── tools/                   # 開發工具
│   └── static-server/      # C# 靜態伺服器
│
├── start.bat               # Windows 啟動腳本
├── start.sh                # Unix 啟動腳本
└── project.json            # 專案配置
\`\`\`

## 安全性功能

- PBKDF2 密碼雜湊 (100,000 iterations)
- JWT 認證
- XSS 防護
- 速率限制
- CORS 限制
- 安全標頭

## 新增頁面

1. 在 \`frontend/pages/\` 建立新的頁面類別
2. 在 \`frontend/pages/routes.js\` 註冊路由
3. 繼承 \`BasePage\` 並實作 \`template()\` 方法

## 新增 API 端點

1. 在 \`backend/Models/\` 建立資料模型
2. 在 \`AppDbContext.cs\` 新增 DbSet
3. 在 \`Program.cs\` 新增 API 端點

---

由 Bricks4Agent SPA 範本建立
`;

    fs.writeFileSync(path.join(projectPath, 'README.md'), content, 'utf8');
}

// 執行
main().catch(console.error);
