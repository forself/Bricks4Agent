# ApprovalTimeline

審核歷程（原子元件）。以垂直時間軸呈現一連串審核步驟，適用各種通用簽核流程：採購申請、請假單、用印申請、報修單等。每個步驟顯示標題、處理人、時間、狀態、意見與附件。

- 語意 `<ol>`；狀態為 `current` 的步驟帶 `aria-current="step"`。
- 狀態一律有文字標籤（Locale），符號與顏色只是輔助。
- 附件網址一律經 `sanitizeUrl()`；不安全的網址（例如 `javascript:`、`data:`）不產生連結，只顯示檔名。
- 全部以 `createElement` + `textContent` 建構、樣式走 CSSOM，符合嚴格 CSP。

## 建構

```js
import { ApprovalTimeline } from './index.js';

const timeline = new ApprovalTimeline({
    steps: [],                 // 步驟：[{ id, title, actor?, time?, status, comment?, attachments?: [{ name, href }] }]
    compact: false,            // 緊湊模式：較小的狀態符號與間距
    showTimestamps: true,      // 顯示時間
    formatTime: null,          // (date: Date, step) => string；null 使用 Intl.DateTimeFormat(Locale 語言, { dateStyle: 'medium', timeStyle: 'short' })
    reverse: false,            // true：最新的步驟在最上面（<ol reversed>）
    openLinksInNewTab: false,  // 附件連結在新分頁開啟：加 target="_blank" 與 rel="noopener noreferrer"
    emptyText: null,           // 沒有步驟時的文字；null 使用 Locale approvalTimeline.empty
    ariaLabel: null,           // 清單的無障礙名稱；null 使用 Locale approvalTimeline.label
    onStepClick: null          // (step) => void；提供時步驟標題成為按鈕
});
timeline.mount('#history');
```

### step 物件

| 欄位 | 型別 | 說明 |
|---|---|---|
| `id` | `string \| number` | 識別碼（輸出為 `data-step-id`） |
| `title` | `string` | 步驟名稱，例如「部門主管審核」 |
| `actor` | `string?` | 處理人 |
| `time` | `string \| Date \| number?` | ISO 字串、`Date` 或毫秒；無法解析的字串原樣顯示 |
| `status` | `string` | `pending`（待審核）、`current`（審核中）、`approved`（已核准）、`rejected`（已駁回）、`returned`（已退回）、`skipped`（已略過）、`cancelled`（已取消）；其他值視為 `pending` |
| `comment` | `string?` | 審核意見（保留換行） |
| `attachments` | `{ name, href }[]?` | 附件；`href` 經 `sanitizeUrl()` |

## 方法

| 方法 | 回傳 | 說明 |
|---|---|---|
| `setSteps(steps)` | `this` | 以新陣列取代全部步驟並重繪 |
| `getSteps()` | `object[]` | 目前的步驟（原始順序，不受 `reverse` 影響；新陣列，元素為呼叫端原物件） |
| `mount(container)` | `this` | 掛載到元素或 CSS 選擇器 |
| `destroy()` | `void` | 移除 DOM 與 `locale-changed` 監聽；可重複呼叫，之後呼叫其他方法不會拋錯 |

靜態屬性：`ApprovalTimeline.STATUSES` 列出所有狀態值。

## 事件（callback）

| Callback | 參數 | 觸發時機 |
|---|---|---|
| `onStepClick` | `step`（呼叫端原物件） | 點擊步驟標題按鈕，或在按鈕上按 Enter／Space |

## 鍵盤與無障礙

- 沒有 `onStepClick` 時沒有任何互動元素，不佔 Tab 停駐點（附件連結除外）。
- 提供 `onStepClick` 時，每個步驟標題是原生 `<button type="button">`：Tab 可到達，Enter／Space 觸發。
- 整體是 `<ol aria-label="審核歷程">`；`reverse: true` 時加 `reversed` 屬性。
- 目前步驟 `<li aria-current="step">`；並行審核時多個 `current` 步驟都會標上。
- 狀態符號與連接線在 `aria-hidden="true"` 的軌道內；狀態以可見文字標籤呈現（例如「已駁回」），`cancelled` 另加刪除線。
- 時間輸出 `<time datetime="ISO">`。
- 附件清單 `<ul aria-label="附件">`；`openLinksInNewTab: true` 時每個連結另有視覺隱藏的「（在新分頁開啟）」提示。
- `Locale.setLang()` 會即時重繪（狀態文字、時間格式），並保留標題按鈕的焦點。

## Locale（namespace：`approvalTimeline`）

`label`、`empty`、`attachments`、`newTab`、`status.pending`、`status.current`、`status.approved`、`status.rejected`、`status.returned`、`status.skipped`、`status.cancelled`。字串由同目錄 `locale.js` 註冊 zh-TW 與 en。

## 可執行範例

把下列內容存成本目錄的 `example.html`，以 HTTP 伺服器開啟（例如在 repo 根目錄執行 `python -m http.server 8124`；ES module 不能用 `file://`）。

```html
<!DOCTYPE html>
<html lang="zh-TW" data-theme="light">
<head>
    <meta charset="UTF-8">
    <title>ApprovalTimeline 範例</title>
    <link rel="stylesheet" href="../../theme.css">
</head>
<body>
    <div id="history"></div>
    <script type="module">
        import { ApprovalTimeline } from './index.js';

        const timeline = new ApprovalTimeline({
            steps: [
                { id: 1, title: '申請人送出', actor: '王小明', time: '2026-09-01T09:00:00+08:00', status: 'approved', comment: '採購會議室投影機一台' },
                { id: 2, title: '部門主管審核', actor: '李主任', time: '2026-09-02T14:30:00+08:00', status: 'returned', comment: '請補上第二家報價',
                  attachments: [{ name: '報價單.pdf', href: '/files/quote-1.pdf' }] },
                { id: 3, title: '部門主管複審', actor: '李主任', status: 'current' },
                { id: 4, title: '總務核銷', status: 'pending' }
            ],
            reverse: true,
            openLinksInNewTab: true,
            onStepClick: (step) => console.log('檢視步驟', step.id)
        }).mount('#history');

        // 狀態更新後整批重設
        setTimeout(() => {
            const steps = timeline.getSteps().map((step) => (step.id === 3 ? { ...step, status: 'approved', time: new Date() } : step));
            steps[3] = { ...steps[3], status: 'current' };
            timeline.setSteps(steps);
        }, 3000);
    </script>
</body>
</html>
```
