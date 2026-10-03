# IssueList

驗證／衝突問題清單（複合元件）。把表單驗證、資料衝突、匯入檢查等結果列成清單，使用者可以依嚴重度篩選，並點選某一列跳回問題來源。每筆問題的 `source` 由呼叫端自訂（欄位名稱、列號、節點 id…），元件不解讀，只在 `onSelect` 時原樣交回。

- 嚴重度同時以圖示、文字標籤與顏色呈現，不單靠顏色。
- 資料變動後以 polite live region 播報摘要（節流），螢幕報讀器使用者不必離開目前位置也知道結果。
- 全部以 `createElement` + `textContent` 建構、樣式走 CSSOM，符合嚴格 CSP；資料一律當文字輸出。

## 建構

```js
import { IssueList } from './index.js';

const list = new IssueList({
    issues: [],              // 問題陣列：[{ id, severity: 'error'|'warning'|'info', title, message?, source?, meta? }]
    sort: 'severity',        // 'severity'：依 error → warning → info 穩定排序；'none'：維持輸入順序
    groupBy: null,           // null：單一清單；'severity'：依嚴重度分組並顯示組標題
    showSummary: true,       // 顯示各嚴重度計數；點擊計數即篩選
    filter: null,            // null：顯示全部；['error', ...]：只顯示指定嚴重度
    dismissible: false,      // 每列顯示「忽略」按鈕（在列上按 Delete／Backspace 亦可）
    emptyText: null,         // 沒有問題時的文字；null 使用 Locale issueList.empty
    maxHeight: null,         // 清單最大高度（數字為 px，或 CSS 長度字串如 '50vh'）；超過時捲動
    ariaLabel: null,         // 清單的無障礙名稱；null 使用 Locale issueList.label
    announceDelay: 1000,     // live region 播報節流間隔（ms）
    onSelect: null,          // (issue) => void：使用者點選或按 Enter／Space
    onDismiss: null          // (issue) => void：使用者忽略某列（該列已從清單移除）
});
list.mount('#issues');
```

### issue 物件

| 欄位 | 型別 | 說明 |
|---|---|---|
| `id` | `string \| number` | 識別碼；`addIssue`／`removeIssue` 以它比對（`'1'` 與 `1` 視為相同） |
| `severity` | `'error' \| 'warning' \| 'info'` | 嚴重度；其他值視為 `info` |
| `title` | `string` | 標題 |
| `message` | `string?` | 說明文字（保留換行） |
| `source` | `any?` | 不透明的定位資訊，`onSelect` 原樣交回 |
| `meta` | `string?` | 列右側的小字（例如欄位名稱、列號） |

- `sort: 'severity'` 是穩定排序：同一嚴重度內維持輸入順序。
- 摘要計數永遠是全部問題的數量，不受篩選影響。
- `filter` 的空陣列、無效值或三種全選都等同 `null`（全部）。

## 方法

| 方法 | 回傳 | 說明 |
|---|---|---|
| `setIssues(issues)` | `this` | 取代全部問題。同 id 的問題保留焦點與 Tab 位置；摘要有變動時節流播報 |
| `addIssue(issue)` | `this` | 新增一筆；`id` 已存在時就地取代該筆 |
| `removeIssue(id)` | `this` | 以 `id` 移除（不觸發 `onDismiss`） |
| `clear()` | `this` | 清空全部問題 |
| `getIssues()` | `object[]` | 目前的問題（輸入順序；新陣列，元素為呼叫端原物件） |
| `setFilter(severities)` | `this` | 設定篩選；`null`、空陣列或三種全選＝全部，也接受單一字串 |
| `getFilter()` | `string[] \| null` | 目前的篩選 |
| `focusFirst()` | `this` | 聚焦第一個可見列（沒有列時不動作） |
| `snapshot()` | `object` | 狀態機快照：`{ lifecycle, filter }` |
| `mount(container)` | `this` | 掛載到元素或 CSS 選擇器 |
| `destroy()` | `void` | 移除 DOM、`locale-changed` 監聽與播報計時器；可重複呼叫，之後呼叫其他方法不會拋錯 |

靜態屬性：`IssueList.SEVERITIES` = `['error', 'warning', 'info']`。

## 事件（callback）

| Callback | 參數 | 觸發時機 |
|---|---|---|
| `onSelect` | `issue`（呼叫端原物件） | 點擊列、在列上按 Enter 或 Space |
| `onDismiss` | `issue`（呼叫端原物件） | 點「忽略」按鈕或在列上按 Delete／Backspace；呼叫時該列已移除 |

程式呼叫 `setIssues`／`removeIssue`／`clear` 不會觸發 callback。

## 鍵盤與無障礙

| 按鍵 | 行為 |
|---|---|
| Tab | 整份清單只佔一個停駐點（roving tabindex）；`dismissible` 時下一個停駐點是目前列的「忽略」按鈕 |
| ↑ ／ ↓ | 移到上一列／下一列（分組時可跨組） |
| Home ／ End | 移到第一列／最後一列 |
| Enter | 觸發 `onSelect`（keydown） |
| Space | 觸發 `onSelect`（keyup，與原生按鈕一致；keydown 只防止捲動） |
| Delete ／ Backspace | `dismissible` 時忽略目前列，焦點移到同位置的下一列；最後一筆被忽略時移到「全部」計數按鈕 |

- 每列是 `role="button"`；嚴重度圖示 `aria-hidden="true"`，另有可見的文字標籤（錯誤／警告／提示）。
- 摘要列為 `role="group"`（`aria-label` 取自 Locale），每個計數是帶 `aria-pressed` 的切換按鈕，無障礙名稱例如「錯誤 2」：點「錯誤」只看錯誤，再點一次或點「全部」恢復。
- 清單外層為 `role="group"` 並有 `aria-label`；分組時每組的 `<ul>` 以 `aria-labelledby` 指向組標題。
- live region（`role="status"`、`aria-live="polite"`、`aria-atomic="true"`）在 `setIssues`／`addIssue`／`removeIssue`／`clear`／忽略之後，依 `announceDelay` 節流播報摘要（例如「共 3 項：錯誤 1、警告 1、提示 1」）；只播報與上次不同的內容，初始資料與語言切換本身不播報。
- `Locale.setLang()` 會即時更新所有文字。
- 在列上按住修飾鍵（Ctrl／Alt／⌘）時不攔截，保留瀏覽器快捷鍵。

## Locale（namespace：`issueList`）

`label`、`empty`、`filteredEmpty`、`all`、`summaryLabel`、`severity.error`、`severity.warning`、`severity.info`、`countLabel`（`{label}`、`{count}`）、`groupHeading`（`{label}`、`{count}`）、`dismiss`（`{title}`）、`announceSummary`（`{total}`、`{error}`、`{warning}`、`{info}`）、`announceEmpty`。字串由同目錄 `locale.js` 註冊 zh-TW 與 en。

## 可執行範例

把下列內容存成本目錄的 `example.html`，以 HTTP 伺服器開啟（例如在 repo 根目錄執行 `python -m http.server 8124`，再開 `http://localhost:8124/packages/javascript/browser/ui_components/common/IssueList/example.html`；ES module 不能用 `file://`）。

```html
<!DOCTYPE html>
<html lang="zh-TW" data-theme="light">
<head>
    <meta charset="UTF-8">
    <title>IssueList 範例</title>
    <link rel="stylesheet" href="../../theme.css">
</head>
<body>
    <label>會議室 <input id="room" value="A 室"></label>
    <label>主持人 <input id="host"></label>
    <div id="issues"></div>
    <script type="module">
        import { IssueList } from './index.js';

        const list = new IssueList({
            issues: [
                { id: 'room-overlap', severity: 'error', title: '會議室時段重疊', message: 'A 室 10:00–11:00 已被預約', source: { field: 'room' } },
                { id: 'host-missing', severity: 'error', title: '缺少主持人', source: { field: 'host' } },
                { id: 'size', severity: 'warning', title: '參與人數超過建議上限', meta: '上限 8 人' },
                { id: 'note', severity: 'info', title: '尚未填寫備註' }
            ],
            groupBy: 'severity',
            dismissible: true,
            maxHeight: 320,
            onSelect: (issue) => document.getElementById(issue.source?.field)?.focus(),
            onDismiss: (issue) => console.log('忽略', issue.id)
        }).mount('#issues');

        // 重新驗證後更新（同 id 的問題保留焦點）
        setTimeout(() => list.removeIssue('host-missing'), 3000);
    </script>
</body>
</html>
```
