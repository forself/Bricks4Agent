# Canvas 圖表無障礙資料表(accessibleTable)

## 目的

Canvas 圖表對輔助科技(螢幕閱讀器等)而言只是一張圖片:`<canvas role="img" aria-label="…">` 只能讀出標題，讀不到任何數據。
開啟 `accessibleTable` 後，圖表會在自己的根元素內產生一個真實的 `<table>`,把圖上的資料以表格呈現，並讓 canvas 以 `aria-describedby` 指向它。

- 適用於 `CanvasChart` 基底與全部 14 個子類:`BarChart`、`LineChart`、`PieChart`、`RoseChart`、`HeatmapChart`、`ScatterChart`、`SankeyChart`、`RelationChart`、`ClusterGraph`、`OrgChart`(含繼承的 `HierarchyChart`)、`SunburstChart`、`FlameChart`、`TimelineChart`、`Sparkline`。
- **預設關閉**。未開啟時不建立任何 DOM、不改動 canvas 的 ARIA 屬性、不註冊任何監聽、不消耗 `nextUid` 序號，繪圖結果與既有行為完全相同。

## 選項

```js
new BarChart({
    container: '#host',
    title: '',                   // 既有選項:圖表標題;開啟資料表時也作為 <caption>(優先)
    ariaLabel: '',               // 既有選項:canvas 的 aria-label;沒有 title 時作為 <caption>
    accessibleTable: false,      // false=關閉(預設);true=視覺隱藏資料表(供輔助科技);'visible'=圖下可見資料表
    accessibleTableMaxRows: 500, // 資料表列數上限;超過時只列前 N 列，末列註明省略筆數(Infinity=不截斷)
    // …其他圖表選項照舊
});
```

- `accessibleTable` 只認 `true` 與 `'visible'`;其他值(例如字串 `'true'`、`1`)一律視為關閉。
- `accessibleTableMaxRows` 為負數或非數字時退回預設 500;`0` 表示只顯示「另有 N 筆資料未列出」註記列。
- 兩個選項都可以在建構後以 `chart.update({ accessibleTable: 'visible' })` 開啟、切換或關閉。

## DOM 結構

開啟後的結構(新增部分只在開啟時存在):

```text
div.cl-canvas-chart                                   根元素
├─ div.cl-canvas-chart__title                         有 title 時
├─ div.cl-canvas-chart__body
│  ├─ canvas[role=img][aria-label][aria-describedby="cl-chart-table-N"]
│  └─ div.cl-canvas-chart__tooltip
└─ div.cl-canvas-chart__a11y[data-mode="hidden|visible"]           ← 新增
   └─ table#cl-chart-table-N.cl-canvas-chart__table
      ├─ caption#cl-chart-table-N-caption
      ├─ thead > tr > th[scope="col"] …
      └─ tbody > tr > (th[scope="row"] | td) …
               └─ tr.cl-canvas-chart__table-note > td[colspan]      無資料 / 截斷註記
```

- 所有文字(caption、表頭、儲存格)一律以 `textContent` 寫入，資料中的 HTML 不會被解析。
- 樣式全部透過 CSSOM(`style.cssText`)設定，只使用 `var(--cl-*)` token;深色模式由 `[data-theme="dark"]` 的 token 值自動切換。
- 表格 id 由 `nextUid('cl-chart-table')` 產生(可重現);caption id 為 `<table id>-caption`。

## 兩種模式

| | `accessibleTable: true` | `accessibleTable: 'visible'` |
|---|---|---|
| 呈現 | 視覺隱藏(`position:absolute; 1px; clip; clip-path`),**不使用 `display:none`**,仍在無障礙樹中 | 接在繪圖區下方的一般表格 |
| 外層屬性 | 無 `role`、無 `tabindex`(隱藏內容不可成為 Tab 停駐點) | `role="region"`、`tabindex="0"`、`aria-labelledby` 指向 caption(寬表可用鍵盤橫向捲動) |
| 版面 | 不佔空間，圖表尺寸不變 | 見下方「可見模式的版面」 |
| 儲存格樣式 | 無 | 表頭 `--cl-bg-secondary` 底、數值欄靠右並使用等寬數字、`--cl-text` 文字、`--cl-border-light` 分隔線 |

### 可見模式的版面

圖表根元素原本以 `height` 固定高度(例如 BarChart 預設 260px)。若直接把表格塞進固定高度的根元素，繪圖區會被擠壓。因此可見模式在圖表第一次完成排版後:

1. 量測繪圖區(`.cl-canvas-chart__body`)目前的像素高度(量測時暫時隱藏資料表);
2. 把繪圖區固定為該像素高度，根元素改為 `height: auto`;
3. 資料表接在繪圖區下方，把根元素撐高，後續內容往下推。

結果是繪圖區大小與未開啟時相同。切回 `true` 或關閉時，根元素與繪圖區的原始樣式會完整還原。注意:

- 以百分比設定 `height` 的圖表，在可見模式期間高度固定為進入時的像素值，不再跟隨容器高度變化;若容器本身固定高度，資料表會超出容器。
- 圖表尚未排版(未掛上文件、位於 `display:none` 容器內)時不固定高度，待下一次量測再處理。
- `Sparkline` 通常放在表格儲存格或行內文字中，寬度很窄;建議使用 `true`。

## ARIA 行為

- canvas 保留既有的 `role="img"` 與 `aria-label`(基底一律會設定);只有當 canvas 缺少這兩個屬性時才補上(`aria-label` 補 caption 文字),關閉時也只移除自己補上的屬性。
- `aria-describedby` 附加資料表 id;若 canvas 原本已有 `aria-describedby`,以空白分隔附加，關閉時只移除本資料表的 id。
- 資料表只在存在期間與 canvas 關聯:`update({ accessibleTable: false })`、直接改選項後呼叫 `render()`、`destroy()` 都會移除表格與關聯。

### caption 來源(依序)

1. `getDataTable()` 回傳的 `caption`
2. `options.title`
3. `options.ariaLabel`
4. `Locale.t('canvasChart.tableCaption')`(「圖表資料」/ "Chart data")

`Sparkline` 預設 `ariaLabel` 為 `'sparkline'`,資料表 caption 改用呼叫端明確傳入的 `ariaLabel`,未傳入時用 Locale 預設;canvas 的 `aria-label` 維持原樣。

## 同步時機

資料表在「資料變更」時重建，同一輪事件內的多次變更以微任務合併為**一次**重建:

| 觸發 | 是否重建 |
|---|---|
| `setData(...)`、`update(patch)`(任何 patch) | 是 |
| 直接替換資料參照後呼叫 `render()`(例如 `chart.options.data = next; chart.render()`) | 是(以參照比對偵測) |
| `Locale.setLang(...)`(資料表存在期間監聽 `window` 的 `locale-changed`) | 是 |
| 切換 `true` ↔ `'visible'` | 是(只換樣式，表格節點沿用) |
| 單純 `render()`、容器縮放、主題切換、滑鼠 hover、動畫/力導向模擬的逐幀重繪 | 否 |
| OrgChart 收合/展開、ClusterGraph 鑽取、縮放平移等檢視狀態 | 否(資料表永遠列出完整資料) |

- **原地修改資料**(例如 `data.series[0].data[3] = 5`)不會改變參照，請接著呼叫 `chart.update()` 讓資料表同步;只呼叫 `render()` 會重繪圖表但不重建資料表。
- 重建與繪圖排程脫鉤：圖表離開視口(背景儲存已釋放、略過繪製)時，資料表仍會同步——螢幕閱讀器讀取的是整份文件，而非只有可視範圍。
- `getDataTable()` 擲錯時會 `console.error` 記錄，資料表顯示「無資料」列，不會中斷圖表。

## 數字與日期格式

- 數字:`Intl.NumberFormat(Locale.getLang())`(千分位、最多 3 位小數);欄位有 `unit` 時附加在數值後(例如 `1,234.5 kg`)。
- 百分比欄:值為 0~1 的比例，以 `Intl.NumberFormat(..., { style: 'percent', maximumFractionDigits: 1 })` 呈現。
- 日期時間欄:接受 epoch 毫秒、`Date` 或可解析字串，以 `Intl.DateTimeFormat(Locale.getLang(), { dateStyle: 'medium', timeStyle: 'medium' })` 呈現;無法解析者原樣顯示。
- `null`、`undefined`、`''`、`NaN` 顯示為空白格;非數字字串原樣顯示。
- 語系代碼不被 `Intl` 接受時(例如 `zh_TW`),退回執行環境預設語系，不擲錯。

## 各圖表的資料表形狀

| 圖表 | 欄位(表頭) | 每列代表 | 說明 |
|---|---|---|---|
| `BarChart`、`LineChart` | 類別、每個系列一欄 | `labels` 的每個類別 | 系列欄表頭 = `series[i].name`,缺名時為「系列 N」;數值附 `unit`;LineChart 的 `null` 斷點為空白格 |
| `RoseChart` | 類別、每個系列一欄 | 每個類別 | 同 BarChart(無 `unit` 選項) |
| `PieChart` | 名稱、數值、占比 | 每個 `value > 0` 的項目 | 與圖相同只列有畫出的扇區;占比 = 值 / 總和 |
| `HeatmapChart` | 列、欄、數值 | 每個非空格 | 長格式;略過 `null`/`NaN` 格;數值附 `unit` |
| `ScatterChart` | [名稱]、x、y、[大小]、[分類] | 每個 x、y 有效的點 | x/y 表頭取 `xLabel`/`yLabel`(缺則「X 值」「Y 值」),單位取 `xUnit`/`yUnit`;大小/分類表頭取 `sizeLabel`/`colorLabel`;方括號欄只在資料有值時出現 |
| `SankeyChart` | 來源、目標、數值 | 每條流(link) | 端點顯示節點 `name`;索引解析規則同佈局 |
| `RelationChart` | 起點、終點、權重 | 每條有效邊 | 端點顯示節點 `label`(缺則 `id`);權重 = `link.value`;略過端點不存在或自環的邊 |
| `ClusterGraph` | 起點、終點、權重 | 每一對有關聯的人員(不分方向) | 同對的多條邊合併，權重 = 邊數;列出完整資料，不受鑽取展開狀態影響 |
| `OrgChart`、`HierarchyChart` | 路徑、說明 | 每個節點(前序) | 路徑 = 各層 `title` 以「 / 」串接(缺則 `label`/`id`);說明 = `label`;不受收合狀態影響 |
| `SunburstChart` | 路徑、數值 | 每個扇區(含根，前序) | 數值規則同繪圖：非葉 = 子節點加總、葉節點缺值 = 1;另行計算，不寫回原資料 |
| `FlameChart` | 路徑、數值 | 每個框(含根，前序) | 數值取節點原值 |
| `TimelineChart` | 名稱、[群組]、開始、結束 | 每個事件(資料順序) | 群組(泳道)欄只在資料有 `group` 時出現;時間以日期時間格式呈現 |
| `Sparkline` | 序號、數值 | 每個資料點 | 序號自 1 起 |
| `CanvasChart`(基底) | 依 `options.data` 形狀 | — | `{ labels, series }` → 同 BarChart;數字陣列 → 序號/數值;`[{ name 或 label, value }]` → 名稱/數值;`{ nodes, links }` → 來源/目標/數值;階層 `{ name, children }` → 路徑/數值;其他形狀 → 「無資料」 |

- 路徑欄、名稱欄、類別欄以 `<th scope="row">` 呈現(列標題),螢幕閱讀器在欄間移動時會一併報讀。
- 無任何資料時，資料表只有一列跨全部欄的「無資料」。

## getDataTable() 契約

```js
/**
 * @returns {{
 *   caption?: string,
 *   columns: Array<{
 *     key: string,              // 列物件中的鍵
 *     label: string,            // 表頭文字(未給時用 key)
 *     format?: 'number' | 'percent' | 'datetime' | 'text',  // 省略時依值型別推斷
 *     unit?: string,            // 數值後綴(number 欄)
 *     rowHeader?: boolean       // 該欄以 <th scope="row"> 呈現
 *   }>,
 *   rows: Array<Object>         // 原始值;percent 為 0~1、datetime 為 epoch 毫秒或 Date
 * } | null}
 */
getDataTable()
```

- 列物件存**原始值**,格式化(語系、單位、百分比)由基底負責，因此 `getDataTable()` 的回傳值也適合拿來做 CSV 匯出等用途。
- 回傳 `null` 或空 `rows` 時，資料表顯示「無資料」。
- 資料不在 `options.data` 的自訂子類，另外覆寫 `_a11ySources()` 回傳資料參照陣列，讓「替換資料參照後 `render()`」也能被偵測(內建子類皆已處理)。

自訂子類範例:

```js
import { CanvasChart } from './CanvasChart.js';
import Locale from '../i18n/index.js';

class RoomUsageChart extends CanvasChart {
    draw(ctx, w, h) { /* 以 this.options.rooms 繪製 */ }

    getDataTable() {
        return {
            columns: [
                { key: 'room', label: Locale.t('canvasChart.name'), rowHeader: true },
                { key: 'hours', label: Locale.t('canvasChart.value'), format: 'number', unit: 'h' },
                { key: 'share', label: Locale.t('canvasChart.percent'), format: 'percent' }
            ],
            rows: this.options.rooms.map((r) => ({ room: r.name, hours: r.hours, share: r.hours / 40 }))
        };
    }

    _a11ySources() { return [this.options.rooms]; }
}
```

## 方法

| 方法 | 說明 |
|---|---|
| `getDataTable()` | 回傳目前資料的表格模型(上方契約);純函式，資料表關閉時也可呼叫 |
| `update(patch)` | 既有方法;合併選項後重繪，並觸發資料表重建一次;`accessibleTable` / `accessibleTableMaxRows` 亦可由此切換 |
| `setData(data)` | 既有方法(各子類);替換資料並重繪，資料表同步重建 |
| `render()` | 既有方法;排程重繪。只有在資料參照或 `accessibleTable` 選項改變時才會牽動資料表 |
| `destroy()` | 既有方法;另外移除資料表、canvas 的 `aria-describedby`、`locale-changed` 監聽與可見模式的高度固定。可重複呼叫 |

## 事件與回呼

不新增事件或回呼。資料表存在期間會在 `window` 監聽 `Locale.setLang()` 發出的 `locale-changed` 事件，以重建 caption、表頭與數字格式;關閉或 `destroy()` 時移除。

## 鍵盤與 ARIA 摘要

- `true`:表格不可聚焦、不在 Tab 順序中;螢幕閱讀器可用表格導覽(例如 NVDA/JAWS 的 `T` 鍵)找到它，或在讀到圖片時經由 `aria-describedby` 取得描述。
- `'visible'`:外層 `role="region"` 以 caption 命名，`tabindex="0"` 可用 Tab 聚焦，聚焦後以方向鍵橫向捲動寬表;焦點外框沿用瀏覽器預設(不覆寫 `outline`)。
- 表頭 `th[scope="col"]`、列標題 `th[scope="row"]`、截斷/無資料註記列以 `colspan` 跨全部欄。

## Locale 字串(`canvasChart` 命名空間)

| 鍵 | zh-TW | en |
|---|---|---|
| `tableCaption` | 圖表資料 | Chart data |
| `empty` | 無資料 | No data |
| `truncated` | 另有 {count} 筆資料未列出 | Rows not shown: {count} |
| `category` | 類別 | Category |
| `series` | 系列 {index} | Series {index} |
| `name` | 名稱 | Name |
| `value` | 數值 | Value |
| `percent` | 占比 | Percentage |
| `row` / `column` | 列 / 欄 | Row / Column |
| `source` / `target` | 來源 / 目標 | Source / Target |
| `from` / `to` / `weight` | 起點 / 終點 / 權重 | From / To / Weight |
| `path` / `description` | 路徑 / 說明 | Path / Description |
| `group` / `start` / `end` | 群組 / 開始 / 結束 | Group / Start / End |
| `index` | 序號 | Index |
| `xValue` / `yValue` / `size` | X 值 / Y 值 / 大小 | X value / Y value / Size |

其他語系可用 `Locale.register('<lang>', 'canvasChart', { … })` 補齊。

## 範例

以下頁面放在 `packages/javascript/browser/` 目錄下，以任一靜態伺服器開啟即可執行:

```html
<!DOCTYPE html>
<html lang="zh-TW" data-theme="light">
<head>
    <meta charset="utf-8">
    <title>圖表無障礙資料表</title>
    <link rel="stylesheet" href="ui_components/theme.css">
</head>
<body>
    <div id="orders"></div>
    <div id="rooms"></div>
    <script type="module">
        import { BarChart } from './ui_components/viz/BarChart.js';
        import { PieChart } from './ui_components/viz/PieChart.js';

        // 可見資料表:圖下直接列出數據
        const orders = new BarChart({
            container: '#orders',
            title: '各月訂單數',
            unit: '筆',
            data: {
                labels: ['一月', '二月', '三月'],
                series: [{ name: '線上', data: [120, 98, 143] }, { name: '門市', data: [80, 91, 77] }]
            },
            accessibleTable: 'visible'
        });

        // 隱藏資料表:畫面不變，只提供給輔助科技
        new PieChart({
            container: '#rooms',
            title: '會議室使用占比',
            data: [{ name: 'A 室', value: 42 }, { name: 'B 室', value: 30 }, { name: 'C 室', value: 28 }],
            accessibleTable: true
        });

        // 資料更新 → 資料表同步重建一次
        setTimeout(() => orders.setData({
            labels: ['一月', '二月', '三月', '四月'],
            series: [{ name: '線上', data: [120, 98, 143, 160] }, { name: '門市', data: [80, 91, 77, 85] }]
        }), 2000);

        // 讀取表格模型(例如匯出 CSV)
        console.log(orders.getDataTable());
    </script>
</body>
</html>
```

## 限制與注意事項

- `aria-describedby` 指向整張表格：部分螢幕閱讀器在讀到圖片時會把描述全文念出;資料量大時可調低 `accessibleTableMaxRows`,或讓使用者直接以表格導覽閱讀。
- 資料表呈現「資料」,不呈現檢視狀態(縮放、平移、鑽取、收合、hover)。
- `RelationChart` / `ClusterGraph` 的資料表列出關聯邊;沒有任何邊的孤立節點與 ClusterGraph 的群組層級不會列在表中。
- 資料表的表頭與註記走 Locale;canvas 上既有的軸標籤、tooltip、「無資料」等繪製文字維持原樣，不在本功能範圍內。
