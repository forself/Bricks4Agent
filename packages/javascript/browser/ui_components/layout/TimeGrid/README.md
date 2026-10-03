# TimeGrid 時段格

以「列＝時段（slot）、欄＝日期或資源（column）」呈現排程的格狀檢視。項目（item）佔同一欄內一或多個連續時段，可用於會議室預約、人員排班或任何時間表；元件只處理時段、欄與項目，不帶任何業務語意。

- DOM + CSS grid 版面，樣式全部走 CSSOM 與 `var(--cl-*)` token，嚴格 CSP（`style-src 'self'`）下可直接使用，不注入 `<style>`、不用 SVG。
- 項目位置以時段索引算術求得，整張表在離線片段中一次建好再掛上；1,000 個項目、7 欄 × 30 時段一次渲染完成，渲染過程不讀取版面。
- 同一欄重疊的項目依「重疊群組」分道並排；`ghost` 項目另成一層半透明虛線預覽，疊在一般項目上方、不擠壓它們。
- 可選取（點擊或拖曳空白格）、可拖放移動（指標或鍵盤），移動一律先經 `onItemMove` 同意（可非同步），同意後元件才更新自己的狀態。
- 欄頭與時段標籤在捲動容器內黏附；可選的目前時間線每分鐘更新一次。
- 完整鍵盤操作與 ARIA grid 語意，操作結果透過 polite live region 播報（有節流）。

## 匯入

```js
import { TimeGrid } from './ui_components/layout/TimeGrid/index.js';
```

頁面需載入 `ui_components/theme.css` 取得 `--cl-*` token；深色模式由 `[data-theme="dark"]` 切換，元件不需額外處理。

## 建構選項

```js
const grid = new TimeGrid({
    columns: [],                // 欄定義 [{ key, label, sublabel?, date?: 'YYYY-MM-DD' }]；label 預設為 key
    slots: null,                // 明確時段 [{ key, label?, start: 'HH:mm', end: 'HH:mm' }]；null 時改用 timeRange 產生
    timeRange: { start: '08:00', end: '18:00', step: 30 }, // slots 為 null 時產生等長時段（step 單位：分鐘，最後一段可較短）
    items: [],                  // 項目 [{ id, column, start, end?, title, subtitle?, variant?, ghost?, draggable?, data? }]
    selectable: false,          // 允許點擊或拖曳同一欄的空白格選取連續時段 → onSelect
    editable: false,            // 允許以指標或鍵盤把項目拖放到其他欄／時段 → onItemMove
    readonly: false,            // true 時停用選取與移動（不論 selectable / editable），點擊回呼照常觸發
    slotHeight: 40,             // 每個時段列的高度（px）
    height: null,               // 捲動容器高度：數字＝px，或 CSS 長度字串（如 '70vh'）；null＝不限高（只做水平捲動）
    columnMinWidth: 120,        // 每欄最小寬度（px）；總寬不足時水平捲動
    slotLabelWidth: 72,         // 左側時段標籤欄寬度（px）
    nowIndicator: false,        // 在 date 等於今天的欄畫出目前時間線，每分鐘更新
    now: () => new Date(),      // 取得目前時間的函式（可注入，方便測試或對時）
    ariaLabel: '',              // 表格的無障礙名稱；空字串時用 Locale 的 timeGrid.gridLabel
    onSelect: null,             // (range) => void；range 見下方「時段範圍物件」
    onItemMove: null,           // ({ item, from, to }) => boolean | Promise<boolean>；回傳 false 表示拒絕
    onItemClick: null,          // (item, event) => void
    onCellClick: null           // ({ column, slot, startTime, endTime }, event) => void
});
```

### 欄（columns）

| 屬性 | 說明 |
|---|---|
| `key` | 必填，欄的唯一識別（字串或數字；比對時轉成字串，回呼中回傳原值） |
| `label` | 欄頭文字，預設為 `key` |
| `sublabel` | 欄頭第二行（例如日期） |
| `date` | `'YYYY-MM-DD'`，只用於 `nowIndicator` 判斷「今天」 |

### 時段（slots / timeRange）

- `timeRange` 以 `start`～`end` 每 `step` 分鐘切一段，key 為每段的開始時間 `'HH:mm'`，標籤預設 `'HH:mm–HH:mm'`。
- 明確的 `slots` 可以長短不一、中間留空檔（例如休息時間）；會依開始時間排序。`key` 省略時用開始時間。
- 無效的時段（時間格式錯、結束不晚於開始、key 重複、與前一段重疊）會略過並以 `console.warn` 回報一次。

### 項目（items）

| 屬性 | 說明 |
|---|---|
| `id` | 唯一識別；省略時自動產生。重複的 id 會略過並警告 |
| `column` | 所在欄的 key |
| `start` | 開始：時段 key 或 `'HH:mm'` |
| `end` | 結束（**不含**）：時段 key 或 `'HH:mm'`；省略時只佔 `start` 所在的一個時段 |
| `title` / `subtitle` | 顯示文字（一律以 `textContent` 寫入） |
| `variant` | `'primary'`（預設）、`'success'`、`'warning'`、`'danger'`、`'info'`、`'neutral'`（也可由 `TimeGrid.VARIANTS` 取得） |
| `ghost` | `true` 時畫成半透明虛線的預覽，另成一層疊在一般項目上方，不參與一般項目的分道，也不阻擋選取 |
| `draggable` | `false` 時此項目不可移動（`editable` 為 true 也一樣） |
| `data` | 任意附加資料，原樣隨回呼傳回 |

`start` / `end` 的判讀規則：

- 字串先比對時段 key，找不到才當作 `'HH:mm'` 時間。
- `end` 為時段 key 時代表「到該時段開始為止」，所以 `{ start: 's1', end: 's3' }` 佔 `s1`、`s2`；`start` 與 `end` 相同時佔一個時段。
- 時間形式取所有與 `[start, end)` 重疊的時段；超出可見範圍的部分會被裁切，但無障礙標籤仍顯示原本的起訖時間。
- 找不到的欄、時段，或完全落在範圍外／空檔內的項目不會畫出，並以一則 `console.warn` 彙整回報；同一個問題只回報一次。這些項目仍保留在 `getItems()` 的結果中，之後補上欄或時段就會出現。

### 時段範圍物件

`onSelect`、`getSelection()`、`onItemMove` 的 `from` / `to` 都用同一種形狀：

```js
{
    column: 'roomB',            // 欄 key（原值）
    start: '09:00',             // 第一個時段的 key
    end: '10:30',               // 結束邊界（不含）：下一個時段的 key；範圍到最後一個時段時為其結束時間 'HH:mm'
    startTime: '09:00',         // 第一個時段的開始時間
    endTime: '10:30',           // 最後一個時段的結束時間
    slots: ['09:00', '09:30', '10:00'] // 範圍內每個時段的 key
}
```

`start` / `end` 的語意與項目相同，所以選取結果可以直接拿來新增項目：`grid.addItem({ id, title, column: range.column, start: range.start, end: range.end })`。`getSelection()` 另外多一個 `confirmed`（鍵盤選取尚未按 Enter 確認時為 `false`）。

## 方法

| 方法 | 說明 |
|---|---|
| `mount(containerOrSelector)` | 掛載到容器（元素或 CSS 選擇器），回傳 `this`；開始監聽語系切換並啟動目前時間線計時器 |
| `destroy()` | 移除 DOM、所有 document/window 監聽、計時器與 requestAnimationFrame；可重複呼叫，之後呼叫其他方法不會丟錯 |
| `setItems(items)` | 以新陣列取代全部項目 |
| `addItem(item)` | 新增一個項目（id 已存在時略過並警告，修改請用 `updateItem`） |
| `updateItem(id, patch)` | 以淺層合併更新項目（`patch.id` 會被忽略），只重排受影響的欄 |
| `removeItem(id)` | 移除項目 |
| `getItems()` | 回傳所有項目的淺拷貝陣列（含未能放置的項目；`data` 為同一參考） |
| `setColumns(columns)` | 更換欄定義並重建表格，焦點與選取盡量依 key 保留 |
| `setSlots(slots)` | 更換明確時段；傳 `null` 改回使用 `timeRange` |
| `setTimeRange(range)` | 改用 `{ start, end, step }` 產生時段（同時清除明確時段） |
| `getSelection()` | 目前的選取範圍（含 `confirmed`），沒有時為 `null` |
| `clearSelection()` | 清除選取（不觸發回呼） |
| `focusCell(column, slot)` | 把焦點移到指定欄 key、時段 key 的儲存格並捲入可視範圍 |
| `scrollToSlot(slotKey)` | 捲動使該時段位於最上方（未設定 `height` 時改捲動頁面） |
| `snapshot()` | 取得內部互動狀態的深拷貝：`{ lifecycle, mode, selection, move }`，`mode` 為 `idle`／`dragging`／`grabbed`／`pending` |

除了 `getItems()`、`getSelection()`、`snapshot()` 回傳資料、`destroy()` 沒有回傳值以外，其餘方法都回傳 `this`，可串接。

## 回呼

| 回呼 | 觸發時機 |
|---|---|
| `onSelect(range)` | 指標在空白格按下、拖曳（限同一欄，遇到被一般項目佔用的格子就停）後放開；或鍵盤選取按 Enter 確認時 |
| `onItemMove({ item, from, to })` | 拖放或鍵盤放下到**不同**位置時。回傳 `false`、`Promise<false>`、丟出錯誤或 Promise 被拒絕，都視為拒絕，項目放回原位；其他值（含 `undefined`）視為同意，元件才更新該項目的 `column` / `start` / `end`。未提供此回呼時一律同意 |
| `onItemClick(item, event)` | 點擊項目，或項目取得焦點時按 Enter（不可移動的項目按 Space 也會觸發）。`item` 為淺拷貝 |
| `onCellClick({ column, slot, startTime, endTime }, event)` | 點擊儲存格的空白處（不在項目上），或在空白格按 Enter |

補充：

- 可選取時，單擊空白格會先觸發 `onSelect`（一個時段）再觸發 `onCellClick`；拖曳跨越多格時只觸發 `onSelect`。
- 移動期間（等待 `onItemMove` 的 Promise）表格標上 `aria-busy="true"`，仍可瀏覽與選取，但不能再拿起其他項目；若期間呼叫端自己用 `setItems` / `updateItem` / `removeItem` 改了該項目，元件不會再套用這次移動，以呼叫端的資料為準。
- 移動後項目保留原本的寫法：用時段 key 的仍寫 key；用時間的會整段平移（保留長度與非整點的起訖），平移後對不上目標時段時才對齊時段邊界。
- 拖放結束後瀏覽器送出的那一次 click 會被忽略，不會誤觸 `onItemClick` / `onCellClick`。

## 鍵盤操作

表格使用 roving tabindex：整張表只有一個 Tab 停駐點（目前的儲存格），項目按鈕為 `tabindex="-1"`。

在儲存格上：

| 按鍵 | 動作 |
|---|---|
| ↑ ↓ ← → | 移到相鄰儲存格 |
| Home / End | 同一列的第一欄／最後一欄 |
| Ctrl + Home / Ctrl + End | 整張表的第一格／最後一格 |
| PageUp / PageDown | 上／下移 5 個時段 |
| Enter | 格子被項目覆蓋時：焦點移到第一個項目。空白格：觸發 `onCellClick`；可選取時同時開始選取這一格 |
| Shift + ↑ ↓ / Shift + PageUp PageDown | 可選取時：從目前格子開始或延伸選取（限同一欄，遇到被佔用的格子就停） |
| Enter（選取中） | 確認選取 → `onSelect` |
| Escape | 取消選取 |

未確認的鍵盤選取在一般方向鍵移動時會自動放棄。

在項目上：

| 按鍵 | 動作 |
|---|---|
| Enter | `onItemClick` |
| Space | 可移動時拿起項目（進入移動模式）；不可移動時為按鈕原生行為（觸發 `onItemClick`） |
| ← → | 同一格內有多個並排項目時切換到左／右一個；已在最邊時移到相鄰儲存格 |
| ↑ ↓ Home End PageUp PageDown | 以進入項目時的儲存格為起點移動到其他儲存格 |
| Escape | 回到進入項目時的儲存格 |

移動模式（拿起後）：

| 按鍵 | 動作 |
|---|---|
| ↑ ↓ | 落點上／下移一個時段 |
| ← → | 落點移到左／右一欄 |
| PageUp / PageDown | 落點上／下移 5 個時段 |
| Home / End | 落點移到第一欄／最後一欄 |
| Enter | 放下 → `onItemMove`（落點與原位相同時視為取消） |
| Escape、Tab 或焦點離開表格 | 取消移動 |

指標拖曳中按 Escape 或發生 `pointercancel` 也會取消；拖曳到捲動容器邊緣會自動捲動。

## 無障礙（ARIA）

- 表格為 `role="grid"`，帶 `aria-label`、`aria-rowcount`（時段數 + 1）、`aria-colcount`（欄數 + 1）；可選取時加 `aria-multiselectable="true"`，`readonly` 時加 `aria-readonly="true"`，等待 `onItemMove` 時加 `aria-busy="true"`。
- 第一列為欄頭（`role="columnheader"`，左上角為時段欄的欄頭），每一列開頭是時段標籤（`role="rowheader"`），其餘為 `role="gridcell"`；列與格帶 `aria-rowindex` / `aria-colindex`。可選取時每格帶 `aria-selected`。
- 項目是格子裡的 `<button type="button">`，`aria-label` 由 Locale 組成「標題（副標），欄，時間」，ghost 項目另加「（預覽）」。可移動的項目帶 `aria-grabbed`（拿起時為 `true`）；此屬性在 ARIA 1.1 已不建議使用，實際的操作回饋以 live region 播報為主。
- `nowIndicator` 開啟時，今天的欄頭加 `aria-current="date"`；時間線、落點預覽與焦點框皆為 `aria-hidden`。
- 選取開始／變更／確認／取消、拿起、落點變更、移動中、移動成功／被拒絕／取消，都寫入表格外的 `aria-live="polite"` 區域；連續快速的變更會節流（約 250ms 內只播最後一則）。
- 焦點可見：儲存格取得焦點時畫出 `--cl-border-focus` 框；項目取得焦點時加外框並提到最上層。

## 樣式與版面

- 所有顏色、圓角、字級皆為 `--cl-*` token；變化色對應 `--cl-{variant}` 與 `--cl-{variant}-light`，`neutral` 用 `--cl-grey` / `--cl-bg-secondary`。
- 可用的 class（供外部選取，勿依賴其內部樣式）：`b4a-timegrid`、`__scroller`、`__grid`、`__header`、`__corner`、`__colheader`、`__row`、`__slot`、`__cell`、`__item`、`__item--ghost`、`__drop`、`__now`、`__focus`、`__live`。儲存格帶 `data-column` / `data-slot`，項目帶 `data-item-id` / `data-variant`，ghost 項目帶 `data-ghost="true"`。
- 每個時段列高為 `slotHeight`（不依時段長短伸縮）；欄寬平均分配且不小於 `columnMinWidth`。

## 範例：會議室單日預約

```html
<!DOCTYPE html>
<html lang="zh-TW" data-theme="light">
<head>
    <meta charset="utf-8">
    <link rel="stylesheet" href="../../theme.css">
</head>
<body>
    <div id="booking"></div>
    <script type="module">
        import { TimeGrid } from './index.js';

        const today = new Date();
        const date = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;

        const grid = new TimeGrid({
            columns: [
                { key: 'roomA', label: '會議室 A', sublabel: '8 人', date },
                { key: 'roomB', label: '會議室 B', sublabel: '12 人', date },
                { key: 'roomC', label: '會議室 C', sublabel: '4 人', date }
            ],
            timeRange: { start: '08:00', end: '18:00', step: 30 },
            height: 480,
            selectable: true,
            editable: true,
            nowIndicator: true,
            items: [
                { id: 'b1', column: 'roomA', start: '09:00', end: '10:30', title: '專案週會', subtitle: '王小明', variant: 'primary' },
                { id: 'b2', column: 'roomA', start: '10:00', end: '11:00', title: '供應商來訪', variant: 'warning' },
                { id: 'b3', column: 'roomB', start: '13:00', end: '15:00', title: '客戶簡報', variant: 'success', draggable: false }
            ],
            onSelect: (range) => {
                // 以 ghost 預覽新的預約，實際存檔後再改成一般項目
                grid.removeItem('draft');
                grid.addItem({ id: 'draft', title: '新預約', ghost: true, column: range.column, start: range.start, end: range.end });
            },
            onItemMove: async ({ item, to }) => {
                const response = await fetch(`/api/bookings/${encodeURIComponent(item.id)}`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ room: to.column, start: to.startTime, end: to.endTime })
                });
                return response.ok; // false → 放回原位
            },
            onItemClick: (item) => console.log('開啟預約', item.id)
        }).mount('#booking');

        grid.scrollToSlot('09:00');
    </script>
</body>
</html>
```

## 限制與注意事項

- 不支援跨午夜的時段（時間範圍為 `00:00`～`24:00`）與拖曳改變項目長度。
- 列高固定為 `slotHeight`，不依時段長短比例伸縮；空檔（明確時段之間的間隔）不佔列。
- 同一格重疊的項目很多時每道會很窄，元件不會收合成「+N」。
- `height` 為 `null` 時捲動容器只做水平捲動，欄頭的黏附只在設定 `height` 時有效。
- 觸控裝置上，可移動的項目設有 `touch-action: none` 以便拖放；在空白格上拖曳會捲動頁面（點一下仍可選取一個時段）。
- 目前時間線每分鐘更新，最多落後 1 分鐘；只在 `columns[].date` 等於 `now()` 當天的欄出現。
