# 變更紀錄

本檔記錄 Bricks4Agent（B4A）對使用者可見的變更，依相容性分類：

- **新增**：新的選項、方法或匯出，預設不改變既有行為。
- **預設行為變更**：不改呼叫方式，但預設畫面或互動與前一版不同。
- **修正**：缺陷修正；若修正會改變可觀察行為，會在條目中寫明。

B4A 只收通用元件與通用能力；任何業務系統的專屬元件都不寫入 B4A。

## 未發行

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
