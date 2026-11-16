# 受治理生成第二刀：會保存資料的應用（presale-app-v1）

日期：2026-11-16
狀態：設計提案，尚未實作。前置文件：[GovernedGeneration.md](GovernedGeneration.md)（第一刀，已實作）。

## 1. 要解決的問題

目前有兩條互不相通的路：

| | 受治理生成（第一刀） | Presale 工作台 |
|---|---|---|
| 誰發動 | 使用者經 LINE／portal，或管理員替其他代理建任務 | 人在 `127.0.0.1:3091` 的表單上操作 |
| 誰寫內容 | 受控代理，經 broker PEP 呼叫三個能力 | 人填結構化訪談表單 |
| 產物 | `definition-site-v1`：多頁前端原型，資料只在瀏覽器分頁的記憶體裡 | Node.js 後端加 SQLite 的套件，`npm start` 後資料重啟仍在 |
| 治理 | grant、配額、稽核、產物 sha256、交付 | 無（只綁本機） |

所以「代理接到需求 → 產出資料會保存的系統」這件事現在做不到：代理走得到的那條路沒有後端，有後端的那條路代理走不到。

這份文件說明怎麼把 Presale 的編譯器接成受治理生成的第二種套件型別，讓同一條受治理路徑能交付有後端的應用。不新增第七條生成路徑：Presale 的編譯器本來就呼叫 `tools/generation` 的 `validateRequest` 與 `buildSite`（[server.mjs:104-111](../../tools/presale-studio/server.mjs)），產物是 `definition-site-v1` 的超集。

## 2. 現況中可以直接用的部分

以下都是 2026-11-16 在工作目錄上實際確認的：

- `buildPresalePackage(draft, pagePlan)`（[server.mjs:81](../../tools/presale-studio/server.mjs)）是純函式：輸入兩個 JSON，輸出一個目錄。它依序做 `validateDraft` → 複合元件定義驗證 → `validatePagePlan` → 瀏覽器 store 與 SQLite 的功能驗證 → DefinitionTemplate 驗證 → `buildSite` → 寫入後端與審查檔。任何一步失敗都不留下檔案。
- 同一份 draft 連續建置兩次，571 個檔逐檔 sha256 相同（課程範本，約 4.4 MB，每次約 0.3 秒，Node 22.13）。
- 產物頂層是 `site/`、`backend/`、`review/`、`report/`、`package.json`、`README.txt`。`backend/` 的 11 個 `.mjs` 是從 `tools/presale-studio/` 原樣複製的固定檔案；draft 的文字只落在 JSON 資料檔與 `review/interview.md`。
- draft 的所有文字經 `plainText` 檢查（不得含 `<`、`>` 與控制字元，有長度上限）；資料表與欄位名稱只由代碼推導（`T_`／`C_` 加 12 個十六進位字元），SQL 值全部參數化。
- 後端支援展示與正式兩種模式。正式模式以 broker 的 `GET /api/v1/sessions/verify` 核對 scoped token，再依管理員核定的 policy 檔決定操作範圍。

不能直接用的部分：

- **draft 的代碼是亂數**（`starterDraft` 用 `crypto.getRandomValues`）。代理寫不出 12 位不重複的十六進位代碼，也不該由它決定資料表名稱。
- **`validateDraft` 遇到第一個錯誤就丟出一則中文訊息**，沒有 `{code, path, message, hint}`。代理的修正迴圈需要結構化、可一次列出多筆的錯誤。
- **`buildPresalePackage` 寫在工作台的 HTTP 伺服器檔案裡**，generation-worker 無法以子程序呼叫。
- **worker 與 broker 都寫死一種套件**：頂層只准 `site/` 與 `report/`（[ScaffoldGenerateHandler.cs:30](../../packages/csharp/workers/generation-worker/Handlers/ScaffoldGenerateHandler.cs)），zip 名稱固定 `-scaffold.zip`，`GenerationIngestingDispatcher` 只處理 `generate_scaffold`。

## 3. 設計

### 3.1 代理寫的東西：AppSpec

代理不寫 Presale draft，改寫一份只有名稱與型別的 AppSpec；代碼由編譯器決定。

```json
{
  "title": "課務基礎資料",
  "goal": "維護課程、教師與教室。",
  "entities": [
    {
      "key": "course",
      "label": "課程",
      "fields": [
        { "key": "code", "label": "課程代碼", "type": "text", "required": true },
        { "key": "credits", "label": "學分", "type": "number", "required": true }
      ],
      "examples": [{ "code": "CS101", "credits": 3 }]
    }
  ],
  "roles": [
    { "key": "admin", "label": "管理者", "actions": ["create", "read", "update", "delete"], "vertical": "all", "horizontal": "all" }
  ]
}
```

- `key` 符合 `^[a-z][a-z0-9_]{0,31}$`，在各自的範圍內不重複。錯誤的 `path` 與之後的頁面配置都用 `key` 指稱，改顯示名稱不會改變代碼。
- 代碼以 `key` 確定性推導：`E_` 加上 `sha256("presale-app-v1\nentity\n" + entityKey)` 的前 12 個十六進位字元（大寫），欄位是 `F_` 加上 `sha256(...\nfield\n + entityKey + "\n" + fieldKey)`，角色是 `R_`。推導出的代碼相撞時回 `CODE_COLLISION`（48 位元，實務上不會發生，但要擋）。同一份 AppSpec 永遠得到同一組代碼、同一個 zip。
- `examples` 以欄位 `key` 為鍵的物件陣列（draft 是依欄位順序的陣列，由轉換器排列），每個資料區塊至多 5 筆。
- 欄位型別就是 Presale 的 8 種：`text`、`email`、`tel`、`number`、`date`、`textarea`、`toggle`、`select`。上限沿用 Presale：資料區塊 1～4 個、每個 1～12 欄、角色 1～6 個。
- 第一個切片不收頁面配置，一律用 `defaultPagePlan()`（清單、明細、表單三種頁面照常產生，「頁面規劃」畫面是空的）。代理自訂頁面配置見 §6。

新增 `tools/presale-studio/app-spec.mjs`，兩個函式：

- `validateAppSpec(spec, limits)`：分層驗證，回 `{ ok, errors:[{code,path,message,hint}], warnings, entities:[{key,code,field_count}], validation_digest, validator_version }`。錯誤的合併、上限與截斷沿用 [validate-definition.mjs](../../tools/generation/validate-definition.mjs) 的做法（同碼合併、最多 50 筆、鍵長上限 128）。
- `appSpecToDraft(spec)`：轉成 `{ schema_version:1, preset:'blank', confirmed:true, … }` 的 draft。轉換後仍然呼叫原本的 `validateDraft` 當最後一關；它不該再失敗，失敗就是轉換器的錯，回 `INTERNAL_ERROR`。

`confirmed: true` 由轉換器寫入，因為人類的確認發生在 broker 端（使用者對 draft 回 `y`），不在這份 JSON 裡。

### 3.2 編譯器抽出與 CLI

- 把 `buildPresalePackage`、`fileList`、`refreshManifest` 從 `server.mjs` 移到 `tools/presale-studio/build-package.mjs`，`server.mjs` 改為匯入。行為不變，既有的 `test:presale-studio` 要全過。
- 新增 `tools/presale-studio/cli.mjs`，契約與 [tools/generation/cli.mjs](../../tools/generation/cli.mjs) 相同（stdin 一個 JSON、stdout 一行 JSON、結束碼 0／64／65／70、stdin 上限 1 MB、輸出位置只取自 `out_dir`）：

  | 指令 | 輸入 | 輸出 |
  |---|---|---|
  | `validate` | `{ app_spec, limits? }` | `validateAppSpec` 的結果 |
  | `build` | `{ app_spec, limits?, out_dir }` | 先驗證；不通過回 `{ ok:false, errors }` 且不寫檔；通過時把套件寫到 `out_dir` 並回 `{ ok:true, entities, pages, files:[{path,sha256,size}], generator_version, catalog_sha256, validation_digest, functional_validation:{result, case_count} }` |

- `buildPresalePackage` 現在把產物寫到 `%TEMP%/b4a-presale-*`。抽出時加上 `outDir` 參數：有指定就寫到那裡（CLI 用），沒指定維持現狀（工作台用）。
- 功能驗證結果是 `failed` 時現在丟出例外；CLI 要把它轉成 `FUNCTIONAL_VALIDATION_FAILED` 的結構化錯誤並附上失敗的案例名稱。

### 3.3 能力與 grant scope

新增兩個能力，型錄查詢沿用既有的：

| capability | route | 風險／審批 | 每任務配額 | broker 寫入的 scope |
|---|---|---|---|---|
| `generation.catalog.query`（既有） | `query_component_catalog` | Low／auto | 20 | 不變；新增 section `app_spec` |
| `generation.app.validate` | `validate_app_spec` | Low／auto | 6 | `{"routes":["validate_app_spec"],"max_entities":4,"max_fields_per_entity":12,"max_roles":6}` |
| `generation.app.generate` | `generate_app` | Medium／auto_if_task_scope_match | 2 | `{"routes":["generate_app"],"output_slot":"<任務 id>","package_name":"<淨化後的資料夾名>","package":"presale-app-v1","max_entities":4,"max_fields_per_entity":12,"max_roles":6}` |

為什麼不沿用 `generate_scaffold` 再加一個 `package` 值：tool-spec 是能力的唯一來源，一條 route 對應一份 `input_schema`。同一條 route 依 scope 接受兩種形狀的輸入，代理端的工具說明、broker 的 schema 驗證與審批畫面都要分岔；分成兩條 route 之後，代理拿到哪些 grant 就只看得到哪些工具，沒有選錯的機會。

- scope 的三個上限只能比編譯器的上限更嚴（broker 設定 `Generation:App:MaxEntities` 等，預設等於編譯器上限）。worker 把它們當成 `limits` 傳給 CLI。
- 型錄的 `app_spec` section 由 `tools/generation/catalog-summary.mjs` 提供：欄位型別與各自接受的示範資料形狀、`vertical`／`horizontal` 的列舉值與意義、上限、一份完整範例。內容從 `tools/presale-studio/model.mjs` 的常數產生，不另外手寫一份。

### 3.4 generation-worker

- `GeneratorCli` 目前固定執行 `tools/generation/cli.mjs`。改成依呼叫端指定兩個固定腳本之一（`CliRelativePath` 變成兩個常數），腳本路徑仍然不可由請求決定。啟動檢查要求兩個腳本都在 `Generation:ToolsRoot` 之下才註冊對應的能力：缺 Presale CLI 時只註冊第一刀的三個能力。
- 把 `ScaffoldGenerateHandler.GenerateAsync` 中與套件型別無關的部分抽成共用的建置流程：輸出位置、連結檢查、同一請求 id 的結果重用、檔案清單核對、本機路徑檢查、決定性 zip、`result.json`。差異以參數表示：

  | | `definition-site-v1` | `presale-app-v1` |
  |---|---|---|
  | CLI | generation `build` | presale `build` |
  | 頂層 | 目錄 `site`、`report` | 目錄 `site`、`backend`、`review`、`report`，檔案 `package.json`、`README.txt` |
  | zip 名稱 | `{package_name}-scaffold.zip` | `{package_name}-app.zip` |
  | 額外檢查 | 無 | 見下 |

- `presale-app-v1` 的額外檢查：`backend/` 之下每個檔案，以及 `package.json`、`README.txt`，必須與 worker 自己的 `tools/presale-studio/` 來源逐位元組相同（worker 啟動時算好 sha256 表）。這把「代理的輸入不會變成伺服器端程式碼」從編譯器的實作慣例變成 worker 強制的條件：使用者會對這個 zip 執行 `npm start`。
- 新增 `AppSpecValidateHandler` 與 `AppGenerateHandler`，在 `Program.cs` 註冊。`generate_app` 與 `generate_scaffold` 共用同一把生成鎖（worker 一次只建置一件）。
- 結果多帶 `package`、`entities`（key、代碼、欄位數）與 `functional_validation`。
- Node 版本：`node:sqlite` 需要 22.13 以上。worker 啟動時執行一次 `node --version`，不足時不註冊兩個 app 能力並記錄原因。

### 3.5 broker

- `GovernedGenerationOptions`：新增設定 `HighLevelCoordinator:Generation:ScaffoldPackage`，值為 `definition-site-v1`（預設）或 `presale-app-v1`；其他值視為設定錯誤，由就緒檢查拒絕。常數加上兩個新能力與 route。
- `GovernedGenerationLauncher`：依設定寫入對應的三個 grant。工作項的上限區塊改列資料區塊、欄位與角色的上限。
- `GovernedGenerationReadiness`：選 `presale-app-v1` 時，檢查的是 catalog 加兩個 app 能力是否已載入並有 worker 註冊。worker 因 Node 版本或缺檔而沒註冊時，這裡自然 fail-closed。
- `GenerationIngestingDispatcher`：把「只處理 `generate_scaffold`」改成 route 對照表（route → 套件名、zip 字尾）。其餘檢查（slot、request id、相對路徑形狀、連結、大小與 sha256、複製後再算一次）不變。證據文件多記 `package`、資料區塊數與功能驗證結果。
- `GenerationDeliveryService`：交付檔名 `{資料夾}-app.zip`。通知文字照實說明產物：含 Node.js 後端與 SQLite；解壓後執行 `npm start`，開啟 `http://127.0.0.1:3092/`；需要 Node.js 22.13 以上；測試角色可任意切換，不是正式登入。
- draft 預覽（使用者回 `y` 之前看到的說明）同樣依套件型別改寫，不能在交付有後端的套件時仍說「不含後端」。

### 3.6 代理

- `tools/agent/lib/tool-registry.js`：加入 `validate_app_spec`、`generate_app` 兩個工具與能力對照。
- 生成用 system prompt 依拿到的 grant 分成兩種工作流程：有 `generation.app.generate` 時是 `query_component_catalog(section: app_spec)` → `validate_app_spec` → `generate_app`。最終回覆沿用 `governed-response.js` 的固定 JSON。
- 參數修補（字串形式的物件、缺少閉合符號）對兩個新工具同樣生效。

## 4. 邊界與不解決的事

這一刀交付的是「解壓後能跑、資料會留下來的 CRUD 應用」，以下都不在範圍內，交付文字與 draft 預覽要照實寫：

- **沒有託管**。使用者拿到 zip，自己在有 Node.js 22.13 的機器上執行。broker 不替它啟動程序、不開埠。
- **沒有正式登入**。預設是展示模式，任何人都能切換測試角色。套件帶有正式模式的程式，但一般人員的 token 簽發與權限對照還沒有核定（套件的 README 也這麼寫），所以第一個切片不宣稱可用於正式環境。
- **資料區塊之間沒有關聯**。Presale 的模型沒有外鍵或參照欄位；「課程的授課教師」只能是文字欄位。這是模型的限制，不是接法的限制，要另外設計。
- **只有一種樣式**：`crud_rbac_v1`，8 種欄位型別，至多 4 個資料區塊、每個 12 欄。超出的需求代理只能在摘要中說明沒有涵蓋。
- **重新生成不搬資料**。同一個需求改了欄位再生成，得到的是新的 zip 與空的資料庫；欄位的 `key` 不變時代碼也不變，資料表名稱相同，但沒有搬移工具。
- **功能驗證不是驗收**。編譯器內建的驗證只確認角色範圍規則與 SQLite 往返正確，不代表畫面符合使用者的需求。

## 5. 實作順序

每一步都能單獨合併與測試，前一步不依賴後一步。

1. **抽出編譯器**：`build-package.mjs`，加 `outDir` 參數；`server.mjs` 改為匯入。新增「同一份 draft 建置兩次逐檔相同」的測試。
2. **AppSpec**：`app-spec.mjs` 與測試（每個錯誤代碼各一例、代碼推導的固定向量、上限、轉換後 `validateDraft` 通過、示範資料的型別檢查）。
3. **Presale CLI**：`cli.mjs` 與結束碼契約測試（比照 `tools/generation/cli.test.mjs`）；`package.json` 的 `test:generation` 或 `test:presale-studio` 納入。
4. **型錄 section**：`app_spec`，測試確認內容與 `model.mjs` 的常數一致。
5. **tool-spec 與 worker**：兩份 `tool.json`、共用建置流程的抽取（第一刀的 `GenerationHandlerTests`、`GenerationCliContractTests` 要原樣通過）、兩個新 handler、`backend/` 位元組比對、Node 版本檢查。
6. **broker**：設定、launcher、就緒檢查、ingest 對照表、交付文字、draft 預覽；對應的單元測試各加一組 `presale-app-v1` 案例。
7. **代理**：工具、提示、`validate:agent-governed` 加一組以 mock 模型走完 app 流程的案例。
8. **端到端**：整合測試比照既有的程序內案例（真正的 handler 與 CLI：catalog → validate 失敗一次 → 修正 → generate → broker 收下 → 交付）；再加一個瀏覽器 smoke：解壓 broker 收下的 zip、啟動後端、新增一筆、重啟後端、讀回同一筆。
9. **文件**：本文件狀態改為已實作，`GovernedGeneration.md` §13 的「第二刀的後端生成」指向這裡，`CLAUDE.md` 的測試產物表補上新的暫存目錄。

部署要跟著改的地方：generation-worker 的 `Generation:ToolsRoot` 子集要多帶 `tools/presale-studio/` 與 `packages/javascript/browser/custom_components/`（sidecar 的發布腳本與 compose 的映像都要改）；代理映像要重建才有兩個新工具。

## 6. 之後

- **代理自訂頁面配置**：`generate_app` 加上選用的 `page_plan`，以 `key` 指稱資料區塊與欄位，轉換器換成代碼後交給既有的 `validatePagePlan`。型錄再加一個 section，內容就是工作台現有的代理清單（[server.mjs 的 `agent-palette`](../../tools/presale-studio/server.mjs)：候選複合元件、內建元件白名單、事件與流程的 schema）。
- **資料區塊之間的參照**：在 Presale 模型加入參照欄位型別（指向另一個資料區塊的某一筆），後端以 `lookup` 提供選項。這會動到 `model.mjs`、`sqlite-backend.mjs` 與三種頁面的欄位渲染。
- **正式模式可用**：一般人員的 token 簽發與 policy 核定流程定案後，交付時由 broker 一併產生 policy 檔的草稿交管理員核定。
- **託管**：由 execution-adapter 或另一個 worker 在受控環境啟動交付的套件，回給使用者一個連結。
- **另一種後端**：SPA 範本的 .NET 10 後端（`templates/spa/backend`，含需自行啟用的 JsonCrud 核心）也可以成為第三種套件型別。它需要 worker 具備 .NET 建置工具鏈，信任面比固定的 Node 檔案大，所以排在 Presale 之後。

## 7. 決策

| # | 決策 | 採用 | 沒採用的做法與原因 |
|---|---|---|---|
| A1 | 後端從哪裡來 | Presale 編譯器的 Node.js／SQLite 後端 | 重寫一個：Presale 的後端已有範圍規則、查詢、匯出、批次與測試 |
| A2 | 代理寫什麼 | 只有名稱與型別的 AppSpec，代碼由編譯器推導 | 讓代理寫帶代碼的 draft：模型寫不出不重複的十六進位代碼，資料表名稱也不該由它決定 |
| A3 | 能力怎麼切 | 兩個新能力、兩條新 route，型錄共用 | 在 `generate_scaffold` 上加 `package` 值：一條 route 兩種輸入形狀 |
| A4 | 編譯器在哪裡跑 | generation-worker 以子程序呼叫 Presale CLI | 讓代理呼叫工作台的 HTTP 端點：工作台沒有身分、配額與稽核，而且只綁本機 |
| A5 | 誰保證後端程式沒被動過 | worker 逐位元組比對 `backend/` 與自己的來源 | 只靠編譯器：使用者會執行這些檔案，要有獨立的一關 |
| A6 | 模式開關 | broker 設定決定整個部署用哪一種套件 | 讓代理自己選：選擇會影響交付內容與使用者看到的說明，應由 broker 在確認 draft 之前決定。之後可改為依 draft 的需求逐案決定 |
| A7 | 頁面配置 | 第一個切片不收，用預設 | 一開始就開放：代碼對照與型錄 section 要先有 |
