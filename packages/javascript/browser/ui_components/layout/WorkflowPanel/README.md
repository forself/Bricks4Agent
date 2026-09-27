# WorkflowPanel

流程歷程時間軸。把一串「某個階段、何時、由哪個單位／人員處理」的紀錄依時間排序，畫成 S 形的節點列；最後一筆標示為「目前」，可再附上一個半透明的「下一階段」節點。版面依容器寬度自動決定每列節點數（ResizeObserver），極窄時改為垂直單欄。

> **內建 13 個階段是舊版預設值。** `WorkflowPanel.STAGES` 與固定欄位名（`StageName`、`DateTime`、`UnitName`、`UserName`、`nextStage.NextUnit`）是為了相容既有系統而保留，行為不會再改動。**新專案請傳入自己的 `stages`（通常搭配 `replaceStages: true`）與 `fieldMap`**，不要依賴內建階段的名稱與顏色。兩個選項都沒給時，輸出與舊版逐字相同。

## 建構

```js
import { WorkflowPanel } from './WorkflowPanel.js';

const panel = new WorkflowPanel({
    data: [],              // 歷程紀錄陣列；依時間欄位由舊到新排序後顯示
    itemsPerRow: 5,        // 每列節點數上限，夾在 3–7；實際數量依容器寬度自動縮減
    nextStage: null,       // 下一階段 { StageName, NextUnit }（鍵名可由 fieldMap 改）；null 不顯示
    showDetails: true,     // 保留欄位，目前未使用
    onNodeClick: null,     // (item, stage) => void；點擊歷程節點時呼叫，下一階段節點不可點
    stages: null,          // 自訂階段 { [階段鍵]: { name, icon, color } }，逐階段合併在內建階段之上（只影響本實例）
    replaceStages: false,  // 只有 true 生效：只用 stages，內建 13 階段不參與
    fieldMap: null         // 邏輯欄位 → 資料屬性名 { stageName, dateTime, unitName, userName, nextUnit }
});
panel.mount('#workflow-host');
```

### 資料欄位與 `fieldMap`

元件從每筆資料讀取下列邏輯欄位；`fieldMap` 用同樣的鍵指定你的屬性名，沒給的鍵沿用預設（舊版）名稱。只接受非空字串，其他值會被忽略。

| 邏輯鍵 | 預設屬性名 | 讀取位置 | 用途 |
|---|---|---|---|
| `stageName` | `StageName` | data 項目**與** `nextStage` | 階段鍵，用來查 `stages` / 內建階段 |
| `dateTime` | `DateTime` | data 項目 | 排序（`new Date(值)`）與顯示（`YYYY/MM/DD HH:mm`） |
| `unitName` | `UnitName` | data 項目 | 單位，與人員以 ` / ` 串接顯示 |
| `userName` | `UserName` | data 項目 | 人員 |
| `nextUnit` | `NextUnit` | `nextStage` | 下一階段的處理單位；沒有值時顯示 Locale 的 `pending` |

`WorkflowPanel.DEFAULT_FIELD_MAP` 是上表預設值的凍結物件。資料項目若帶有 truthy 的 `isNext`，會被當成「下一階段」樣式（舊版行為，不受 `fieldMap` 影響）。

### 自訂階段 `stages`

- 形狀：`{ [階段鍵]: { name?, icon?, color? } }`，階段鍵就是資料上 `stageName` 欄位的值。
- **合併（預設）**：逐階段、逐欄位合併在 `WorkflowPanel.STAGES` 之上。例如 `{ Create: { name: 'Draft' } }` 只改名稱，圖示與顏色沿用內建值；未列出的內建階段不受影響。
- **取代**（`replaceStages: true`）：只使用 `stages`；內建階段與未列出的階段一律走回退外觀。
- **回退外觀**（舊版行為）：未知階段顯示 `📋`、`var(--cl-grey)`，名稱為資料上的階段值；自訂階段缺 `name` 時顯示階段鍵，缺 `icon` / `color` 時用回退值（合併模式下先沿用同名內建階段的值）。
- `name`、`icon` 是呼叫端提供的文字，以 `textContent` 呈現，不會被當成 HTML。語系由呼叫端自行決定（內建階段名稱只有中文）。
- **`color` 必須是主題 token**（`var(--cl-*)`），這是元件庫的樣式規範。元件不會強制檢查是否為 token，但會拒絕任何可能跳出單一 CSS 宣告或載入外部資源的字串（分號、大括號、引號、反斜線、`/* */`、`!`、括號不成對、`url()` 等非色彩函式）；被拒絕時改用預設色並 `console.warn`。顏色只透過 `style` 寫入，從不進入 `innerHTML`。
- 正規化在建構時做一次；**永不修改 `WorkflowPanel.STAGES`**，不同實例互不影響。需要改階段時請重建實例。

## 靜態成員

| 成員 | 說明 |
|---|---|
| `WorkflowPanel.STAGES` | 內建 13 階段（舊版預設，勿修改；請改用實例的 `stages`） |
| `WorkflowPanel.DEFAULT_FIELD_MAP` | 預設欄位名（凍結） |
| `NODE_MIN_WIDTH` / `NODE_MAX_WIDTH` | 節點寬度下限 72px／上限 100px |
| `CONNECTOR_MIN_TOTAL` / `CONNECTOR_MAX_TOTAL` | 連接線含左右邊距的最小 40px／最大 76px |

## 方法

| 方法 | 回傳 | 說明 |
|---|---|---|
| `mount(container)` | `this` | 掛到元素或 CSS 選擇器對應的容器 |
| `setData(data)` | `this` | 更換歷程資料並重繪 |
| `setNextStage(nextStage)` | `this` | 更換（或以 `null` 移除）下一階段並重繪 |
| `destroy()` | `void` | 斷開 ResizeObserver、取消待執行的重排並移除 DOM；可重複呼叫，之後呼叫其他方法不會拋錯 |

## 回呼

`onNodeClick(item, stage)`

- `item`：呼叫端傳入的**原始資料物件**（不是元件內部的正規化結果）。
- `stage`：該節點使用的 `{ name, icon, color }`。未使用 `stages` 時就是 `WorkflowPanel.STAGES` 內的同一個物件（未知階段為臨時的回退物件）；使用 `stages` 時為合併後的物件，同一實例內重繪也維持同一個物件。
- 下一階段節點不綁定點擊。

## 版面與語系

- 依時間欄位由舊到新排序；奇數列反向排列形成 S 形，列與列之間以向下連接線銜接。
- 每列節點數 = `floor((面板內容寬 + 40) / 112)`（ResizeObserver 的 `contentRect.width`，不含面板 20px 內距），上限 `itemsPerRow`；少於 2 時改為垂直單欄（`.workflow-column`）。沒有 ResizeObserver 的環境固定使用 `itemsPerRow`。
- 最後一筆歷程顯示「目前」徽章；下一階段節點半透明，顯示「(待處理)」提示與下一單位。
- 介面文字走 `Locale`（命名空間 `workflowPanel`）：`currentBadge`（目前）、`pending`（下一單位未提供時）、`nextStageHint`（下一階段提示）。元件不監聽 `locale-changed`，切換語系後於下一次重繪（`setData`、`setNextStage` 或寬度變化）生效。
- 顏色全部來自主題 token，深色模式由 `[data-theme="dark"]` 的 token 值提供。

## 鍵盤與 ARIA

WorkflowPanel 是唯讀的顯示元件：沒有 ARIA 角色、沒有可聚焦元素，節點的 `onNodeClick` 只支援滑鼠／觸控點擊（舊版行為，為了維持既有 DOM 不變而未加入鍵盤操作）。若流程節點需要鍵盤操作，請在頁面上另外提供等效的按鈕或連結。所有文字（階段名稱、時間、單位、人員）都是一般文字節點，可被輔助技術讀取。

## DOM 結構（class）

`.workflow-panel` › `.workflow-row` › `.workflow-node` + `.workflow-connector`；列間的 `.workflow-vertical-connector`；垂直單欄時為 `.workflow-column` › `.workflow-node` + `.workflow-down-connector`。

## 範例

```html
<!DOCTYPE html>
<html lang="zh-TW">
<head>
    <meta charset="UTF-8">
    <link rel="stylesheet" href="../../theme.css">
</head>
<body>
    <div id="workflow-host"></div>
    <p id="detail"></p>

    <script type="module">
        import { WorkflowPanel } from './WorkflowPanel.js';

        // 任務流程：自訂階段與欄位名，不依賴內建 13 階段
        const stages = {
            queued:    { name: '排入佇列', icon: '📥', color: 'var(--cl-grey)' },
            drafting:  { name: '撰寫中',   icon: '✏️', color: 'var(--cl-primary)' },
            reviewing: { name: '審閱中',   icon: '🔍', color: 'var(--cl-warning)' },
            done:      { name: '完成',     icon: '✅', color: 'var(--cl-success)' }
        };

        const panel = new WorkflowPanel({
            data: [
                { step: 'queued',    at: '2026-09-01T09:00:00', team: '設計組', owner: '王小明' },
                { step: 'drafting',  at: '2026-09-01T10:30:00', team: '設計組', owner: '王小明' },
                { step: 'reviewing', at: '2026-09-02T14:00:00', team: '品保組', owner: '陳美玲' }
            ],
            nextStage: { step: 'done', assignee: '營運組' },
            stages,
            replaceStages: true,
            fieldMap: { stageName: 'step', dateTime: 'at', unitName: 'team', userName: 'owner', nextUnit: 'assignee' },
            onNodeClick: (item, stage) => {
                document.getElementById('detail').textContent = `${stage.name}：${item.team} / ${item.owner}`;
            }
        }).mount('#workflow-host');

        // 離開頁面前
        // panel.destroy();
    </script>
</body>
</html>
```
