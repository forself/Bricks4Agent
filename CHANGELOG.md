# 變更紀錄

本檔記錄 Bricks4Agent（B4A）對使用者可見的變更，依相容性分類：

- **新增**：新的選項、方法或匯出，預設不改變既有行為。
- **預設行為變更**：不改呼叫方式，但預設畫面或互動與前一版不同。
- **修正**：缺陷修正；若修正會改變可觀察行為，會在條目中寫明。

B4A 只收通用元件與通用能力；任何業務系統的專屬元件都不寫入 B4A。

## 未發行

### 新增：QR Code 與條碼產生器（2026-10-03）

元件數由 132 增為 134。兩個元件在 catalog 中標為 `beta`、`manual_only`；字串由元件資料夾內的 `locale.js` 註冊 zh-TW 與 en。

**新增**

- `QrCode`：QR Code（ISO/IEC 18004），版本 1～40、錯誤修正 L/M/Q/H，自動選數字、英數或位元組（UTF-8）模式，八種遮罩依罰分選最小；可選 UTF-8 的 ECI 宣告。編碼器 `encodeQr` 可單獨匯入。
- `Barcode`：一維條碼 Code 128（自動切換 A/B/C 字集）、Code 39（可附 mod 43 檢查字元，台灣超商代收與郵局劃撥的繳費單即此格式）、EAN-13、EAN-8（自動補或驗證檢查碼，護線延伸、數字分組）。編碼器 `encodeBarcode` 可單獨匯入。
- 兩者都是純 Canvas、零依賴；模組與條邊對齊整數像素，`exportPNG(scale)` 輸出高倍率圖檔供列印；顏色取主題 token，深色主題下自動對調，模組或條永遠比底色暗；內容不合格式時在畫面上顯示訊息，`getError()` 取得原因。

驗證入口：`node --test packages/javascript/browser/ui_components/viz/QrCode/qr-encoder.test.mjs packages/javascript/browser/ui_components/viz/Barcode/barcode-encoder.test.mjs`（含標準已知數值與獨立解碼器的往返比對）。

### 修正：SPA 範本清理器邊界（2026-09-29）

- 保留 f905b2f 的「先清洗子節點再拆殼」修正；追加瀏覽器無 CSP 回歸，直接驗證清理器及 WebTextEditor 儲存／載入。
- SPA 範本的 URL 檢查先移除控制字元，阻擋被 Tab／換行切開的危險協定、協定相對 URL 與非白名單協定。
- 範本 HTML 改用屬性白名單，不再保留 inline style、srcset、ping、contenteditable 等主動屬性；圖片 data URL 僅保留點陣格式。無 DOMParser 時跳脫文字，不回傳原始 HTML。
- 一般格式文字、表格與本機相對連結保留；這是安全性收斂，依賴上述移除屬性的消費端需調整。TIM 使用元件庫清理器與專案防護包裝，不使用 SPA 範本清理器。

驗證入口：`npm --prefix packages/javascript/browser run test:vitest -- __tests__/security/SanitizeHtml.test.js`、`npm run test:sanitizer:browser`（需測試用 Playwright 與 Edge）。

### 新增：16 個通用元件與兩個工具（2026-09-27）

新元件在 catalog 中標為 `beta`、`manual_only`，以手動 `new` 使用；字串由各元件資料夾內的 `locale.js` 自行註冊，提供 zh-TW 與 en。元件數由 116 增為 132。

**新增**

- 排程與資料：`TimeGrid`（時段格線，項目跨格、重疊並排、點選與拖放、鍵盤搬移）、`DataGrid`（試算表式資料格，逐格驗證、方向鍵導覽、貼上 TSV、未存標記與 `getChanges()`；`EditableTable` 不變）。
- 輸入：`RemoteSelect`（遠端查找）、`DateRangePicker`、`TimeRangePicker`、`DateTimeRangePicker`（區間輸入）、`Transfer`（穿梭框）、`ConditionBuilder`（條件編輯器，輸出純資料）。以上都實作欄位錯誤契約 `setError` / `clearError`。
- 流程與回饋：`ImportWizard`（CSV/TSV 匯入精靈）、`IssueList`（問題清單）、`ApprovalTimeline`（審核歷程）、`NotificationCenter`（通知中心）、`Countdown`（倒數計時）、`ConflictNotice`（版本衝突提示）。
- 版面：`Popover`（可放按鈕與連結的浮層）、`PrintLayout`（只列印指定區塊）。
- 工具：`createPermissionGate`／`PermissionGate` 依權限隱藏或停用元素與元件，只改善介面，權限仍須由伺服器把關；`createDirtyGuard`／`DirtyGuard` 追蹤未存變更並在離開前確認。由 `utils/index.js` 匯出。

**已知限制**

- `DateRangePicker`、`TimeRangePicker`、`DateTimeRangePicker` 從外部替內部的 DatePicker、TimePicker 補上鍵盤與 ARIA，依賴它們的 DOM 結構；日後應內建到 DatePicker、TimePicker 本身。
- `PrintLayout` 的紙張大小與邊界需要 Chromium 或 Firefox 110 以上；頁首頁尾只印一次。

### 補強：既有元件的選配能力與無障礙（2026-09-27）

**新增**

- DataTable：
  - 伺服器端模式 `serverSide` + `dataSource(query)`，排序、分頁、快速篩選交給伺服器，過期回應會被丟棄；另有 `searchDebounce`、`onQueryChange`、`reload()`、`getQuery()`、`setData(rows, total)`。
  - 固定表頭 `stickyHeader` + `maxHeight`；欄位可設 `sticky: 'left' | 'right'`。
  - 欄位切換 `columnToggle`，方法 `setColumnVisible()`、`getColumnVisibility()`，回呼 `onColumnVisibilityChange`；欄位可設 `hideable: false`。
  - 列展開 `expandable`，方法 `expandRow()`、`collapseRow()`、`toggleRow()`、`getExpandedKeys()`。
  - 跨頁勾選：設 `rowKey` 後依鍵值追蹤勾選，新增 `getSelectedKeys()`、`setSelectedKeys()`、`clearSelection()` 與 `selectAllScope`。
- TreeList：勾選多選 `checkable`（含半選、`checkStrictly`、`checkedKeys`、`onCheck`、`getCheckedKeys()`、`setCheckedKeys()`、`checkAll()`、`uncheckAll()`）與延遲載入 `loadChildren`（含 `reloadNode()`）。
- WorkflowPanel：`stages`、`replaceStages`、`fieldMap` 與 `WorkflowPanel.DEFAULT_FIELD_MAP`。內建 13 個階段只為相容保留，新專案請傳入自己的階段。
- Progress：分段堆疊 `segments`、`showLegend`、`setSegments()`，另有 `mount()` 別名。
- Canvas 圖表（14 種）：`accessibleTable` 產生輔助科技可讀的資料表（`true` 視覺隱藏、`'visible'` 顯示在圖下），`accessibleTableMaxRows` 限制列數，子類以 `getDataTable()` 提供內容。說明見 `viz/ACCESSIBILITY.md`。
- ModalPanel：`ModalPanel.defaults.manageFocus` 與 `manageFocus`、`initialFocus`、`ariaLabel` 選項。開啟焦點管理後，開啟時焦點移入、Tab 限制在對話框內、關閉時還原焦點。預設關閉，新專案建議在啟動時開啟。

**預設行為變更**

- ModalPanel 帶 `role="dialog"`、`aria-modal="true"`，以標題命名；關閉鈕的 `aria-label` 改走語系（zh-TW 仍為「關閉」）。疊加多層時一次 Escape 只關最上層；內部元件已處理（`preventDefault`）的 Escape 不再連帶關閉對話框。
- Dropdown 選單展開時按 Escape 只關閉選單，不再同時關閉外層對話框；MultiSelectDropdown 選單已收合時不再攔下 Escape，外層對話框可正常關閉。
- TreeList 預設帶樹狀結構的 ARIA 角色與鍵盤操作（方向鍵、Home、End、Enter、Space），每棵樹成為一個 Tab 停駐點。
- DataTable 的 `rowKey` 原本不起作用，現在會改為依鍵值追蹤勾選。只影響可勾選的表格；Tim2026 傳入 `rowKey` 的表格都不可勾選，行為不變。
- WorkflowPanel 下一階段的「(待處理)」提示改走語系，英文介面顯示「(To do)」。
- Progress 圓形模式掛載後立即以主題色重繪，不再先以灰色顯示。
- Dropdown、MultiSelectDropdown 的輸入框文字改用主題文字色 `var(--cl-text)`。
- BasicButton 停用時不再顯示滑過效果。

**修正**

- BasicButton 以 `disabled: true` 建立後，`setDisabled(false)` 仍無法點擊；現在事件一律綁定，執行時才檢查停用狀態。
- Dropdown、MultiSelectDropdown 的輸入框在深色主題下是黑字、難以閱讀。
- InfoPanel、FunctionMenu、WorkflowPanel 的 CommonJS 相容段在唯讀的 ESM 環境（例如 Vitest）會在載入時拋錯。
- Progress 在 `destroy()` 後呼叫 `setValue`、`setVariant` 會拋錯；TreeList 在 `destroy()` 後呼叫 `setData` 會重新繪製並殘留圖示訂閱。
- Metadata 工具：`build-metadata.mjs --check` 改為唯讀，原本會刪除沒有註冊的 manifest；方法擷取不再把 `if`、`for` 等控制敘述當成方法，也不再漏掉預設參數含括號的方法；文件路徑優先選與元件同名的 README。

**相容性驗證**

- B4A：`npm test`（Vitest 61 個檔案、1102 項）、`audit-csp`、`audit-ui-style-rules`、`validate-ui-library`、`build-metadata --check`、`test-audit-csp-hard-zero`、`studio-self-host-audit` 全部通過；`validate:ui-state` 只剩 main 既有的 Badge 一項失敗。
- Tim2026：以包含本節與上一節全部變更的版本取代其內嵌副本，執行 Tim2026 的 2846 個單元與安全測試，結果與內嵌副本基準完全相同，沒有退步。

### 補強：輸入元件的欄位錯誤標示（2026-09-27）

**新增**

- 所有輸入元件都有 `setError(message, { display })` 與 `clearError()`，錯誤外觀一致：控制項紅框、`aria-invalid="true"`，錯誤文字以 `role="alert"` 顯示並用 `aria-describedby` 連回控制項。`display: false` 只標示錯誤狀態、不顯示文字，給自行顯示錯誤文字的外層使用。
  - 新增此方法的元件：Checkbox、ToggleSwitch、ColorPicker、Rating、Slider、TextArea、NumberInput、Dropdown、MultiSelectDropdown、DatePicker、TimePicker、DateTimeInput、TagInput、BatchUploader、CommandComposer、ChainedInput、ListInput，以及 `Checkbox.createGroup`、`Radio.createGroup` 回傳的群組。
  - Checkbox、ToggleSwitch、ColorPicker、Rating 的錯誤文字插在元件正後方，元件需已掛載；ChainedInput 只在整組下方顯示文字，不替個別欄位畫紅框。
- TextInput 的 `setError` 新增第二個參數 `{ display }`，預設行為不變。
- FormField 新增 `markControl` 選項，預設 `false`。設為 `true` 時，`setError` 也會標示內部元件的錯誤狀態。
- SearchForm 新增 `markInvalidFields` 選項，預設 `false`。設為 `true` 時，必填驗證失敗會標示欄位元件的錯誤狀態，錯誤文字仍只顯示一次。
- `utils/field-error.js` 提供 `setFieldError`、`clearFieldError`、`getFieldError`、`hasFieldError` 與 `FIELD_ERROR_CONTRACT`，也由 `utils/index.js` 匯出，自訂元件可直接沿用同一套外觀與無障礙標示。
- `Checkbox.createGroup` 回傳的群組新增 `destroy()`。

**修正**

- NumberInput：使用者直接輸入後按 Enter 或離開欄位，數值確實改變時會觸發 `onChange` 一次。原本只有 +/- 按鈕、方向鍵與 `clear()` 會觸發，直接輸入的數值不會通知呼叫端。程式呼叫的 `setValue()` 仍預設不觸發。
- TextInput 處於錯誤狀態時，輸入框會加上 `aria-invalid="true"`，讓螢幕報讀器能辨識錯誤欄位；畫面不變。
- SearchForm 銷毀時會一併銷毀 Checkbox 群組，釋放勾選圖示。

**相容性**

- SearchForm 預設只呼叫原本就有 `setError` 的元件，畫面與前一版相同。Tim2026 的查詢表單使用 TimePicker、Checkbox 群組與 Radio 群組，這些元件新增了 `setError`，但在預設設定下不會被標示。
- FormField 預設不轉呼叫內部元件，既有以 FormField 顯示錯誤的畫面不變。

**相容性驗證**

- B4A：`npm test`、`audit-csp`、`audit-ui-style-rules`、`validate-ui-library`、`build-metadata --check` 全部通過。
- `validate:ui-state`：測試用假 DOM 補上元件實際使用的標準 DOM API 後，除了 Badge 一項以外全部通過。Badge 一項在 main 上就已失敗，原因是假 DOM 不會把 `style.cssText` 反映到個別樣式屬性。上一版寫回後，Dropdown、TimePicker、MultiSelectDropdown、ChainedInput、PhoneListInput 五項曾因假 DOM 缺少 API 而失敗，已一併恢復。
- Tim2026：以本版本取代其內嵌副本，執行 Tim2026 的 2846 個單元與安全測試，結果與內嵌副本基準完全相同，沒有退步。

### 安全修正：sanitizeHTML（2026-09-27）

**修正**

- `sanitizeHTML`：拆掉不在允許清單的包裝元素時，會先清洗其子節點。原本子節點移出後不再經過清洗，`<section><img src=x onerror=...></section>` 這類內容會保留事件屬性，造成 XSS。
- SPA 範本前端的 `sanitizeHTML` 修正同一個拆殼繞過。它原本連 `<body>` 本身也當成不允許的標籤拆掉，任何非空輸入都會丟出例外；改為只清洗 `<body>` 的子節點。

**相容性驗證**

- 新增 26 項測試，涵蓋包裝元素繞過樣本、允許清單內容不變、重複清洗結果不變，以及 SPA 範本版本。
- Tim2026 沒有直接呼叫 `sanitizeHTML`。以包含本修正的 B4A 工作樹執行 Tim2026 的 2846 個單元與安全測試，結果與內嵌副本基準相同。

### 安全修正：sanitizeHTML（2026-09-27）

**修正**

- `sanitizeHTML`：拆掉不在允許清單的包裝元素時，會先清洗其子節點。原本子節點移出後不再經過清洗，`<section><img src=x onerror=...></section>` 這類內容會保留事件屬性，造成 XSS。
- SPA 範本前端的 `sanitizeHTML` 修正同一個拆殼繞過。它原本連 `<body>` 本身也當成不允許的標籤拆掉，任何非空輸入都會丟出例外；改為只清洗 `<body>` 的子節點。

**相容性驗證**

- 新增 26 項測試，涵蓋包裝元素繞過樣本、允許清單內容不變、重複清洗結果不變，以及 SPA 範本版本。
- Tim2026 沒有直接呼叫 `sanitizeHTML`。以包含本修正的 B4A 工作樹執行 Tim2026 的 2846 個單元與安全測試，結果與內嵌副本基準相同。

### 寫回 Tim2026 內嵌副本的通用改進（2026-09-27）

Tim2026 以 `packages/Bricks4Agent` 內嵌 B4A，並在正式環境中做了一批通用改進。本次把其中屬於通用能力的部分寫回 B4A，預設值對齊 Tim2026 正式環境的行為；Tim 專屬的業務內容不寫回。

**新增**

- DataTable 快速篩選：`search: true` 時在工具列顯示篩選框，只篩選已載入的資料列。新增 `getSearchText()`、`getSearchDraft()`、`setSearchText()`、`clearSearch()`，欄位可設 `searchable: false`，文字可用 `options.textLabels.search` 覆寫。預設關閉。
- DynamicDetailRenderer 子表格：欄位支援 `hidden`、`width`、`link.route` 連結樣板與 `link.target`；表格支援 `titleTemplate`、`pagination`、`pageSize`、`rowsPerPageOptions`、`search`；資料中的 `subtableErrors[id]` 會以錯誤訊息取代空表。
- 查詢定義的 `table.textLabels.search` 會傳給 DataTable 快速篩選。
- Link：頁面有 `<meta name="app-path-base">` 時，外開的 hash 路由會補上部署路徑前綴；沒有該 meta 時行為不變。
- TextArea：新增 `sizing`、`sizingToggle`、`onSizingChange` 選項，`getSizing()`、`setSizing()` 方法，以及 `preferredTextAreaSizing()`、`TEXTAREA_SIZING_MODES`、`TEXTAREA_SIZING_STORAGE_KEY` 匯出。舊的 `autoResize: true` 等同 `sizing: 'auto'`。

**預設行為變更**

- TextArea 預設改為固定 5 行並顯示捲軸，框內右下角有「固定／依內容加高」切換鈕，使用者的選擇記在瀏覽器儲存空間，成為之後 TextArea 的預設。預設 `resize` 由 `vertical` 改為 `none`。原本預設為 4 行、可拖拉。
- Dropdown、MultiSelectDropdown 的選項清單展開時改用固定定位浮在最上層，不再被上層容器裁切，寬度依內容加寬。清單仍留在元件的 DOM 內。
- DatePicker 的月曆、TimePicker 的時間面板展開時暫時移到 `document.body` 並以固定定位顯示，收合後移回元件內。
- Dropdown 的選項節點只在展開時建立，收合時釋放。
- Dropdown 的值比對改為字串相等，例如 `1` 與 `'1'` 視為同一個選項。
- 宣告式列表（query 定義）的結果表預設開啟快速篩選；定義中設 `table.search: false` 可關閉。
- FieldResolver 產生的多行欄位不再依 `maxLength` 自動加高，一律由 TextArea 的共通行為決定；只有定義中明確指定 `rows` 時才傳入。

**修正**

- ModalPanel：`destroy()` 之後 `backdrop` 重新回到 `null`，恢復既有公開契約；在 `onClose` 內銷毀面板也能完整拆除遮罩。
- TgosMap：瀏覽器取不到 2D Canvas 時改用 TGOS 預設標記，不再導致地圖初始化失敗。
- QueryDefinitionAdapter：酬載樣板中沒有預設值的明確 `null` 會保留；空的動態綁定仍不會覆蓋 `payloadDefaults`。這同時修正了 `npm test` 中原本失敗的查詢酬載測試。
- Dropdown、MultiSelectDropdown、DatePicker、TimePicker 的 document 點擊與視窗捲動、縮放監聽，只在展開期間掛載，元件數量多時不再常駐。

**未寫回**

- Tim2026 版 TgosMap 以 SVG 圖片作為地圖標記，違反 B4A 禁用 SVG 的規則；保留 B4A 以 Canvas 繪製的標記。
- 其餘差異經比對為格式或註解差異，程式行為與 B4A 相同，保留 B4A 版本。

**相容性驗證**

- B4A：`npm test`、`audit-csp`、`audit-ui-style-rules`、`validate-ui-library`、`build-metadata --check` 全部通過。
- Tim2026：以本版本取代其內嵌副本，執行 Tim2026 的 2846 個單元與安全測試，結果與內嵌副本基準完全相同，沒有退步。
