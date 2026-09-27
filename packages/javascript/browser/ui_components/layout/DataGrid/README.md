# DataGrid 可編輯資料格

試算表式的可編輯資料格，用於大量資料輸入：以鍵盤在儲存格之間移動、直接輸入即編輯、從試算表貼上整塊資料、逐格驗證，並與基準資料比對標示修改。大量資料可開啟 `virtual` 只渲染可視範圍的列。

- 通用元件：只處理「列、欄、儲存格」，不含任何業務語意。
- 零相依、符合嚴格 CSP：儲存格內容一律以 `textContent` 寫入，樣式只用 CSSOM 與 `--cl-*` token，不注入 `<style>`、不需要額外的 CSS 檔。
- 深色主題由 `[data-theme="dark"]` 的 token 值自動套用。

既有的 `layout/EditableTable`（排序＋逐格 TextInput）維持不變；需要鍵盤導覽、剪貼簿、驗證與修改追蹤的大量輸入情境改用 DataGrid。

## 建立

```javascript
import { DataGrid } from './index.js';

const grid = new DataGrid({
    columns: [],                  // 欄位定義陣列（見下方「欄位定義」）
    rows: [],                     // 初始資料列；元件會複製，不改動呼叫端物件
    rowKey: 'id',                 // 列鍵欄位名稱，或 (row) => key；缺少或重複列鍵的列改用內部產生的鍵
    height: null,                 // CSS 長度（數字視為 px）；設定後在內部捲動並固定表頭
    virtual: false,               // true 時只渲染可視範圍的列（大量資料）；未設 height 時使用 400px
    rowHeight: 34,                // 列高（px，限 20–200）；virtual 以此計算列的位置
    disabled: false,              // 停用：不可編輯、貼上、清除，仍可瀏覽與複製
    showRowNumbers: false,        // 在最左側顯示列號欄（role="rowheader"，水平捲動時固定）
    allowAddRowsOnPaste: false,   // 貼上超過最後一列時自動新增列；false 時截斷並回報
    ariaLabel: '',                // 表格的無障礙名稱；空字串時使用語系預設（「資料表格」）
    onCellChange: null,           // ({ rowKey, key, value, oldValue, row }) => void，使用者變更儲存格時逐格觸發
    onChange: null,               // (rows) => void，使用者操作造成資料變更後觸發一次，rows 為全部資料列的複本
    onValidationChange: null,     // (errors) => void，錯誤清單內容改變時觸發，errors 同 getErrors()
    onPaste: null,                // (info) => void，貼上完成後觸發（見下方「回呼」）
});
grid.mount('#container');        // 也可傳入元素；回傳 this
```

### 欄位定義

| 屬性 | 型別 | 說明 |
|---|---|---|
| `key` | string | 欄位鍵，對應資料列的屬性名稱（必填，不可重複） |
| `label` | string | 表頭文字；省略時使用 `key` |
| `type` | `'text'` \| `'number'` \| `'select'` \| `'date'` \| `'checkbox'` | 欄位型別，預設 `'text'` |
| `width` | number \| string | 欄寬；數字為 px，字串可用 `px`、`rem`、`em`、`%`、`ch`、`fr`、`vw`。省略時為「最小寬度＋平分剩餘寬度」 |
| `align` | `'left'` \| `'center'` \| `'right'` | 對齊；預設 number 靠右、checkbox 置中、其餘靠左 |
| `required` | boolean | 必填（checkbox 必須勾選）；表頭顯示 `*` 並附螢幕閱讀器文字「必填」 |
| `readonly` | boolean \| `(row) => boolean` | 唯讀；函式形式可依列決定 |
| `min` / `max` | number \| string | number 欄的數值範圍；date 欄的日期範圍（`YYYY-MM-DD`） |
| `precision` | number | number 欄的小數位數（0–10）：顯示、編輯與貼上都依此四捨五入 |
| `maxLength` | number | text 欄的長度上限（編輯器限制輸入；貼上或程式寫入的過長值標示錯誤） |
| `options` | `[{ value, label }]` | select 欄的選項；也接受純值陣列 |
| `validate` | `(value, row) => string \| null` | 自訂驗證，回傳錯誤訊息或 `null` |
| `format` | `(value, row) => string` | 只影響顯示的格式化；輸出一律當純文字 |
| `compute` | `(row) => value` | 計算欄：唯讀，每次該列有變動後重算，結果寫入資料列 |

## 方法

| 方法 | 說明 |
|---|---|
| `mount(container)` | 掛載到元素或選擇器，回傳 `this` |
| `destroy()` | 移除 DOM、事件、計時器與 observer；可重複呼叫，之後呼叫其他方法不會丟錯 |
| `getValue()` | 同 `getRows()` |
| `setValue(rows)` | 同 `setRows(rows)`；不觸發 `onChange` |
| `clear()` | 清空所有資料列（同 `setRows([])`） |
| `setDisabled(disabled)` | 切換停用；停用時若正在編輯會放棄未提交的輸入 |
| `setError(message, { display = true } = {})` | 整個表格的欄位錯誤：訊息（`role="alert"`）顯示在表格下方，表格標示 `aria-invalid` 並以 `aria-describedby` 連到訊息、外框改為 danger 色；`display: false` 只標示狀態不顯示文字；空訊息等同 `clearError()` |
| `clearError()` | 清除 `setError()` 的標示與文字 |
| `getRows()` | 全部資料列的淺層複本（含計算欄的值，不含內部欄位） |
| `setRows(rows)` | 取代全部資料列並作為新的基準（清除修改標記與驗證錯誤） |
| `getRow(key)` | 依列鍵取得一列的複本；找不到回傳 `null` |
| `updateRow(key, patch)` | 以 `patch` 合併更新一列並重新驗證被改動的儲存格；回傳是否找到該列 |
| `addRow(row = {}, { index } = {})` | 新增一列（預設加在最後），回傳列鍵；新列在呼叫 `validate()` 或被編輯前不驗證 |
| `removeRows(keys)` | 依列鍵移除一或多列，回傳實際移除的列數 |
| `validate()` | 驗證所有儲存格，全部通過回傳 `true` |
| `getErrors()` | `[{ rowKey, key, message }]`，依列與欄的順序 |
| `isDirty()` | 與基準相比是否有新增、修改或移除 |
| `getChanges()` | `{ added: [row], updated: [{ rowKey, changes: { [key]: { from, to } } }], removed: [row] }` |
| `acceptChanges()` | 以目前資料作為新的基準（編輯中的內容先提交） |
| `revertChanges()` | 還原為基準資料並清除驗證錯誤 |
| `focusCell(rowKey, key)` | 將指定儲存格設為作用中並取得焦點；找不到回傳 `false` |
| `getActiveCell()` | 目前作用中的儲存格 `{ rowKey, key }` 或 `null` |
| `snapshot()` | 狀態快照：`lifecycle`、`availability`、`active`、`anchor`、`editing` |
| `DataGrid.parseTSV(text)` | 靜態方法：把試算表複製的 TSV 解析成二維字串陣列 |
| `DataGrid.toTSV(matrix)` | 靜態方法：把二維陣列轉成 TSV（含 tab、換行、雙引號的值以雙引號包住） |

程式呼叫的 `setRows`、`setValue`、`clear`、`updateRow`、`addRow`、`removeRows` 不觸發 `onCellChange` / `onChange`；`onValidationChange` 則在錯誤清單內容改變時一律觸發（包含 `validate()`、`updateRow()`、`setRows()`）。

## 回呼

| 回呼 | 參數 | 觸發時機 |
|---|---|---|
| `onCellChange` | `{ rowKey, key, value, oldValue, row }` | 使用者編輯提交、切換 checkbox、貼上、清除、剪下造成儲存格值改變時，逐格觸發；`row` 為該列（含重算後計算欄）的複本。計算欄的連動變化不另外觸發 |
| `onChange` | `rows` | 同一次使用者操作結束後觸發一次（例如一次貼上多格只觸發一次） |
| `onValidationChange` | `errors` | 錯誤清單內容改變時 |
| `onPaste` | `{ startRowKey, startKey, rows, cols, appliedRows, appliedCols, addedRows, truncatedRows, data }` | 貼上完成後。`rows`／`cols` 為剪貼簿資料的列數與最大欄數，`appliedRows`／`appliedCols` 為實際寫入的範圍，`addedRows` 為自動新增的列數，`truncatedRows` 為超出表格未貼上的列數，`data` 為解析後的字串陣列 |

## 鍵盤操作

焦點在儲存格上（非編輯中）：

| 按鍵 | 動作 |
|---|---|
| ↑ ↓ ← → | 移動一格（在邊界停住） |
| Home / End | 移到該列第一格 / 最後一格 |
| Ctrl+Home / Ctrl+End | 移到第一列第一格 / 最後一列最後一格 |
| PageUp / PageDown | 上下移動一頁（依可視高度；未設定 height 時為 10 列） |
| Tab / Shift+Tab | 往後 / 往前一格，列尾換到下一列；已在最後一格（或第一格）時不攔截，焦點離開表格 |
| Shift + 上述移動鍵 | 以起點為錨點擴大矩形選取；Shift+點擊也可擴大 |
| Ctrl+A | 選取全部儲存格 |
| Escape | 取消多格選取（沒有多格選取時不攔截，外層對話框可照常關閉） |
| Enter / F2 | 開始編輯作用中儲存格 |
| 可列印字元 | 開始編輯並以該字元取代原值（select 欄會選到開頭相符的選項；date 欄開啟空白的日期輸入） |
| 空白鍵 | checkbox 欄切換勾選；多格選取時，範圍內可編輯的 checkbox 一律設為作用中格切換後的值 |
| Delete / Backspace | 清除選取範圍內可編輯的儲存格（checkbox 設為 false，其餘設為 null） |
| Ctrl+C / Ctrl+X / Ctrl+V | 複製 / 剪下 / 貼上（TSV） |

編輯中：

| 按鍵 | 動作 |
|---|---|
| Enter / Shift+Enter | 提交並往下 / 往上移動 |
| Tab / Shift+Tab | 提交並移到下一格 / 上一格（列尾換列；在最後一格或第一格時提交後焦點離開表格） |
| Escape | 取消編輯，焦點回到儲存格 |
| 點擊其他位置 | 提交（編輯器失去焦點即提交） |

輸入法組字（例如注音）期間的 Enter 與 Escape 交給輸入法處理，不會提交或取消。

滑鼠：點擊設為作用中；拖曳選取矩形範圍；Shift+點擊擴大選取；雙擊開始編輯；點擊 checkbox 方塊切換勾選。

## 無障礙

- 表格為 `role="grid"`，帶 `aria-rowcount`、`aria-colcount`、`aria-multiselectable="true"` 與 `aria-label`；表頭列 `aria-rowindex="1"`，資料列從 2 起算，儲存格帶 `aria-colindex`（顯示列號時列號欄為第 1 欄）。`virtual` 模式下未渲染的列不在 DOM 中，已渲染列的 `aria-rowindex` 仍是其在全部資料中的位置。
- 同一時間只有作用中儲存格 `tabindex="0"`（roving tabindex），其餘為 `-1`；Tab 進入表格時落在作用中儲存格。沒有資料列時，表格本身可取得焦點（搭配 `allowAddRowsOnPaste: true` 可直接貼上建立資料列）。
- 多格選取的儲存格帶 `aria-selected="true"`；唯讀與計算欄帶 `aria-readonly="true"`；停用時表格帶 `aria-disabled="true"`。
- checkbox 欄以 `role="checkbox"` 與 `aria-checked` 呈現勾選狀態。
- **驗證訊息的提供方式：`aria-describedby`。** 無效的儲存格帶 `aria-invalid="true"`，並以 `aria-describedby` 指向儲存格內一個帶 `hidden` 屬性的訊息元素（不計入儲存格名稱，但會作為描述朗讀）；不使用 `title`。同一訊息也以文字顯示在表格下方（作用中儲存格有錯時），該視覺文字標示 `aria-hidden="true"` 以免重複朗讀。
- 修改標記不只靠顏色：右上角的三角標記（`aria-hidden`）搭配視覺隱藏文字「已修改」，螢幕閱讀器朗讀儲存格時一併讀出。
- 貼上、複製、清除與全選的結果透過 `role="status"`（`aria-live="polite"`）朗讀。
- 編輯器帶 `aria-label`（例如「數量，第 3 列」）。作用中儲存格在表格取得焦點時有 2px 外框（無效儲存格改用 danger 色）。

## 驗證

- 順序：必填 → 型別（number 可解析、date 為有效日期、select 為現有選項、checkbox 為布林）→ `min`／`max` → `maxLength` → 自訂 `validate`。
- 使用者編輯、貼上、清除、切換，以及 `updateRow()` 修改到的儲存格會立即驗證；同一列已被驗證過的儲存格與計算欄也會一併重驗（跨欄位規則可正確更新）。從未動過的儲存格（例如剛新增的空白列）只在呼叫 `validate()` 時驗證。
- 無法轉換的值不會被丟棄：例如數字欄輸入 `abc`，會保留原文並標示「請輸入數字」。
- number 編輯器提交時會解析（容許千分位逗號與全形數字）、依 `precision` 四捨五入並夾在 `min`／`max` 內；貼上與程式寫入只四捨五入、不夾住，超出範圍的值標示錯誤。
- `setRows()` 與 `revertChanges()` 會清除所有驗證錯誤。

## 修改追蹤

- 基準資料在建構、`setRows()`、`acceptChanges()` 時擷取。
- 與基準不同的儲存格顯示修改標記並帶 `data-modified="true"`；新增的列帶 `data-row-state="added"`，其中有值的儲存格顯示修改標記。計算欄不顯示標記（來源欄已標示），但 `getChanges()` 會列出其值變化。
- 比較方式：空值（`null`、`undefined`、空白字串）彼此相等；其餘以字串比較（`1` 與 `'1'` 視為相同）；checkbox 以布林比較。

## 剪貼簿

- 貼上（非編輯中）：解析 TSV（tab 分欄、換行分列；支援雙引號欄位內含 tab、換行與 `""`；結尾換行不會多出一列），從作用中儲存格（或選取範圍左上角）開始寫入。只貼上一個值且選取了多格時，填滿整個選取範圍。貼上的範圍會被選取。
- 型別轉換：number 解析並依 `precision` 四捨五入；date 接受 `YYYY-MM-DD`、`YYYY/M/D`、`YYYY.M.D`、`YYYYMMDD`；select 先比對 value、再不分大小寫比對顯示文字；checkbox 接受 `TRUE/FALSE`、`1/0`、`yes/no`、`y/n`、`on/off`、`是/否`、`✓`、`x`、`v`。無法轉換的值保留原文並標示錯誤。
- 唯讀與計算欄會被跳過（貼上資料的欄位位置不變）；超出最後一欄的資料捨棄；超出最後一列時依 `allowAddRowsOnPaste` 新增列或截斷。
- 編輯中的貼上交給編輯器原生處理。
- 複製：作用中儲存格或選取範圍轉成 TSV（select 取顯示文字、checkbox 為 `TRUE`／`FALSE`、number 依 `precision`，不套用 `format`），因此可原樣貼回。瀏覽器沒有觸發 copy 事件時，改用 Clipboard API 寫入。剪下 = 複製後清除可編輯的儲存格。

## 效能

- 未開啟 `virtual`：建構時一次渲染全部列；之後每次變更只比對並更新有變動的儲存格，不整表重繪。
- `virtual: true`：只渲染可視範圍（上下各多 6 列緩衝），捲動時以 `requestAnimationFrame` 更新；作用中列永遠保留在 DOM，焦點與編輯器不會因捲動消失。所有列使用相同的 `rowHeight`，儲存格內容單行顯示並以省略號截斷。
- 實測（Chrome，2,000 列 × 10 欄、`virtual: true`、高度 320px、`rowHeight: 32`）：建構約 11 毫秒，DOM 中約 16 列；Ctrl+End 跳到最後一列同樣只保留約 15 列。

## 樣式掛鉤

根元素 `.b4a-datagrid`；捲動容器 `.b4a-datagrid__frame`；表格 `.b4a-datagrid__grid`；資料列 `.b4a-datagrid__row`（`data-row-key`、`data-row-state`）；儲存格 `.b4a-datagrid__cell` 與 `.b4a-datagrid__cell--<type>`（`data-col-key`、`data-modified`）；編輯器 `.b4a-datagrid__editor`；空資料 `.b4a-datagrid__empty`；作用中儲存格的錯誤文字 `.b4a-datagrid__status`。顏色、圓角與字級全部來自 `--cl-*` token。

## 範例

以下程式放在 `packages/javascript/browser/` 目錄下的同源 `.js` 檔（頁面需載入 `ui_components/theme.css`，嚴格 CSP 下請勿使用行內 script）：

```javascript
import { DataGrid } from './ui_components/layout/DataGrid/index.js';

const rooms = [
    { value: 'r1', label: '會議室 A' },
    { value: 'r2', label: '會議室 B' },
];

const grid = new DataGrid({
    ariaLabel: '訂購明細',
    columns: [
        { key: 'item', label: '品項', required: true, maxLength: 40 },
        { key: 'qty', label: '數量', type: 'number', min: 1, max: 999, precision: 0, width: 90 },
        { key: 'price', label: '單價', type: 'number', min: 0, precision: 2, width: 110 },
        { key: 'room', label: '送達地點', type: 'select', options: rooms },
        { key: 'due', label: '到貨日', type: 'date', min: '2026-01-01' },
        { key: 'urgent', label: '急件', type: 'checkbox' },
        {
            key: 'amount', label: '小計', type: 'number', precision: 2,
            compute: (row) => (Number(row.qty) || 0) * (Number(row.price) || 0),
        },
    ],
    rows: [
        { id: 101, item: '白板筆', qty: 12, price: 25, room: 'r1', due: '2026-03-02', urgent: false },
        { id: 102, item: '投影機燈泡', qty: 1, price: 3200, room: 'r2', due: '2026-03-05', urgent: true },
    ],
    height: 360,
    showRowNumbers: true,
    allowAddRowsOnPaste: true,
    onValidationChange: (errors) => {
        document.querySelector('#save').disabled = errors.length > 0;
    },
}).mount('#order-lines');

document.querySelector('#add').addEventListener('click', () => {
    const key = grid.addRow({ qty: 1 });
    grid.focusCell(key, 'item');
});

document.querySelector('#save').addEventListener('click', () => {
    if (!grid.validate()) {
        const [first] = grid.getErrors();
        grid.focusCell(first.rowKey, first.key);
        return;
    }
    const { added, updated, removed } = grid.getChanges();
    console.log('送出變更', { added, updated, removed });
    grid.acceptChanges();
});
```

對應的 HTML：

```html
<div id="order-lines"></div>
<button id="add" type="button">新增一列</button>
<button id="save" type="button">儲存</button>
```

## 已知限制

- 列鍵在資料列進入表格時決定；之後修改列鍵欄位的值不會改變列鍵。缺少或重複列鍵的列使用內部產生的字串鍵（格式不保證，請以回傳值或 `getActiveCell()` 取得）。
- 以輸入法輸入中日韓文字時，請先按 Enter 或 F2（或雙擊）開啟編輯器再輸入；直接在儲存格上打字時，第一個按鍵會以原始字元送出。
- date 欄使用原生 `<input type="date">`：直接打字開始編輯時只會清空並開啟日期輸入，該字元是否帶入由瀏覽器決定。
- 編輯器內的方向鍵只移動游標，不會提交並移動儲存格。
- 不提供排序、篩選、欄寬拖曳、復原（undo）與凍結欄（列號欄除外）。
- 複製的內容是原始值；以 `=` 開頭的文字貼到試算表軟體時可能被當成公式，匯出給外部使用時請自行處理。
