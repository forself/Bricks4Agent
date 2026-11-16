# Presale 雛型工作台

執行 `npm run presale:studio`，在本機瀏覽器開啟 `http://127.0.0.1:3091/`。

工作流程：選擇課務、申請案件或空白起點；調整名稱、資料區塊、欄位、展示資料與角色範圍；核對後勾選確認。工作台依各資料區塊產生查詢列、資料摘要、編輯欄位組三種 B4A JSON 複合元件候選；它們與標題、文字、文字／數值／日期／多行輸入、開關、選單、按鈕、資料表及分隔線等 B4A 基本元件同列於「可選元件」。可拖到頁面畫布，或按「加入」；畫布支援調整順序、半寬／全寬及移除，並立即用 B4A 元件渲染器預覽。每個新加入的複合元件實例可設定內部節點的欄位、查詢條件、事件、後端行為、資料來源與結果節點；查詢結果欄位也可選擇。獨立元件可透過畫布下方的資料流程連成查詢、摘要、選項查詢及展示資料的新增、修改、刪除。B4A 獨立按鈕仍支援既有的查詢、新增、儲存動作。修改訪談內容後需重新核對；仍存在的資料區塊及其元件位置會保留，已移除區塊的元件才會失效，不相符的互動設定會還原成預設值或移除該流程。

工作進度在訪談資料有效時自動保存於本機瀏覽器，重新整理可接續；也能下載 `b4a-presale-workspace.json` 並在另一個工作檯重新開啟。工作檔含訪談名稱與展示資料，供人員保存，不送給代理。無效的輸入會保留上一份有效的自動保存快照；下載與開啟都以版本、欄位、代碼及頁面連結白名單驗證。這是單份本機進度，尚無多人協作或版本歷程。

### 將元件與頁面加入本地庫

確認訪談後，在第 06 步的「我的本地庫」選擇畫布中的元件，填寫名稱並按「存入元件庫」；也可按「存入頁面庫」保存整份畫布及資料流程。已保存的元件會出現在左側可拖拉清單，頁面列在本地庫清單。選用時先將來源資料區塊和欄位對應到目前訪談，通過相同的頁面配置驗證後才會加入畫布或取代目前頁面；取代前會顯示確認。元件若有跨元件的事件連結，須保存整個頁面。庫內不保存展示資料、角色或自由指令。

本地庫預設保存在工作台的 `tools/presale-studio/.local-library/`，重新啟動工作台仍可使用；它是此工作台的本機 JSON 模板庫，與原始碼中的 `packages/javascript/browser/custom_components/registry.json` 分開。每次保存會建立新的不可變項目，清單可個別移除。資料區塊有複合元件時，目標資料區塊須有相同數量且依序相同型別的欄位；其他頁面流程可以對應到欄位較多的區塊。這個限制會在選擇與套用時檢查，避免錯誤綁定。代理清單中的 `saved_templates` 僅含模板代碼、來源區塊與欄位代碼及型別，不含人類名稱或展示文字；代理若要套用，仍須提交完整欄位映射並通過同一驗證器。目前不提供跨電腦同步，也不會把本地庫自動發布到核心 B4A 元件套件。

工作台也提供「匯出本地庫」下載版本化 JSON bundle，以及「匯入本地庫」選取檔案。匯入只接受目前版本與精確 schema，最多 200 項、整包 8 MB、單項 256 KB（以 9 個元件的課程頁面為例，每項約 17 KB，200 項約 3.2 MB）；本地庫超過 8 MB 時匯出會直接回報，不會產生匯不回來的檔案；逐筆驗證 SHA-256、record 與 page-plan 白名單後才會寫入。任何未知欄位、無效摘要、重複 ID、容量超限或既有 ID 衝突都會拒絕整批；衝突回覆 409，不會改寫既有項目或引用。匯出只含已保存的結構模板與欄位映射來源描述，不含訪談展示資料、角色或程式碼；不會執行匯入檔內容。

按「產生雛型與交接文件」後，可瀏覽資料畫面及獨立的「頁面規劃」畫面，或下載 ZIP 交給團隊。課程欄位參考 CourseAffairs.Web 現有畫面，教師與教室為展示擴充，並非宣稱已實作完整課務需求。ZIP 內有 `site/`、原生 Node.js 後端 `backend/`、`package.json`、`review/interview.md`、`review/architecture.agent.json`、`review/labels.json`、`review/field-mapping.json`、`review/page-plan.json`、`review/custom-components/`、`report/manifest.json` 與 `report/functional-validation.json`。下載後解壓，在套件根目錄執行 `npm start`，再開啟 `http://127.0.0.1:3092/`；需 Node.js 22.13 以上版本。`node:sqlite` 在此版本仍會顯示實驗功能提示。SQLite 資料建立在套件的 `data/prototype.sqlite`，重啟服務後保留；不需要安裝執行期第三方套件。元件候選遵守 B4A 複合元件 JSON 格式，並通過既有定義驗證器；頁面配置只接受候選名單或固定 B4A 元件白名單，最多 24 個元件。

`architecture.agent.json` 只含不透明代碼及白名單型別、操作與範圍；使用者文字僅保留在經人工確認的展示定義、名稱對照及複合元件中。此版本由結構化訪談表單確定性產生候選，未接入對話式 AI 訪談；不呼叫模型，也不會將訪談文字交給代理。配置頁中的查詢列可依指定欄位搜尋，編輯欄位組可選取、新增或修改記錄；它和清單、明細、表單頁共用 SQLite 資料。配置頁的「重設展示資料」會恢復原始樣本。後端以 `review/field-mapping.json` 的白名單代碼建立每個資料區塊的表，啟動時用 SQLite 欄位反射核對映射；JSON 欄位名稱及型別由共用規則驗證，SQL 值均參數化。測試角色的垂直及水平範圍在每次後端查詢與異動時與使用者篩選條件相交。預設主體與展示資料的 owner／unit／function 都是虛構值，與 CourseAffairs 真實組織資料無關。後端以 HttpOnly 工作階段記住測試角色，但任何使用者都能切換角色，所以這不是正式身分驗證。正式 IAM 與正式資料庫接入仍須另外設計。

`page-plan.json` 只接受白名單元件、版面、欄位代碼與互動物件，可作為人類與受控代理共用的結構化頁面配置格式。工作台建立雛型後會回傳 `agent_palette_url` 與 `agent_plan_url`：前者提供代碼化架構、候選元件的內部節點能力、獨立元件的事件／輸入型別／結果接口及固定流程規則，後者只接受與工作檯相同的嚴格 `page-plan.json` 物件，並產生新的雛型。這是受控代理的 JSON 介面，尚未接入模型或 broker 的代理會話；通用代理文字回覆亦尚未成為這個介面的輸入。`report/functional-validation.json` 檢查全範圍查詢、受限查詢與篩選交集、四種讀取模組的範圍一致性、批次新增預檢不寫入、可修改角色的資料保存，以及唯讀角色的修改拒絕。若訪談缺少必要角色或資料，報告為 `incomplete`，不應宣稱權限驗證完成。

### 複合元件的節點互動

複合元件本身是含巢狀 `root.children[]` 的 JSON 物件。每次拖入畫布會得到固定 `instance_id`；實例的 `wiring.fields` 以複合元件內部 `node_id` 對應已確認的 `F_` 欄位，查詢列另有 `operator` 和 `wiring.columns`。`wiring.actions` 指定觸發節點及事件、同區塊的資料來源實例、後端操作，以及回應送往的資料表／文字／選單節點。`@page` 的 `load` 是平台提供的頁面載入事件。陣列順序只決定版面與顯示順序，跨節點引用不使用陣列索引。

頁面執行器從已驗證的元件定義遞迴建立實例，再依 `wiring.actions` 掛載事件監聽器；它只呼叫固定 API 操作並將回應交給元件適配器，不執行 JSON 中的程式碼。查詢、摘要、選項查詢可由載入、按鈕或支援的值變更事件觸發；新增、儲存、刪除與匯出只可由按鈕點擊觸發。

獨立元件使用 `page-plan.json` 的 `flows[]`。讀取流程指定資料區塊代碼、觸發元件 `instance_id` 與事件（或 `@page/load`）、`query`／`summary`／`lookup`、最多六個 `{input_instance_id, field_code, operator}` 條件，以及結果元件的 `instance_id`。`query` 只能送往 `DataTable`，`summary` 送往 `Text`，`lookup` 送往 `Dropdown`；資料表欄位及摘要計算只能使用同區塊已確認的欄位。開關條件在使用者第一次切換前視為未指定，切換後才會送出 `true` 或 `false`。

寫入流程使用 `create`／`update`／`delete`，每條都要有未配置其他動作的獨立按鈕，以 `click` 觸發；新增與修改將獨立輸入元件對應到同區塊欄位代碼，新增必須涵蓋所有必填欄位。修改與刪除還須指定同區塊查詢流程的獨立資料表，並由使用者先在該表選取資料列；不能在頁面配置中填入任意記錄 ID。後端每次操作仍驗證目前模式的角色權限與資料範圍，寫入後重新查詢資料表、摘要與選項。展示模式依展示角色規則執行；正式模式只依 Broker 核驗主體與管理員核定的正式 policy 決定操作，頁面配置不會自行授與權限。所有流程合計最多八條，同一輸入元件不能跨流程對應不同欄位；尚未替 134 個 B4A 元件全部定義資料接口。`npm run test:presale-studio:browser` 會用 Edge 驗證展示模式的工作台設定、生成畫面、複合元件互動、獨立元件讀寫流程及儲存；`npm run test:presale-studio:formal-browser` 以 mock Broker 與隔離 SQLite 檢查正式模式讀寫、唯讀拒絕、撤銷工作階段拒絕及重啟讀回，不連正式資料庫。

服務僅綁定 `127.0.0.1`。伺服器關閉時會刪除本次建立的暫存雛型；需留存請下載 ZIP。測試：`npm run test:presale-studio`；瀏覽器含本地庫儲存、下載與 JSON 往返：`node tools/presale-studio/library-browser-smoke.mjs`。

Node.js 內建的 `node:http` 只提供 HTTP 伺服器。共用的 `native-router.mjs` 提供宣告式路由表及守衛，在兩個服務入口檢查本機 Host、重複敏感標頭、路徑編碼與遍歷、查詢鍵的白名單／型別／範圍、同源來源、JSON 媒體型別／容量／深度／重複鍵、每位址請求速率上限及安全回應標頭。`prototype-router.mjs` 宣告雛型 API 的方法、路徑、查詢與請求本文政策；`prototype-api.mjs` 處理請求與回應，工作台的生成、下載及靜態檔也在 `server.mjs` 以相同路由表宣告。沒有使用 Express 等第三方路由套件。這些入口檢查不能取代正式身分驗證、使用者授權或部署環境的網路防護。

## 常用後端模組

每個資料區塊的 `/api/presale/e<12 位代碼>` 除單筆 CRUD 外，提供下表的 POST 端點。JSON 本文只接受列出的屬性；欄位須使用 `review/field-mapping.json` 中已確認的 `F_` 代碼。`review/architecture.agent.json` 的 `backend_modules` 提供代理可讀的模組清單。這些模組參考 Tim2026 的常見功能類型，沒有複製其資料、權限或資料表規則。

| 路徑 | 權限 | JSON 本文 | 結果與限制 |
|---|---|---|---|
| `/query` | read | `filters`、`sort`、`page`、`page_size` | 分頁資料與總數；每頁最多 100 筆 |
| `/lookup` | read | `field`、`filters`、`limit` | 去重選項及筆數；最多 100 個選項 |
| `/summary` | read | `metric`、`group_by`、`filters` | `count` 或數值欄位 `sum`；最多 100 組 |
| `/export` | read | `fields`、`filters`、`sort` | CSV；最多 20 欄、2000 筆，試算表公式前綴會中和 |
| `/batch` | create | `request_id`、`items`、`dry_run` | 一批 1 至 100 筆；整批驗證、同一交易寫入、重送去重 |

`filters` 是最多 12 個 `{ "field": "F_...", "op": "eq", "value": 值 }` 的陣列；比較方式限 `eq`、文字 `contains`、數值或日期 `gte`／`lte`。`sort` 為 `{ "field": "id 或 F_...", "direction": "asc 或 desc" }`。查詢條件一律與伺服器端角色範圍取交集，查詢、選項、摘要與 CSV 不會因為換端點而看到額外資料。`/batch` 的 `request_id` 是 32 字元小寫十六進位碼；`dry_run: true` 只驗證、不寫入。正式寫入後相同代碼與內容重送會回傳原始結果，不同內容會回覆 409。每筆資料的 owner、unit、function 都取自已驗證主體，JSON 無法指定。

此階段的模組覆蓋常見資料查詢、摘要、匯出與批次新增。Tim2026 的附件、通知、簽核、組織及代碼主檔帶有不同的保存與授權規則；目前沒有將其假設成通用寫入模組。

## Broker 驗證的正式模式

下載套件可用 `B4A_PROTOTYPE_MODE=formal` 啟動獨立的 `data/formal.sqlite`。啟動前須指定 `B4A_BROKER_VERIFY_URL=http://127.0.0.1:<Broker 埠>/api/v1/sessions/verify` 和 `B4A_FORMAL_POLICY`（管理員核定的 JSON 權限檔絕對路徑）；任一缺少就拒絕啟動。正式模式不載入展示資料，也不開放測試角色切換與資料重設。Broker 的 `/api/v1/sessions/verify` 只回覆經現有中介軟體檢查簽章、期限、epoch、撤銷和工作階段綁定的主體、任務、角色、工作階段代碼。Node 只連本機 Broker 的固定唯讀路徑，並在每次受保護操作重新驗證；Bearer 權杖可供代理直接呼叫 API，瀏覽器則可交換成 HttpOnly 工作階段。

正式權限檔的頂層為 `{ "schema_version": 1, "principals": [...] }`。每筆核定綁定必須完整指定 `principal_id`、`task_id`、`role_id`、`subject` 和 `roles`；`subject` 僅接受 `owner`、`unit`、`function`、`descendants`，每個角色僅接受 `code`、`label`、`actions`、`vertical`、`horizontal`。其中 `code` 是 `R_` 加 12 位大寫十六進位碼，操作限 `create/read/update/delete`，垂直及水平條件與工作檯訪談選項相同。Broker 核驗所得的三個代碼必須同時與核定綁定一致；訪談角色、展示主體與 token 的一般 `scope` 不會自動轉成正式授權。多個核定角色的適用範圍取聯集，再與使用者篩選條件取交集；單筆讀取、修改與刪除均套用相同的 SQL 範圍守衛。新增資料的 owner、unit、function 由伺服器端核定主體決定，不接受前端覆寫。

這個模式目前支援 Broker 的代理 scoped token。Broker Portal 一般使用者沒有對應的正式權杖簽發和核定主體／角色對照；管理員也尚須核定正式權限檔和正式資料欄位定義。因此它提供可用的正式授權執行路徑，並未宣稱已可直接取代 CourseAffairs 正式系統或供一般業務人員直接上線。正式 SQLite 仍使用 Node 22 的實驗性 `node:sqlite`，部署前需另做備份、監控與容量驗證。
