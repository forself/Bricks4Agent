# Theme Studio — 全域視覺調校台

基於**全部元件**的所見即所得(WYSIWYG)設計 token 調校工具,給網站開發者與 AI 代理:
即時調整全站樣式(顏色/圓角/字級/陰影/字體…)→ 即時預覽所有元件 → 存成客製樣式表。

> **延伸文件**:設計架構見 [設計說明 DESIGN-SYSTEM.md](../../packages/javascript/browser/ui_components/DESIGN-SYSTEM.md);
> 網站引入、客製產出如何使用見 [使用說明 THEME-USAGE.md](../../packages/javascript/browser/ui_components/THEME-USAGE.md)。

## 自舉(dogfooding)

Theme Studio 的介面**完全由 Bricks4Agent 自己的元件搭成**(ColorPicker / Slider / TextArea /
TextInput / ToggleSwitch / BasicButton / DownloadButton / UploadButton / TabContainer / Notification …)。
「搭得出來」即證明元件覆蓋面達到最基礎標準;搭不出來的缺口就是必要元件。
> 本工具在開發時即以此驗證,補上了缺的 **Slider** 與 **TextArea** 兩個基礎表單控制項。

## 為什麼即時生效很簡單

全庫樣式一律走 `var(--cl-*)` token,所以調 token 只需
`document.documentElement.style.setProperty('--cl-primary', value)`,**所有已渲染的元件立即重繪**,無需重建。

## 啟動

所有樣式/模組路徑皆為**相對路徑**,任何靜態伺服器、任何根目錄都能跑(VS Code Live Server、python 皆可):

```powershell
# 例:於 Bricks4Agent 根(或工作區根皆可)啟動任一靜態伺服器
python -m http.server 8124 --bind 127.0.0.1
# 開啟 .../tools/theme-studio/index.html(依你的伺服器根調整前綴)
```

> VS Code Live Server 直接對 `index.html` 按「Go Live」即可,不需特定根目錄。

## 使用

- **左側**:分頁式 token 編輯器(語意色 / 圓角 / 字級 / 效果·字體 / 進階 JSON)。拖曳/選色即時套用**全域**。
- **右側**:元件展示廊——掃全 catalog(115),隨 token 即時變化;缺範例者以可見標記現形,不靜默缺席。
- **頂部**:深色切換、儲存(localStorage)、匯出 `theme.tokens.json`、匯出 `theme.custom.css`、匯入 tokens、重置。
- **每卡 ⚙(已渲染元件)**:開右側抽屜**只調該元件**,作用域為具名 class(預設 `b4a-c-<元件名>`,可改名建立變體);併入同一份 `theme.custom.css`(`:root` 在後接各 `.class`)。見 [THEME-USAGE §3.4](../../packages/javascript/browser/ui_components/THEME-USAGE.md)。
- **「↗ 開啟舞台」(非內嵌重元件)**:地圖 / 繪圖板 / 大圖表 / 富文本編輯器等需大畫布者,點卡片開**全尺寸彈窗**渲染,頂部下拉 + 上/下一個可切換其他重元件。Leaflet 已 vendored(本地載入);圖磚本身仍需連得到圖磚伺服器(OSM 或 NLSC/TGOS),連不到時顯示優雅 fallback。

## 儲存機制與客製樣式表

| 產物 | 用途 |
|---|---|
| `theme.tokens.json` | 只含有調整的 token(機器可讀)。**AI 代理直接編輯此檔**即可調主題 |
| `theme.custom.css` | 由 tokens 產生的 `:root{…}` 覆蓋樣式表。網站於 `theme.css` **之後**載入即套用 |

載入順序:`palette.css`(theme.css @import 的色階 foundation)→ `theme.css`(語意層)→ **`theme.custom.css`**(客製覆蓋)。

### AI 代理管道(零依賴)

```bash
# 代理直接寫/改 theme.tokens.json,再產出客製樣式表:
node gen-custom-css.mjs theme.tokens.json theme.custom.css
```

`theme.tokens.json` 結構:
```jsonc
{
  "meta": { "name": "TimWeb Theme" },
  "tokens":     { "--cl-primary": "#7c3aed", "--cl-radius-md": "14px" },
  "tokensDark": { "--cl-primary": "#a78bfa" }   // 選填:深色主題覆蓋
}
```

## 測試

```powershell
# 於 Bricks4Agent 根啟動 8124,再:
node tools/theme-studio/run.mjs   # 真實 Edge:啟動/全 catalog 覆蓋/即時生效/匯出/元件覆蓋/舞台 驗證(14 項)
```

## 檔案

| 檔案 | 說明 |
|---|---|
| `index.html` | 入口頁(只引 theme.css + richtext.css,UI 全由元件組成) |
| `studio.js` | 主程式:token 編輯器 + 展示廊 + 儲存/匯出/匯入 |
| `sample-data.js` | 展示廊各元件的範例 options |
| `gen-custom-css.mjs` | tokens.json → theme.custom.css 生成器(AI/CI 管道) |
| `run.mjs` | 真實 Edge 冒煙測試 |
