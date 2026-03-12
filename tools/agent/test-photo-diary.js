#!/usr/bin/env node
'use strict';

/**
 * 測試腳本：使用 GPT-5 mini 建立照片日記網站
 *
 * 此腳本記錄 Agent 的完整執行過程，包含：
 * - 每次工具呼叫
 * - 模型回應
 * - 執行時間
 */

const { AgentLoop } = require('./lib/agent-loop');
const { createProvider } = require('./lib/providers/provider-factory');
const { resolveProjectRoot, bold, colorize, logInfo } = require('./lib/utils');
const fs = require('fs');
const path = require('path');

// ─── 配置 ───

const API_KEY = process.env.OPENAI_API_KEY;
if (!API_KEY) {
    console.error('錯誤：請設定 OPENAI_API_KEY 環境變數');
    console.error('  Windows: set OPENAI_API_KEY=sk-xxx');
    console.error('  Linux/Mac: export OPENAI_API_KEY=sk-xxx');
    process.exit(1);
}
const MODEL = 'gpt-5-mini';
const MAX_ITERATIONS = 30;

// ─── 任務指令 ───

const TASK_PROMPT = `你的任務是使用 Bricks4Agent 工具鏈建立一個「照片日記」網站。請嚴格按照以下步驟操作。

## 需求
- 使用者註冊與登入（使用範本內建的 AuthService）
- 日記 CRUD：標題、照片網址、內容、日期、是否公開
- SQLite 資料庫（範本預設）
- 使用自訂元件庫

## 步驟

### 步驟 1：建立專案配置
用 write_file 建立 projects/photo-diary-config.json，內容如下（JSON 格式）：

{
  "project": {
    "name": "PhotoDiary",
    "displayName": "照片日記",
    "description": "具備註冊登入的照片日記網站",
    "outputDir": "./projects"
  },
  "backend": {
    "dbName": "photodiary.db",
    "apiPort": "5001"
  },
  "frontend": {
    "devPort": "3000",
    "apiBaseUrl": "https://localhost:5001/api"
  },
  "security": {
    "jwtKey": "PhotoDiarySecretKey2024_ChangeInProduction_MustBe64CharsLong!!!",
    "jwtIssuer": "PhotoDiary",
    "corsOrigins": ["http://localhost:3000"]
  },
  "admin": {
    "email": "admin@photodiary.com",
    "password": "Admin@123",
    "name": "管理員"
  }
}

### 步驟 2：建立專案
執行指令：
node templates/spa/scripts/create-project.js --config projects/photo-diary-config.json

### 步驟 3：複製腳本到專案
generate-api.js 和 generate-page.js 使用 __dirname 定位輸出目錄，因此腳本必須在專案內。
執行指令：
xcopy templates\\spa\\scripts projects\\PhotoDiary\\scripts /E /I /Y

### 步驟 4：生成日記功能
在 projects/PhotoDiary 目錄中執行（用 run_command 的 cwd 參數指定工作目錄為 "projects/PhotoDiary"）：
node scripts/spa-cli.js feature DiaryEntry --fields "Title:string,PhotoUrl:string,Content:text,DiaryDate:datetime,IsPublic:bool"

### 步驟 5：更新 Program.cs
5a. 讀取 projects/PhotoDiary/backend/Program.cs
5b. 根據步驟 4 輸出的指示，在 Program.cs 的適當位置加入：
    - DiaryEntryService 的服務註冊（builder.Services.AddScoped<DiaryEntryService>();）
    - DiaryEntry CRUD API 端點（5 個：GetAll, GetById, Create, Update, Delete）
    - 所需的 using 語句
5c. 用 write_file 寫入更新後的完整 Program.cs

### 步驟 6：驗證
用 list_directory 列出 projects/PhotoDiary 目錄（depth=3），確認以下檔案存在：
- backend/Models/DiaryEntry.cs
- backend/Services/DiaryEntryService.cs
- frontend/pages/diaryentrys/DiaryEntryListPage.js
- frontend/pages/diaryentrys/DiaryEntryDetailPage.js

最後總結所有生成的檔案和功能說明。`;

// ─── 主程式 ───

async function main() {
    const startTime = Date.now();

    console.log('╔══════════════════════════════════════════════════╗');
    console.log('║  GPT-5 mini Agent 測試：照片日記網站             ║');
    console.log('╚══════════════════════════════════════════════════╝');
    console.log('');
    console.log(`模型: ${MODEL}`);
    console.log(`最大迭代: ${MAX_ITERATIONS}`);
    console.log(`時間: ${new Date().toISOString()}`);
    console.log('');

    // 確保 projects 目錄存在
    const projectRoot = resolveProjectRoot();
    const projectsDir = path.join(projectRoot, 'projects');
    if (!fs.existsSync(projectsDir)) {
        fs.mkdirSync(projectsDir, { recursive: true });
        console.log(`已建立 projects 目錄: ${projectsDir}`);
    }

    // 清理舊測試（如果存在）
    const targetDir = path.join(projectsDir, 'PhotoDiary');
    if (fs.existsSync(targetDir)) {
        console.log(`清理舊測試目錄: ${targetDir}`);
        fs.rmSync(targetDir, { recursive: true, force: true });
    }

    // 建立 Provider
    const provider = createProvider({
        provider: 'openai',
        apiKey: API_KEY,
    });

    // 建立 Agent
    const agent = new AgentLoop({
        model: MODEL,
        provider,
        projectRoot,
        stream: true,
        noConfirm: true,
        verbose: true,
        maxIterations: MAX_ITERATIONS,
    });

    console.log('═══════════════════════════════════════════════════');
    console.log('開始執行任務...');
    console.log('═══════════════════════════════════════════════════');
    console.log('');

    try {
        const result = await agent.send(TASK_PROMPT);

        const elapsed = Date.now() - startTime;
        const stats = agent.getStats();

        console.log('');
        console.log('═══════════════════════════════════════════════════');
        console.log('任務完成');
        console.log('═══════════════════════════════════════════════════');
        console.log(`耗時: ${(elapsed / 1000).toFixed(1)} 秒`);
        console.log(`訊息數: ${stats.messageCount}`);
        console.log(`總字元: ${stats.totalChars}`);
        console.log('');

        // 驗證生成結果
        console.log('═══ 檔案驗證 ═══');
        const expectedFiles = [
            'projects/PhotoDiary/backend/Models/DiaryEntry.cs',
            'projects/PhotoDiary/backend/Services/DiaryEntryService.cs',
            'projects/PhotoDiary/frontend/pages/diaryentrys/DiaryEntryListPage.js',
            'projects/PhotoDiary/frontend/pages/diaryentrys/DiaryEntryDetailPage.js',
            'projects/PhotoDiary/frontend/pages/routes.js',
            'projects/PhotoDiary/backend/Program.cs',
        ];

        let allFound = true;
        for (const f of expectedFiles) {
            const fullPath = path.join(projectRoot, f);
            const exists = fs.existsSync(fullPath);
            const icon = exists ? '✅' : '❌';
            console.log(`  ${icon} ${f}`);
            if (!exists) allFound = false;
        }

        console.log('');
        if (allFound) {
            console.log('🎉 所有檔案驗證通過！');

            // 額外檢查 Program.cs 是否包含 DiaryEntry 端點
            const programCs = fs.readFileSync(
                path.join(projectRoot, 'projects/PhotoDiary/backend/Program.cs'),
                'utf8'
            );
            const hasDiaryEndpoint = programCs.includes('DiaryEntry') || programCs.includes('diaryentry');
            console.log(`  ${hasDiaryEndpoint ? '✅' : '⚠️'} Program.cs 包含 DiaryEntry 端點: ${hasDiaryEndpoint}`);
        } else {
            console.log('⚠️ 部分檔案缺失');
        }

    } catch (e) {
        console.error('');
        console.error(`❌ 任務失敗: ${e.message}`);
        console.error(e.stack);
    }

    console.log('');
    console.log('測試結束');
}

main().catch(console.error);
