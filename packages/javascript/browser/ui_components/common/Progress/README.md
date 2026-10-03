# Progress

進度指示器。提供線性條（`bar`，DOM）與圓環（`circle`，Canvas）兩種外觀，支援確定值、不確定（indeterminate）動畫，以及**多段堆疊（segments）**：把「完成／進行中／受阻」之類的組成畫在同一條（或同一圈）進度上，可選配圖例。分段模式預設關閉；未使用時輸出與舊版逐字相同。

## 建構

```js
import { Progress } from './Progress.js';

const progress = new Progress({
    value: 0,               // 目前數值（0–max）；單一數值模式使用
    max: 100,               // 最大值；分段的數值也是 max 的一部分
    variant: 'primary',     // 單一數值模式的色彩：'primary' | 'success' | 'warning' | 'danger'
    type: 'bar',            // 'bar'（線性，DOM）| 'circle'（圓環，Canvas）
    size: 'medium',         // 'small' | 'medium' | 'large'（bar 高 4/8/12px；circle 直徑 48/80/120px）
    showText: false,        // 顯示百分比文字；分段模式顯示各段合計
    indeterminate: false,   // 不確定動畫（bar：WAAPI；circle：rAF 旋轉）；分段模式忽略
    segments: null,         // 分段陣列 [{ value, variant?, color?, label? }]；給陣列即進入分段模式
    showLegend: false       // 分段模式下在圖形下方顯示圖例（標籤＋數值／百分比）
});
progress.mount('#host');   // 與 render('#host') 相同
```

### 分段物件

| 欄位 | 型別 | 說明 |
|---|---|---|
| `value` | `number` | 這一段的數值，是 `max` 的一部分。負數、非數字視為 0 |
| `variant` | `string` | `'primary'`、`'success'`、`'warning'`、`'danger'`、`'info'`、`'neutral'`（`--cl-grey`）。未指定或不認得時依序輪替上列六色，讓相鄰段落可區分 |
| `color` | `string` | 只接受**單一 token 參照**，例如 `'var(--cl-purple)'`；其他寫法（具名色、多個值、含分號等）一律忽略並改用 `variant` 的顏色 |
| `label` | `string` | 純文字名稱（`textContent`），用於圖例與 `aria-label`；未提供時用 Locale 的「區段 {index}」 |

規則：

- **截斷**：依陣列順序累加，累計值不會超過 `max`；超出的部分從後面的段落截掉（例如 `max: 100` 時 `[70, 50, 10]` 會畫成 70、30、0）。`max <= 0` 時每段都是 0%。
- **bar**：軌道內每段是一個絕對定位的 `.cl-progress-bar-segment`，依序由左往右堆疊；更新時重用既有元素，寬度變化會套用 `--cl-transition`。
- **circle**：Canvas 從 12 點鐘方向依序畫多段弧（端點平切，0% 的段落略過），顏色在繪製時經 `theme-bus` 的 `resolveTokens` 解析；主題變更（`data-theme`、`<html>` 的 style/class 變化或 `notifyThemeChange()`）與掛載後都會重繪。token 解析不到時使用 `FALLBACK_PAINT`。
- 分段一律為確定值：進入分段模式時停止 indeterminate 動畫，`setSegments(null)` 回到單一數值模式時恢復。
- 空陣列也是分段模式：圖形為空，`aria-label` 為「無資料」。

## 方法

| 方法 | 回傳 | 說明 |
|---|---|---|
| `render(container)` | `this` | 掛到元素或 CSS 選擇器對應的容器；circle 會在掛載後重繪一次，讓顏色取自實際文件的 token |
| `mount(container)` | `this` | 元件契約用的別名，同 `render` |
| `setValue(value)` | `this` | 更新單一數值（夾在 0–max）。分段模式中只記住數值，回到單一模式時才顯示 |
| `setVariant(variant)` | `this` | 切換單一數值模式的色彩；不認得的值忽略。分段各自保有顏色 |
| `setSegments(segments)` | `this` | 陣列（含空陣列）→ 進入或更新分段模式；`null`／非陣列 → 還原單一數值模式（DOM、ARIA、文字、動畫都回到原狀） |
| `destroy()` | `void` | 取消動畫與 rAF、解除主題訂閱、移除 DOM（含圖例）；可重複呼叫，之後呼叫其他方法不會拋錯 |

沒有事件或回呼；Progress 是純顯示元件。

## 圖例（`showLegend`）

只在分段模式出現：`ul.cl-progress-legend`，每段一個 `li.cl-progress-legend-item`（`data-segment-index`），內含

- `.cl-progress-legend-swatch`：色塊，`aria-hidden="true"`；
- `.cl-progress-legend-label`：標籤；
- `.cl-progress-legend-value`：`{value}（{percent}%）`，數值依目前語系格式化（最多兩位小數）。

bar 的圖例換到下一行（外層加上 `flex-wrap: wrap`，軌道改為自動伸縮，讓進度條與百分比文字維持同一行）；circle 會把 canvas 與中央文字包進 `.cl-progress-circle-ring`，圖例排在圓環下方。回到單一數值模式時這些版面調整全部還原。

## 鍵盤與 ARIA

Progress 不可聚焦、沒有鍵盤操作，也不在 Tab 順序中。

| 模式 | 語意 |
|---|---|
| 單一數值（bar） | 軌道 `role="progressbar"`，`aria-valuemin="0"`、`aria-valuemax`、`aria-valuenow`（indeterminate 時不帶 `aria-valuenow`） |
| 單一數值（circle） | 外層 wrapper 同上 |
| 分段（bar） | 軌道 `role="img"` + `aria-label`，移除 `aria-value*` |
| 分段（circle） | `canvas` `role="img"` + `aria-label`；wrapper 移除 `progressbar` 語意 |

分段模式採用 **`role="img"`** 而非 group：堆疊條是一個整體圖形，內部色塊沒有個別語意（`img` 的子元素一律視為展示用）；摘要放在 `aria-label`，格式為每段 `{label}：{percent}%`，以 `，` 串接（英文為 `{label}: {percent}%`，以 `, ` 串接）。圖例與百分比文字在圖形之外，仍是可被輔助技術讀取的一般文字。

## 語系

命名空間 `progress`（zh-TW／en）：`segmentSummary`、`segmentSeparator`、`segmentFallbackLabel`、`noSegments`、`legendValue`。元件不監聽 `locale-changed`；切換語系後於下一次 `setSegments()` 生效。

## DOM 結構（class）

- bar：`.cl-progress-wrapper` › `.cl-progress-bar-track`（分段時加 `.cl-progress-bar-track--segmented`）› `.cl-progress-bar-fill`（`--indeterminate`）或多個 `.cl-progress-bar-segment`；`.cl-progress-text`；`.cl-progress-legend`。
- circle：`.cl-progress-circle-wrapper` › `canvas` + `.cl-progress-circle-text`（有圖例時兩者包在 `.cl-progress-circle-ring` 內）；`.cl-progress-legend`。

顏色一律來自主題 token（`var(--cl-*)`），深色模式由 `[data-theme="dark"]` 的 token 值提供。

## 範例

```html
<!DOCTYPE html>
<html lang="zh-TW">
<head>
    <meta charset="UTF-8">
    <link rel="stylesheet" href="../../theme.css">
</head>
<body>
    <div id="orders-bar"></div>
    <div id="orders-ring"></div>
    <button id="refresh" type="button">更新</button>

    <script type="module">
        import { Progress } from './Progress.js';

        const snapshot = () => [
            { value: 42, variant: 'success', label: '已出貨' },
            { value: 18, variant: 'warning', label: '備貨中' },
            { value: 5, color: 'var(--cl-danger)', label: '缺貨' }
        ];

        // 線性堆疊條 + 圖例（max 為訂單總數）
        const bar = new Progress({ max: 80, showText: true, showLegend: true, segments: snapshot() })
            .mount('#orders-bar');

        // 圓環多段弧
        const ring = new Progress({ type: 'circle', size: 'large', max: 80, showText: true, segments: snapshot() })
            .mount('#orders-ring');

        document.getElementById('refresh').addEventListener('click', () => {
            const next = snapshot().map((s) => ({ ...s, value: s.value + Math.round(Math.random() * 5) }));
            bar.setSegments(next);
            ring.setSegments(next);
        });

        // 回到單一數值模式：bar.setSegments(null); bar.setValue(60);
        // 離開頁面前：bar.destroy(); ring.destroy();
    </script>
</body>
</html>
```
