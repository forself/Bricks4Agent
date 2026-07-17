# Bricks4Agent Studio

這是同一個頁面內的元件客製工作台；「客製」是操作，不是另一套元件家族。

- 「樣式客製」：調整全域 theme tokens、元件 scoped tokens 與 class，匯入／匯出 `theme.tokens.json`、匯出 `theme.custom.css`。
- 「元件組合」：從內建 catalog 與客製 registry 組合元件，依規則自動分類為 `atomic`、`composite`、`template`，匯入／匯出客製元件 JSON。

兩個頁籤都內建可見說明與互相導覽的正式 `Link` 元件。「樣式客製」說明 catalog 展示與 scoped token 操作；「元件組合」說明 Palette → Outline → Inspector → 驗證／匯出流程、三層分類規則、definitions 路徑與驗收指令，並連到本 repo 的完整指南。

## 自舉架構

Studio 的唯一頁面定義是 [`studio.page.json`](studio.page.json)。啟動程式讀取這份 JSON，交給 `DynamicPageRenderer` 的 `tool` mode；所有工具列、頁籤、輸入、清單、樹狀結構與按鈕都由 `ComponentFactory` 建立的 Bricks4Agent 元件產生。

JSON 只保存資料與可信 command ID，不包含函式、HTML 或任意 CSS。`controller.js` 與 `../custom-component-studio/controller.js` 提供 command registry、狀態與預覽資料。`slot` 只承載由 catalog/runtime 建立的動態預覽，不另建一套工具控制。

這個結構同時驗證：

1. PageDefinition JSON 能產生實際工具頁。
2. Studio 缺少的互動能力必須回補到正式元件與 renderer，而不是在頁面手刻控制。
3. 樣式與元件組合在同一個 renderer、同一份文件、不同頁籤中運作；切頁與 state 更新不得重建整個工作台。

## 開啟

以 repository root 啟動任一靜態 HTTP server，再開啟：

```text
/tools/theme-studio/index.html
/tools/theme-studio/index.html?tab=components
```

舊的 `/tools/custom-component-studio/index.html` 會導向第二個頁籤，保留書籤相容性。

## 驗收

下列腳本都會自行啟動 random-port、no-store server 與 fresh Microsoft Edge，不依賴既有 8124 server：

```powershell
npm run test:theme-studio:browser   # Theme、catalog 與頁內說明 18/18
npm run test:studio:self-host       # 單一 JSON、Link provenance、相容入口 19/19
npm run test:studio:browser         # 說明連結、DOM identity、雙 JSON round-trip、CSS 注入拒絕 16/16
npm run test:custom-components:browser # 客製元件安全/runtime 13/13
```

主要測試鉤子：

- `window.__studio`：definition、SHA-256、pageRenderer、renderer 與兩個 controller。
- `window.__toolPageRenderer`：實際 `DynamicToolRenderer`。
- `window.__studioControls.records`：互動控制到正式元件實例的 provenance map。
- `window.__ts`：Theme controller 相容 API。
- `window.__customComponentStudio`：元件組合 controller 相容 API。

## 檔案

| 檔案 | 用途 |
|---|---|
| `studio.page.json` | 唯一、權威 Tool PageDefinition |
| `studio.js` | 讀 JSON、合併 controllers、啟動 renderer 與公開測試鉤子 |
| `controller.js` | Theme 狀態、可信 commands、匯入匯出與 catalog 預覽 |
| `studio.css` | JSON node class 的外部樣式；只用 `--cl-*` tokens |
| `sample-data.js` | catalog 預覽用 options；不是工具 UI |
| `gen-custom-css.mjs` | 將 theme token JSON 轉為覆蓋 CSS |
| `run.mjs` | Theme Studio fresh-Edge 回歸 |

Theme token 的載入順序與產物用法見 [`THEME-USAGE.md`](../../packages/javascript/browser/ui_components/THEME-USAGE.md)；客製元件契約見 [`CUSTOM-COMPONENTS.md`](../../CUSTOM-COMPONENTS.md)。
