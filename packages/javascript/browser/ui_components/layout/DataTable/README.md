# DataTable

資料表格元件，提供排序、分頁、選取、快速篩選、自訂渲染等功能；另有六項**選用**進階功能：伺服器端資料來源、固定表頭、固定欄、欄位顯示切換、列展開明細、依 key 跨頁選取。

進階功能全部預設關閉。未設定對應選項時，產生的 DOM、事件與回呼參數與既有版本完全相同。

## API

### Constructor

```js
new DataTable(config?)
// 或兩參數模式：
new DataTable(containerElement, config)
```

完整選項與預設值（標示「進階」的選項可以寫在 config 頂層，也可以寫在 `config.options` 內；兩處都有時以 `options` 為準）：

```js
const table = new DataTable({
    container: null,                 // 容器元素；省略時稍後呼叫 mount()
    title: '',                       // 工具列標題；字串會跳脫，raw() 可輸出 HTML
    columns: [],                     // 欄位定義，支援三種格式（見下方「欄位定義」）
    data: [],                        // 資料：物件陣列或 2D 陣列（依欄位格式）
    variant: 'default',              // 主題：'default' | 'search'
    pagination: true,                // false 停用分頁
    pageSize: 10,                    // 每頁筆數；未指定時取 rowsPerPageOptions 第一項
    pageSizeOptions: undefined,      // 每頁筆數選項（同 rowsPerPageOptions）
    emptyText: undefined,            // 無資料文字；未指定時為 Locale 的 dataTable.noMatch
    striped: undefined,              // 相容用，目前無作用（斑馬紋依主題 CSS）
    hoverable: undefined,            // 相容用，目前無作用（懸停效果依主題 CSS）
    selectableRows: 'multiple',      // 行選取：'multiple' | 'single' | 'none'
    sortOrder: null,                 // 初始排序 { name, direction: 'asc' | 'desc' }
    search: false,                   // true：工具列顯示快速篩選（見「快速篩選」）
    customToolbar: null,             // 工具列右側內容：HTML 字串或 () => string
    customToolbarSelect: null,       // 有選取時的工具列：(selectedRowsObj, data, setSelectedRows) => string
    rowsPerPageOptions: [10, 20, 100, 500, 1000], // 每頁筆數下拉選項

    // ── 進階：伺服器端模式 ──
    serverSide: false,               // true：分頁、排序、快速搜尋交給 dataSource 在伺服器端處理
    dataSource: null,                // async (query) => ({ rows, total })
    searchDebounce: 300,             // 伺服器端模式快速搜尋的輸入防抖（毫秒）
    onQueryChange: null,             // (query) => void；伺服器端模式每次送出查詢前呼叫
    // ── 進階：固定表頭 ──
    stickyHeader: false,             // true：表頭固定，表身在元件內捲動
    maxHeight: null,                 // 捲動區最大高度（CSS 長度或 px 數字）；stickyHeader 未指定時用 '60vh'
    // ── 進階：欄位顯示切換 ──
    columnToggle: false,             // true：工具列顯示「欄位」選單
    onColumnVisibilityChange: null,  // (visibility) => void；使用者由選單切換欄位後呼叫
    // ── 進階：列展開明細 ──
    expandable: null,                // { render(row, index), rowExpandable?(row, index), expandOnRowClick?: false }
    // ── 進階：依 key 跨頁選取 ──
    rowKey: null,                    // 欄位名稱字串或 (row, index) => key
    selectAllScope: 'page',          // rowKey 模式的表頭全選範圍：'page' | 'filtered'

    options: {
        textLabels: {},              // 自訂文字（見「文字與語系」）
        tableBodyHeight: null,       // 捲動區最大高度（既有選項；maxHeight 優先）
        onRender: null,              // (element, table) => void；每次渲染完成後呼叫
        onRowSelectionChange: null,  // (_, allSelected, selectedIndices, selectedKeys?) => void
        // selectableRows、search、customToolbar、customToolbarSelect、rowsPerPageOptions、sortOrder
        // 與所有「進階」選項也可以寫在這裡
    },
});
```

進階選項在建構時決定行為；建構後直接改寫 `table.options` 不保證生效。

### 欄位定義

| 格式 | 寫法 | 說明 |
|---|---|---|
| 標準 | `{ name, label, options: { customBodyRender, customBodyRenderLite, display, setCellProps, sort, searchable, sticky, hideable } }` | `data` 為 2D 陣列 |
| Audit | `{ key, title, width?, sortable?, render?, hidden?, html?, searchable?, sticky?, hideable? }` | `data` 為物件陣列 |
| Search | `{ title, visible?, hidden?, width?, render?, searchable?, sticky?, hideable? }` | `data` 為 2D 陣列，欄位 key 為 `col_0`、`col_1`… |

進階欄位屬性（標準格式寫在 `options` 內或欄位物件上皆可）：

| 屬性 | 說明 |
|---|---|
| `sticky: 'left' \| 'right'` | 固定欄；必須從左右兩端連續排列，不連續的宣告會被忽略（見「固定欄」） |
| `hideable: false` | 欄位顯示狀態鎖定，使用者與 `setColumnVisible()` 都不能改變 |

### 方法

| 方法 | 回傳 | 說明 |
|---|---|---|
| `setData(data, total?)` | `this` | 設定資料並重新渲染。一般模式重置分頁/選取；伺服器端模式見「伺服器端模式」 |
| `getData()` | `Array` | 取得目前資料（正規化後的 2D 陣列） |
| `getSelectedRows()` | `Array` | 未設定 `rowKey`：已選取列的 dataIndex 陣列（既有行為）。設定 `rowKey`：已選取的**列物件**陣列，含其他頁面的列 |
| `setSelectedRows(indices)` | `void` | 以 dataIndex 設定選取並重新渲染；`rowKey` 模式會轉成 key 後取代整個選取 |
| `getSelectedKeys()` | `Array` | 設定 `rowKey` 時回傳跨頁的 key 陣列；未設定時回傳 dataIndex 陣列 |
| `setSelectedKeys(keys)` | `this` | 以 key 取代選取（尚未載入的 key 也會保留）；未設定 `rowKey` 時 keys 視為 dataIndex |
| `clearSelection()` | `this` | 清除全部選取（含其他頁面） |
| `render()` | `void` | 重新渲染表格至 container |
| `reload()` | `Promise<boolean>` | 重新執行目前查詢。伺服器端模式再呼叫一次 dataSource；一般模式只重新渲染。`true` 表示結果已套用，`false` 表示被較新的查詢取代、中止、失敗、已銷毀或未提供 dataSource |
| `getQuery()` | `Object` | 目前查詢條件 `{ page, pageSize, sort, search }`（page 從 1 起算） |
| `getSearchText()` | `string` | 目前套用中的快速篩選文字 |
| `getSearchDraft()` | `string` | 篩選框內尚未套用的文字 |
| `setSearchText(text)` | `this` | 設定並立即套用快速篩選，回到第一頁（伺服器端模式文字實質未變時不重新查詢） |
| `clearSearch()` | `this` | 清除快速篩選 |
| `setColumnVisible(key, visible, { emit = false } = {})` | `this` | 顯示/隱藏欄位；`key` 為欄位 key 或欄位索引；`emit: true` 才觸發 `onColumnVisibilityChange` |
| `getColumnVisibility()` | `Object` | 所有欄位的顯示狀態 `{ [key]: boolean }` |
| `expandRow(key)` / `collapseRow(key)` / `toggleRow(key)` | `this` | 展開/收合列明細；`key` 為 rowKey 值，未設定 rowKey 時為 dataIndex |
| `getExpandedKeys()` | `Array` | 目前展開中的 key |
| `mount(container)` | `this` | 掛載至容器（CSS 選擇器或 DOM 元素）；destroy 之後再 mount 會重新啟用 |
| `destroy()` | `void` | 銷毀元件：中止進行中的查詢、清除防抖計時器、關閉欄位選單並移除其 document/window 監聽、中斷 ResizeObserver、清空 DOM；可重複呼叫 |

### 事件與回呼

| 回呼 | 參數 | 觸發時機 |
|---|---|---|
| `options.onRender` | `(element, table)` | 每次渲染完成（伺服器端模式的「載入中」渲染也會觸發） |
| `options.onRowSelectionChange` | `(_, allSelected, selectedIndices, selectedKeys?)` | 使用者勾選/全選變更時；未設定 `rowKey` 時，快速篩選文字改變、隱藏欄位使篩選結果變少，或伺服器端模式載入新資料而清除選取時也會發出。第四個參數只在 `rowKey` 模式提供（跨頁完整 key 清單）；前三個參數只描述目前已載入的列 |
| `onQueryChange` | `(query)` | 伺服器端模式每次送出查詢前（含首次查詢）；`query` 與傳給 dataSource 的是同一個物件 |
| `onColumnVisibilityChange` | `(visibility)` | 使用者在欄位選單切換欄位後；程式呼叫 `setColumnVisible` 只有傳 `{ emit: true }` 才觸發 |
| `dataSource` | `(query) => Promise<{ rows, total }>` | 伺服器端模式需要資料時 |

以程式呼叫的 `setSelectedRows`、`setSelectedKeys`、`clearSelection` 不觸發 `onRowSelectionChange`。

### 選取變更的更新方式

使用者點擊行 checkbox 或表頭全選時，DataTable **只定點同步選取相關 DOM**（列的 `--selected` / `--even` class、行 checkbox、表頭全選狀態），不重建整張表：

- `onRender` **不會**因為純選取變更而重發

- 儲存格內既有的元件實例與事件監聽器不會被銷毀重建，外部加在 `<tr>` 上的其他 class 也會保留

- `onRowSelectionChange` 仍照常發出

以下情況維持完整重繪（工具列內容依賴選取狀態，`onRender` 會再次發出）：

- 設定了 `options.customToolbarSelect`

- `options.customToolbar` 傳入的是函式（可能讀取選取狀態）

- 由程式呼叫 `setSelectedRows(indices)`、`setSelectedKeys(keys)`、`clearSelection()`

排序結果在單次渲染流程內共用（工具列、事件綁定重複取用時免重算），流程結束即清除；渲染流程外的呼叫維持即時重算。

### 快速篩選

`search: true` 時工具列會出現篩選框、套用按鈕與「顯示 N / 共 M 筆」計數。規則如下：

- 一般模式只比對目前已載入的資料，不會發出查詢；伺服器端模式（`serverSide: true`）則把文字放進 `query.search` 交給 dataSource，見「伺服器端模式」。

- 比對的是儲存格實際顯示的文字：`render`、`customBodyRender` 的結果與 `raw()` 內容的可見文字都納入；比對前做 NFKC 正規化並忽略大小寫，所以全形、半形視為相同。

- 欄位設 `searchable: false`、動作欄位，以及目前隱藏的欄位（`hidden`、`visible: false`，或由欄位選單/`setColumnVisible` 隱藏）不列入比對。

- 按 Enter 或套用按鈕才生效；中文輸入法組字期間的 Enter 不會觸發。

- 未設定 `rowKey` 時，篩選文字改變會清除既有選取，表頭全選只涵蓋篩選後的資料列；隱藏欄位使篩選結果變少時，不再符合的選取也會一併移除。設定 `rowKey` 時選取跨篩選保留（見「依 key 跨頁選取」）。

## 伺服器端模式（serverSide）

`serverSide: true` 時，表格不在本地排序、篩選或分頁：每次換頁、排序、改變每頁筆數、快速搜尋都會呼叫 `dataSource(query)`，顯示它回傳的 `rows`，並以 `total` 計算分頁。

```js
query = {
    page: 1,                                  // 從 1 起算
    pageSize: 10,
    sort: { key: 'amount', direction: 'desc' }, // 或 null；key 為欄位 key
    search: 'north',                          // 快速搜尋文字（已去頭尾空白）
    signal: AbortSignal,                      // 下一次查詢開始或 destroy() 時會 abort
}
// dataSource 回傳 { rows, total }；rows 為這一頁的資料（物件陣列或 2D 陣列，依欄位格式）
```

- **首次查詢**：在建構子返回後的 microtask 送出，所以 dataSource / onQueryChange 可以放心引用剛建立的實例；首次渲染即呈現載入中狀態。

- **載入中**：`.b4a-dt__scroll` 加上 `aria-busy="true"`，工具列下方的狀態列（`role="status"`）顯示 `LoadingSpinner` 與「載入中…」，舊資料列淡化；尚無資料時以空白列代替「無查詢結果」。

- **錯誤**：dataSource 丟出例外或 reject 時，表身顯示錯誤訊息與「重試」按鈕（`role="alert"`），不會顯示伺服器回傳的錯誤內容；上一頁的資料列會清除。按「重試」等同 `reload()`；若重試又失敗，焦點會回到新的重試按鈕。

- **過期回應**：新查詢會 abort 前一個請求的 signal，並以遞增序號忽略任何晚到的回應，dataSource 沒有理會 signal 也安全。

- **快速搜尋**：輸入後防抖 `searchDebounce` 毫秒自動查詢並回到第一頁；中文輸入法組字期間不送出，組字結束後才排程；Enter 或套用按鈕立即查詢；文字實質未變（去頭尾空白後相同）不重新查詢。回應抵達時若正在組字，重繪會延到組字結束，避免打斷輸入。篩選框預設提示改為「搜尋」，計數顯示「共 N 筆」（`total`）。

- **焦點**：非同步重繪（回應抵達）後，焦點會還原到原本的控制項（篩選框含游標位置、分頁按鈕、每頁筆數…）。

- **超出範圍**：`total` 變少使目前頁超出範圍時（例如刪除後重新載入），自動改查最後一頁。

- **setData(rows, total)**：呼叫端也可以自行推入目前這一頁的資料；不會回到第一頁，並會取消尚未回來的 dataSource 請求（以推入的資料為準），`total` 省略時取 `rows.length`。

- **受控模式**：不提供 `dataSource`、只提供 `onQueryChange` 時，表格只負責發出查詢條件；呼叫端取回資料後以 `setData(rows, total)` 推入。此時 `reload()` 會再次觸發 `onQueryChange` 並回傳 `false`。

- **選取**：未設定 `rowKey` 時，dataIndex 只對應目前這一頁，換頁或重新查詢後選取與展開狀態都會清除（並發出 `onRowSelectionChange`）。需要跨頁保留請設定 `rowKey`。

- `pagination: false` 時不顯示分頁列，查詢仍帶 `page: 1` 與 `pageSize`。

## 固定表頭（stickyHeader）

`stickyHeader: true` 時，捲動區（`.b4a-dt__scroll`）限制最大高度，表身在元件內捲動，表頭儲存格以 `position: sticky; top: 0` 固定。

- 高度依序取 `maxHeight`、`options.tableBodyHeight`，都沒有時用 `'60vh'`。`maxHeight` 可給 CSS 長度字串或 px 數字，不開 stickyHeader 也可單獨用來限制高度（優先於 `tableBodyHeight`）。

- 固定的是元件內部的捲動，不是整個頁面捲動時的表頭。

## 固定欄（sticky）

欄位設 `sticky: 'left'` 或 `sticky: 'right'`，水平捲動時保持可見：

- 必須從左右兩端**連續**排列（以目前顯示中的欄位計算）；中間夾了非固定欄之後的 `sticky: 'left'` 會被忽略。

- 左側有固定欄時，前導的展開鈕欄與勾選欄也一併固定。

- 偏移量優先採用實際量測的欄寬，尚未排版（量不到）時用宣告寬度（`width` 或 `setCellProps` 的 `width` / `minWidth`，僅限 px）；表格尺寸改變時以 `ResizeObserver` 重新量測，`destroy()` 時中斷。

- 固定的儲存格沿用所屬列的底色（奇偶、選取、懸停），捲動時下方內容不會透出；最內側的固定欄有細微陰影。

## 欄位顯示切換（columnToggle）

`columnToggle: true` 時工具列出現「欄位」按鈕，開啟的選單列出各欄位的勾選狀態：

- 初始狀態沿用欄位定義的 `hidden`、`visible: false`、`display: false`。

- `hideable: false` 的欄位鎖定目前的顯示狀態：顯示中的欄位在選單中以停用項目呈現；隱藏中的欄位（通常是只供 render 取值的資料欄）不列入選單。

- 選單開啟時掛到 `document.body` 並以 `position: fixed` 對齊按鈕，切換欄位時表格整表重繪但選單保持開啟、焦點留在同一項；document / window 監聽只在選單開啟期間存在。

- 切換後觸發 `onColumnVisibilityChange(getColumnVisibility())`。也可用 `setColumnVisible()` / `getColumnVisibility()` 以程式控制（不需開啟 columnToggle）。

## 列展開明細（expandable）

```js
expandable: {
    render: (row, index) => Node | string,   // 必填；row 為呼叫端傳入的原始列物件
    rowExpandable: (row, index) => boolean,  // 選填；回傳 false 的列不顯示展開鈕
    expandOnRowClick: false,                 // 選填；true 時點列（非互動元素）也會切換
}
```

- 每列最前面多一欄展開鈕（`<button>`，`aria-expanded`；展開時 `aria-controls` 指向明細列的 id）。展開後在該列下方插入一列明細（`.b4a-dt__detail-row`），就地插入、不整表重繪，焦點留在展開鈕上。

- `render` 回傳 DOM 節點時直接插入；回傳字串（或其他值）一律以 `textContent` 呈現，**不會解析 HTML**，`raw()` 在這裡也視為純文字。需要結構化內容請回傳 DOM 節點。每次表格重繪都會重新呼叫 `render`。

- `expandOnRowClick: true` 時，點在列內的連結、按鈕、輸入框、checkbox 等互動元素上不會切換，拖曳選取文字時也不會。

- 展開狀態以 key 記錄：設定 `rowKey` 時為 rowKey 值，跨排序、篩選、換頁與伺服器端重新載入保留，也可以預先展開尚未載入的 key；未設定 `rowKey` 時以 dataIndex 記錄（不存在的索引會被忽略），本地排序/篩選/換頁仍保留，但 `setData()` 或伺服器端模式重新查詢（換頁、排序、搜尋）後會清空。`rowExpandable` 回傳 false 的列無法展開。

## 依 key 跨頁選取（rowKey）

`rowKey` 為欄位名稱（讀取原始列物件的屬性；2D 陣列資料則比對欄位 key）或 `(row, index) => key` 函式。設定後選取改以 key 記錄：

- 跨換頁、排序、快速篩選與伺服器端重新載入保留，快速篩選不會清除選取；有 `customToolbarSelect` 時工具列顯示跨頁的總選取數。

- `getSelectedRows()` 改為回傳**列物件**（含已不在目前頁面的列，依選取先後排序）；`getSelectedKeys()` 回傳 key。以 `setSelectedKeys()` 設定、但從未載入過的 key 只會出現在 `getSelectedKeys()`，該列載入後自動勾選並納入 `getSelectedRows()`。

- 表頭全選只作用在目前顯示的列：`selectAllScope: 'page'`（預設）為目前這一頁（篩選後）；`'filtered'` 為快速篩選後的全部列（一般模式）。伺服器端模式只有已載入的這一頁可選。部分選取時表頭 checkbox 呈現 `indeterminate`。

- `onRowSelectionChange` 多帶第四個參數：跨頁的完整 key 清單。

- key 必須唯一且非空；取不到 key 的列會給本次資料內唯一的替代 key（不會跨頁或跨重新載入保留），並在主控台警告一次。

- 未設定 `rowKey` 時，所有選取行為維持原本以 dataIndex 為準的方式。

## 鍵盤與 ARIA

| 對象 | 鍵盤 | ARIA |
|---|---|---|
| 快速篩選框 | Enter 套用（組字中不觸發）；伺服器端模式輸入即防抖查詢 | `aria-label`；計數 `aria-live="polite"` |
| 「欄位」按鈕 | Enter / Space / 點擊開關選單並聚焦第一項；↓ / ↑ 開啟並聚焦第一 / 最後一項；Esc 關閉 | `aria-haspopup="menu"`、`aria-expanded`、開啟時 `aria-controls` |
| 欄位選單 | ↑ / ↓ 循環移動、Home / End、Space / Enter 切換（選單保持開啟）、Esc 關閉並把焦點還給按鈕（不外傳給外層對話框）、Tab 關閉並從按鈕繼續 Tab 順序；點選單外關閉 | `role="menu"`、項目 `role="menuitemcheckbox"` + `aria-checked`，鎖定欄位 `aria-disabled="true"` |
| 展開鈕 | 原生按鈕，Enter / Space 切換 | `aria-expanded`、`aria-controls`（展開時），名稱「列明細」；表頭有螢幕閱讀器文字「明細」 |
| 載入/錯誤 | 「重試」為原生按鈕 | 捲動區 `aria-busy="true"`、狀態列 `role="status"`、錯誤 `role="alert"` |
| 表頭全選 | 原生 checkbox | rowKey 模式部分選取時 `indeterminate` |

## 文字與語系

所有新字串走 `Locale.t('dataTable.*')`，在渲染時求值（切換語系後重新渲染即更新），也可用 `options.textLabels` 覆寫：

| textLabels | 預設（Locale key） |
|---|---|
| `body.loading` | `dataTable.loading`「載入中…」 |
| `body.loadError` | `dataTable.loadError`「資料載入失敗」 |
| `body.retry` | `dataTable.retry`「重試」 |
| `search.placeholder` | 一般模式 `dataTable.searchPlaceholder`；伺服器端模式 `dataTable.serverSearchPlaceholder`「搜尋」 |
| `search.serverResultCount` | `dataTable.serverResultCount`「共 {total} 筆」 |
| `columns.button` | `dataTable.columnToggle`「欄位」 |
| `columns.menu` | `dataTable.columnMenuLabel`「顯示欄位」 |
| `expand.header` | `dataTable.expandColumn`「明細」 |
| `expand.toggle` | `dataTable.toggleRowDetails`「列明細」 |

既有的 `pagination`、`body.noMatch`、`selectedRows`、`search.resultCount`、`search.buttonLabel` 維持原本行為。

### 具名匯出

- `linkCell(text, href, options?)` — 產生連結儲存格（內部以 `Link` 元件 hydrate）

- `badgeCell(text, options?)` — 產生徽章儲存格（內部以 `Badge` 元件 hydrate）

### 依賴

- `Link`、`Badge` — `linkCell()` / `badgeCell()` 儲存格渲染

- `LoadingSpinner` — 伺服器端模式的載入指示

- `utils/security.js` — `escapeHtml` / `isRawHtml` / `raw`

- `utils/uid.js` — 明細列與欄位選單的 DOM id（只在用到時取號）

## 使用範例

```js
import { DataTable } from './DataTable.js';
import { raw } from '../../utils/security.js';

const table = new DataTable({
    columns: [
        { key: 'id', hidden: true },   // render 的 row 只含 columns 定義的 key
        { key: 'name', title: '姓名' },
        { key: 'age', title: '年齡' },
        { key: 'action', title: '操作', render: (_, row) => raw(`<button>編輯</button>`) }
    ],
    data: [{ id: 1, name: '張三', age: 28 }],
    pageSize: 10,
    options: {
        selectableRows: 'multiple',
        onRowSelectionChange: (_, allSelected, indices) => console.log(indices)
    }
});
table.mount('#app');
```

### 進階功能範例（可直接執行）

以記憶體陣列模擬伺服器端 API，同時啟用伺服器端模式、固定表頭、固定欄、欄位切換、列展開與跨頁選取：

```js
import { DataTable } from './DataTable.js';

const orders = Array.from({ length: 87 }, (_, i) => ({
    id: i + 1,
    code: `ORD-${String(i + 1).padStart(4, '0')}`,
    customer: ['North Store', 'South Market', 'East Depot'][i % 3],
    owner: ['Kim', 'Lee', 'Park'][i % 3],
    amount: (i * 37) % 1000,
}));

// 模擬後端：依 query 篩選、排序、分頁；真實情況請把 query.signal 傳給 fetch
async function dataSource({ page, pageSize, sort, search, signal }) {
    await new Promise(resolve => setTimeout(resolve, 300));
    if (signal?.aborted) return { rows: [], total: 0 };
    let rows = orders.filter(o => !search || `${o.code} ${o.customer} ${o.owner}`.toLowerCase().includes(search.toLowerCase()));
    if (sort) {
        const dir = sort.direction === 'desc' ? -1 : 1;
        rows = [...rows].sort((a, b) => (a[sort.key] > b[sort.key] ? 1 : a[sort.key] < b[sort.key] ? -1 : 0) * dir);
    }
    return { rows: rows.slice((page - 1) * pageSize, page * pageSize), total: rows.length };
}

const table = new DataTable(document.querySelector('#orders'), {
    title: '訂單',
    columns: [
        { key: 'code', title: '編號', width: '120px', sticky: 'left', hideable: false },
        { key: 'customer', title: '客戶' },
        { key: 'owner', title: '負責人' },
        { key: 'amount', title: '金額', width: '100px', sticky: 'right' },
    ],
    serverSide: true,
    dataSource,
    search: true,
    pageSize: 10,
    stickyHeader: true,
    maxHeight: '360px',
    columnToggle: true,
    rowKey: 'id',
    selectableRows: 'multiple',
    expandable: { render: row => `${row.code}：${row.customer}，負責人 ${row.owner}` },
    onColumnVisibilityChange: visibility => console.log('欄位', visibility),
    options: {
        onRowSelectionChange: (_, __, ___, keys) => console.log('已選取', keys),
    },
});

// 之後：await table.reload(); table.getSelectedRows(); table.destroy();
```

## XSS 安全協議（render / customBodyRender）

DataTable 預設對 `render` / `customBodyRender` 的回傳值進行 HTML 跳脫（escapeHtml），防止 XSS。

若需輸出原始 HTML（如按鈕、圖示），必須使用 `raw()` 包裝：

```js
import { raw, escapeAttr } from '../../utils/security.js';

const table = new DataTable({
    columns: [
        // 隱藏欄位 — render 收到的 row 只含 columns 定義的 key，要用 row.id 就必須宣告
        { key: 'id', hidden: true },

        // 純文字 — 自動跳脫，安全
        { key: 'name', title: '姓名' },

        // 需要 HTML — 必須用 raw() 明確標記
        { key: 'action', title: '操作',
          render: (_, row) => raw(`<button data-id="${escapeAttr(row.id)}">編輯</button>`)
        }
    ]
});
```

**規則**：
- `render` 回傳 `string` → 自動 HTML 跳脫（safe-by-default）
- `render` 回傳 `raw(html)` → 原樣輸出（開發者負責確保安全）
- `render(value, row)` 的 `row` 只包含 `columns` 中宣告的 key（資料物件的其他屬性會被捨棄）；需要的欄位請以 `{ key, hidden: true }` 加入
- 在 `raw()` 內部使用使用者資料時，務必用 `escapeHtml()` / `escapeAttr()` 跳脫
- 只有 `raw()` 產生的物件會被認可：標記以 `Symbol.for('bricks4agent.rawHtml')` 品牌識別，手寫的 `{ __html: '...' }`（或 API 回傳的同名 JSON 欄位）一律當成一般值跳脫
- `expandable.render` 的回傳值不走 HTML：字串一律 `textContent`，要結構化內容請回傳 DOM 節點；`rowKey` 函式與 `expandable.render` 收到的 `row` 是呼叫端傳入的**完整原始列物件**

## Demo

開啟 `demo.html` 直接在瀏覽器測試。
