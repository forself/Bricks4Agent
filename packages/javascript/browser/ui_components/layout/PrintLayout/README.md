# PrintLayout

列印版面（容器元件）。把頁面中的一個區塊整理成可列印的版面：標題、副標題、自訂頁首／頁尾、列印時間，並提供 `print()`——**只印這個區塊**，不破壞、不搬動宿主頁面的 DOM。適用報表、清單、單據、會議紀錄等需要列印的畫面。

## 建構

```js
import { PrintLayout } from './index.js';

const layout = new PrintLayout({
    title: '',               // 標題（<h2>）
    subtitle: '',            // 副標題
    header: null,            // 標題下方的自訂頁首：Node、B4A 元件（有 .element）或文字
    footer: null,            // 頁尾：Node、B4A 元件或文字
    content: null,           // 主要內容：Node、B4A 元件或文字；也可自行掛到 getContentElement()
    pageSize: 'A4',          // 'A4' | 'A3' | 'Letter' | 'Legal'（不分大小寫）
    orientation: 'portrait', // 'portrait' | 'landscape'
    margin: '15mm',          // '0' | '10mm' | '15mm' | '20mm' | '25mm'（PrintLayout.css 的固定組合）
    showPrintedAt: true,     // 顯示列印時間（依 Locale 格式；每次 print() 時更新；只出現在紙本與預覽畫面）
    preview: false,          // 螢幕上以實際紙張寬度、邊界內距與陰影預覽列印結果
    printButton: false,      // 顯示「列印」按鈕（呼叫 print()）
    beforePrint: null,       // (layout) => void | false；回傳 false 取消這次列印
    afterPrint: null         // (layout) => void；列印結束（afterprint 或備援計時器）後呼叫
});
layout.mount('#report');
```

- 文字型的 `header`／`footer`／`content` 一律當純文字輸出，不解析 HTML。
- 列印時間在尚未列印的一般螢幕畫面沒有意義，所以 `preview: false` 時只在紙本上出現（`PrintLayout.css` 的 `@media screen` 規則隱藏）；`preview: true` 時螢幕上也顯示，方便預覽。
- 不支援的 `pageSize`／`orientation`／`margin` 會 `console.warn` 並退回預設值（A4、portrait、15mm）。
- 靜態屬性：`PrintLayout.PAGE_SIZES`、`PrintLayout.ORIENTATIONS`、`PrintLayout.MARGINS`、`PrintLayout.CLEANUP_FALLBACK_MS`（預設 60000）。

## 方法

| 方法 | 回傳 | 說明 |
|---|---|---|
| `print()` | `this` | 只列印這個版面。需要先 `mount` 到文件中（未掛載時警告並略過） |
| `setContent(node)` | `this` | 以 Node、B4A 元件或文字取代主要內容；`null` 清空 |
| `getContentElement()` | `HTMLElement` | 主要內容容器，可直接把其他元件 `mount` 進來 |
| `setTitle(title)` | `this` | 更新標題；空字串移除標題 |
| `mount(container)` | `this` | 掛載到元素或 CSS 選擇器 |
| `destroy()` | `void` | 移除 DOM、Icon、監聽與計時器；列印中呼叫會立即收掉列印標記（不呼叫 `afterPrint`）；樣式表 link 在最後一個實例 destroy 時才移除。可重複呼叫，之後呼叫其他方法不會拋錯 |

## 事件（callback）

| Callback | 參數 | 觸發時機 |
|---|---|---|
| `beforePrint` | `(layout)` | `print()` 開始、標記頁面之前；回傳 `false` 取消列印。可在這裡展開收合區塊、載入完整資料 |
| `afterPrint` | `(layout)` | 列印結束：收到 `afterprint`、備援計時器到期、或另一個 PrintLayout 開始列印時。`destroy()` 中斷列印時不呼叫 |

## print() 如何只印這個區塊

整個流程不注入 `<style>`，符合嚴格 CSP：

1. 在 `<html>` 加 `data-b4a-printing`，版面根元素加 `data-b4a-print-target`（另有 `data-b4a-page`，例如 `a4-portrait-15mm`），祖先鏈（含 `<body>`）加 `data-b4a-print-ancestor`。
2. 同目錄 `PrintLayout.css` 的 `@media print` 規則依這些標記：隱藏祖先鏈上的其他兄弟節點（頁首、側欄、portal 到 body 的浮層），祖先鏈改 `display: contents`（捲動容器、固定高度、`transform` 都不再裁切內容），並解除 SPA 常見的 `html, body { height: 100%; overflow: hidden }`。
3. 呼叫 `window.print()`。
4. 收到 `afterprint` 時移除所有標記；瀏覽器沒送 `afterprint` 時，`print()` 返回 `CLEANUP_FALLBACK_MS` 毫秒後由備援計時器移除。標記只影響列印樣式，螢幕畫面不受影響。

同一時間只會有一個版面在列印：另一個實例呼叫 `print()` 時，前一個的標記會先被收掉。

### 紙張大小、方向與邊界

嚴格 CSP 不能動態產生 `@page`，所以 `PrintLayout.css` 內預先寫好固定組合的具名頁面（4 種紙張 × 2 種方向 × 5 種邊界 = 40 個 `@page b4a-print-…`），列印期間以 `data-b4a-page` 選擇其一。只有經過 `print()` 的列印會套用這些頁面設定，使用者直接按 Ctrl+P 時不受影響。

限制：

- 邊界只支援 `0`、`10mm`、`15mm`、`20mm`、`25mm`；其他值退回 `15mm`。需要別的邊界時可設 `margin: '0'`，再自行在內容加內距。
- 具名頁面（CSS `page` 屬性）需要瀏覽器支援（Chromium 系、Firefox 110+）；不支援時紙張與邊界改由列印對話框決定，但仍然只印這個區塊。
- 頁首、頁尾各印一次（在第一頁頂端與最後一頁底端），不會在每一頁重複。
- 樣式表以 `<link>` 非同步載入；在樣式表載入前呼叫 `print()` 會印出整頁。一般由使用者點擊觸發時早已載入完成。
- 深色主題下，列印與預覽的紙張內會把文字、背景、框線 token 換成淺色紙張上可讀的值。

### 樣式表載入

`PrintLayout.css` 以同源 `<link rel="stylesheet" id="b4a-print-layout-styles">` 載入（做法同 `layout/DataTable`）。所有實例共用一個 link 並引用計數，最後一個實例 `destroy()` 才移除；宿主頁面自行放置的同 id link 不會被移除。link 被宿主移除時，下一次 `print()` 會補回。

## 鍵盤與無障礙

- 沒有浮出層，不掛 `document` 監聽；`window` 上只有 `locale-changed`（掛載期間）與 `afterprint`（列印期間）。
- 「列印」按鈕是原生 `<button type="button">`，Tab 可到達、Enter／Space 觸發；文字標籤取自 Locale，印表機圖示（Canvas）為 `aria-hidden="true"`。工具列在任何列印中都不會印出。
- 標題為 `<h2>`，頁首／頁尾使用 `<header>`／`<footer>`。
- `Locale.setLang()` 會即時更新按鈕文字與列印時間格式。

## Locale（namespace：`printLayout`）

`print`、`printedAt`（`{time}`）。字串由同目錄 `locale.js` 註冊 zh-TW 與 en。

## 可執行範例

把下列內容存成本目錄的 `example.html`，以 HTTP 伺服器開啟（例如在 repo 根目錄執行 `python -m http.server 8124`；ES module 與樣式表 link 不能用 `file://`）。

```html
<!DOCTYPE html>
<html lang="zh-TW" data-theme="light">
<head>
    <meta charset="UTF-8">
    <title>PrintLayout 範例</title>
    <link rel="stylesheet" href="../../theme.css">
</head>
<body>
    <nav>這段導覽列不會被印出</nav>
    <main>
        <div id="report"></div>
    </main>
    <script type="module">
        import { PrintLayout } from './index.js';

        const layout = new PrintLayout({
            title: '會議室使用月報',
            subtitle: '2026 年 9 月',
            header: '資料來源：預約系統',
            footer: '總務組 製表',
            orientation: 'landscape',
            margin: '10mm',
            preview: true,
            printButton: true,
            beforePrint: () => console.log('準備列印'),
            afterPrint: () => console.log('列印結束')
        }).mount('#report');

        const table = document.createElement('table');
        for (const [room, hours] of [['A 室', 42], ['B 室', 35], ['C 室', 18]]) {
            const row = table.insertRow();
            row.insertCell().textContent = room;
            row.insertCell().textContent = `${hours} 小時`;
        }
        layout.getContentElement().appendChild(table);
    </script>
</body>
</html>
```
