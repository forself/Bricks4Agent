# 交接文件 — AI 代理接續開發用

> **你是接手的 AI 代理。** 本文件自足,不依賴先前任何對話、記憶系統或其他代理的上下文;照本文件即可續作。
> **功能基準:** commit `39f330f`(波 2,最後一次功能變更),分支 `main_0707`。
> 本文件自身的版本以 `git log -1 -- DEV-STATUS.md` 為準(文件修訂不代表功能基準變動)。
> 配套規則文件:[CLAUDE.md](CLAUDE.md) / [AGENTS.md](AGENTS.md)(代理規則)、[AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md)(元件調用契約,動手寫頁面前必讀)。

---

## 0. 任務與邊界(先讀這節)

**大案子:** 把 TIM 組織犯罪資料應用系統(React 16 舊系統)重製為**只用本元件庫**的前端 + .NET 10 契約相容後端。三條線:

| 線 | 內容 | 狀態 | 你現在動不動 |
|---|---|---|---|
| **元件庫線(本 repo)** | 補元件、嚴格 CSP、SVG→Canvas、Theme Studio、機制腳本 | 進行中 | **動——你的第一任務是波 3(§4)** |
| 前端軌(F) | 舊頁面翻寫,產出在 `D:\proj\newTim\tim-web` | 藍圖已定、POC 已過、量產未開工 | **不動,等使用者發動** |
| 後端軌(B) | .NET FX 4.8.1 → **.NET 10(目標)**,契約逐位相容 | 架構已裁決、未開工 | **不動,等使用者發動** |

> ⚠ .NET 10 是 B 軌的**目標**,不是 repo 現況:本 repo 現有 .NET 基礎設施(SPA template 後端 `templates/spa/backend` 等)仍是 **net8.0**,另有一份 BaseOrm 為 .NET FX 4.8。B 軌啟動後再依裁決升級或另建 .NET 10 專案,勿誤判現有專案已是 .NET 10。

**硬邊界(違反=事故):**
1. `D:\work\new`、`D:\work\TIMSolution` 兩個舊專案**唯讀**,任何產出禁止寫入。
2. `D:\proj\newTim\tim-web` **不上本 repo 版控**(版控歸使用者的大專案),**不得對它做任何 git 操作**;檔案可讀、經使用者同意可改。
3. git push **不直推 main**,推 `main_月日` 日期分支(現用 `main_0707`;使用者若開新日期分支會告知)。
4. 本 repo 是大專案剪枝後的工作副本,已設 `git sparse-checkout set packages templates tools`——**不要**碰 sparse 設定、不要「還原」看似被刪的路徑。

**已定案、不得重開的決策**(使用者已裁決,別再提替代方案):
- **SVG 全面禁用、只允許 Canvas**(含 UI 小圖示),機器棘輪執法(§2)。
- **嚴格 CSP**:`script-src 'self'; style-src 'self'`,零 unsafe-inline/eval,機器判定(§2)。
- 元件庫**不依賴任何 npm 安裝的第三方 runtime 套件**;核准例外只有 vendored 的 Leaflet 1.9.4 + html2canvas(`ui_components/vendor/`,本地優先,Leaflet 缺檔才退 CDN)。工具腳本**零 npm 依賴、純 Node**,禁 spawn ripgrep 等外部工具(不得假設目標機器有裝)。
- 圖表基底=`viz/CanvasChart.js`;主題響應=`utils/theme-bus.js`;色回退唯一常數=`FALLBACK_PAINT`。
- 民國曆:DatePicker 用 `format:'taiwan'`;後端回民國字串前端原樣輸出、禁 `new Date()` 轉換。

---

## 1. 環境與開工自檢

**路徑地圖:**
- 本 repo:`D:\proj\newTim\Bricks4Agent`(遠端 github.com/forself/Bricks4Agent)
- 元件庫本體:`packages/javascript/browser/ui_components/`(下文簡稱 `ui_components`)
- 守門與 harness:`tools/scripts/`;視覺調校台:`tools/theme-studio/`
- 重製案文件(參考,勿版控):`D:\proj\newTim\tim-web\docs\`(blueprint.md、task-board.json)

**開工先跑(全部應綠;有紅先修再開工):**

```bash
cd D:/proj/newTim/Bricks4Agent
git status -sb                              # 應在 main_0707 且乾淨
node tools/scripts/audit-csp.mjs            # CSP A-F 全零 + G 類 SVG 棘輪合規
node tools/scripts/validate-ui-library.mjs  # 風格稽核/裸 import/公開面/demo 引用
npm test                                    # 頁面生成器產碼 + 元件路徑測試(根 package.json)
npm --prefix packages/javascript/browser test   # 四套純函式單元(palette/色階/聚合/力學)
```

**瀏覽器驗收電池**(前置:repo 根 `python -m http.server 8124`;用 Edge 無頭,playwright-core 借 `../tim-web/poc/node_modules`,repo 本身零 devDeps):

```bash
node tools/theme-studio/run.mjs             # 14 項
node tools/scripts/canvas-chart-smoke.mjs   # 8 項
node tools/scripts/wave2-stage-sweep.mjs    # 29 項
node tools/scripts/data-explorer-smoke.mjs  # 8 項
node tools/scripts/cluster-graph-perf.mjs   # 8 項(5000 節點效能)
```

---

## 2. 必須內化的架構事實

- **catalog 115 元件**,權威來源=`ui_components/metadata/component-catalog.json`;元件契約 `new X(options)` → `.mount(el)` → `.destroy()`(個別元件用 `render`,harness 已示範相容處理)。
- **CSP 守門員** `tools/scripts/audit-csp.mjs`:六類硬零(A `<style>` 注入/B setAttribute('style')/C innerHTML 模板 `style=`/D 模板 `on*=`/E eval/F `javascript:`)+ **G 類 SVG 棘輪**(對 `tools/scripts/svg-baseline.json` 基線 26 檔 181 處:新增檔或既有檔增量=fail;清零一檔就跑 `--write-baseline` 收緊)。**合規宣稱只認機器判定。**
- **樣式作法**:CSSOM(`cssText`/`setProperty`)為主;hover/focus 用事件;偽元素/@media/大量子孫選擇器 → 同目錄 `.css` + 同源 `<link>` 注入(注意 inline CSSOM 蓋樣式表,@media 覆蓋需 `!important`)。
- **Canvas 體系**:`CanvasChart` 提供 DPR 背景儲存/ResizeObserver/ThemeBus 重繪/hit-region(rect/circle/Path2D,`isPointInPath` 要乘 dpr)/DOM tooltip(textContent)/`exportPNG(scale)`/`niceTicks/fmt/ellipsis/wrapText` 排版輔助。子類契約=實作 `draw(ctx,w,h)` + `addRegion()` + 選配 `getTooltip(data)`。**點擊回拋走 `options.onPointClick`——基底不會自動呼叫你的 `_handleClick`,要嘛建構子裡預設接線、要嘛自己 addEventListener(波 2 曾因此出過死代碼 bug)。**
- **`ModalPanel.alert()` 只認 `message` 字串**;要放 DOM 內容:取回傳的 modal,把 message `<p>` `replaceWith(node)`(TimelineChart/FlameChart 有現成範例)。
- **色彩**:單一來源=`editor/richtext-palette.js` 的 MATERIAL(16 色相;**這份 palette 無 deep-purple/light-blue,有 blue-grey/grey**——注意 theme.css 另有歷史語意 token 如 `--cl-deep-purple`,兩者是不同層,別混為一談)→ `ui_components/editor/gen-palette-css.mjs` 產 palette.css → theme.css 引用。程式取色用 `utils/color-scale.js`(sequential/diverging/categorical/hierarchical)。Canvas 內色回退一律 `FALLBACK_PAINT`(theme-bus 匯出),**元件內禁散裝 hex**;亮度對比遮罩四常數(`#00000099/#ffffffcc/#000000aa/#ffffffdd`)已在稽核 allow 名單。
- **vendor/ 豁免一切庫規範**(CSP/風格/裸 import 掃描都跳過):Leaflet 1.9.4 + html2canvas,本地優先、缺檔才退 CDN。
- **聚合**:`utils/aggregation-engine.js` 白名單 fail-closed;**時間聚合先按日期排序再分組**(民國標籤字典序不可靠)。
- **新元件流程**:三件套(`<Name>.js`+`index.js`+`<Name>.manifest.json`)→ 類別 barrel export → 需要的話進 ComponentFactory → `node ui_components/metadata/build-metadata.mjs`。**新「分類」要動兩處白名單**:`metadata/introspection.js` SEARCH_CATEGORIES + `metadata/manifest-schema.js` 分類枚舉。

---

## 3. 現況(哪裡了)

**一句話:** catalog 115、CSP 六類機器判定全零、SVG 只剩 26 檔存量(棘輪鎖死)、瀏覽器電池 67 項全綠、文件已對齊現況。

| 完成 | Commit | 內容 |
|---|---|---|
| 07-17 | `39f330f` | **波 2**:8 支重型圖表 + Sparkline/RegionMap/Progress/Rating 遷 Canvas;BaseChart 刪除;棘輪 31→26;風格稽核歸零(FALLBACK_PAINT 收斂) |
| 07-16 | `30e87f6` | **DataExplorer** 統計探索複合件 + Bar/Line/Pie Canvas 化 |
| 07-15 | `8d466e3` | **波 1**:Heatmap/Scatter/**ClusterGraph**(5000 節點 10.8ms/幀)+ 力學/色階/聚合核心 |
| 07-14 | `5293873` | **波 0**:SVG 禁用政策+棘輪、CanvasChart 基底、theme-bus |
| 07-09 | `9e9b3fa` | 嚴格 CSP 159 處清零 + audit-csp 守門員 |
| 07-08 | `cb22ba2` | main_0626+main_0707 合併(0707 優先;0626 的 26 元件 CSP 修正後併入) |
| 07-07 | `3ed9504` | 設計系統/Theme Studio/TGOSMapEditor/vendoring/junction+snapshot 機制 v2/create-project |

---

## 4. 你的第一個任務:波 3 —— SVG 存量清零

**目標:** 26 檔 181 處 → 0,G 類棘輪收硬零。清單=`tools/scripts/svg-baseline.json`(主力:Icon 家族、Button 家族、Picker 家族、Panel、TreeList、WebTextEditor、OSMMapEditor、DrawingBoard、DocumentWall、PhotoWall…)。

**步驟(已定案,照做):**
1. **先親手寫 `common/Icon` 的 Canvas 版**(不發子代理——這是其餘檔案的共同依賴,先立標準):Path2D 直接吃現有 55 條 path 字串(原字串不動)、尺寸相容(`'sm'|'md'|'lg'|數字`)、`Icon.register()` 契約不變、ThemeBus 換膚重繪、`currentColor` 語意用 resolveTokens 解析後 fill。完成即跑 audit + 手寫冒煙。
2. **發 ~5 路子代理**(模型 sonnet 級)分檔清剩餘:inline `<svg>` 字串 → `new Icon()` 或小型 canvas 繪製。**檔案互斥分工**;**驗收命令寫進提示詞**(該檔 `grep -cE '<svg|createElementNS|data:image/svg'` =0 + 語法檢查 + 行為要點),要求代理回報實跑輸出。
3. 特例:游標(cursor:url(data:image/svg…))→ 改 PNG data URL;`LeafletMap` 建構加 `preferCanvas:true`;vendor/ 內不動。
4. **本人逐檔機器驗收**(§5 鐵律)+ 瀏覽器電池全跑 + 展示廊/舞台抽查互動。
5. 基線清空後 `node tools/scripts/audit-csp.mjs --write-baseline`,再把 audit-csp.mjs 的 G 類改為硬零(比照 A-F);commit + push `main_0707`。

**完成定義:** audit-csp 全類硬零、五支瀏覽器 harness 全綠、兩組 npm test(根 + `--prefix packages/javascript/browser`)綠、validate-ui-library 綠、Theme Studio 展示廊肉眼抽查圖示正常、commit 訊息含棘輪清零聲明。

---

## 5. 驗收協定(每次改 ui_components 都要)

```
靜態:audit-csp → validate-ui-library → npm test(根)→ npm --prefix packages/javascript/browser test
動態:五支瀏覽器 harness(§1)
```

**鐵律(前人血淚,條條有事故背書):**
1. **回報不算數**——子代理宣稱完成後,逐檔機器驗收 + 行為驗證。波 2 實例:六路代理全報成功,實測仍揪出兩個 bug(alert content 被覆蓋、click 沒接線)——**渲染全綠也測不出,必須做互動鏈斷言**(dispatch 真實 click → 斷言彈窗文字非空)。
2. **代理死於 session 上限會留半成品**(曾發生寫了 `<link>` 沒建 .css):驗收看檔案,不看回報清單。
3. 改繪製路徑後,`canvas-chart-smoke` 的像素斷言(換膚變紅)是最後防線,別跳過。
4. harness 選擇器要鎖自建容器(如 `#cc-smoke`)——展示廊卡片本身是 canvas 圖表且有覆蓋層,全頁裸選 canvas 會誤中。
5. Theme Studio 測試鉤子:`window.__ts.openStage('元件名')` + `window.__stageInst`。

---

## 6. 多代理波工作法(若你派子代理)

1. 檔案互斥——絕不兩代理碰同檔。
2. 提示詞內建機器驗收命令與通過標準,要求代理自跑並貼輸出。
3. 上限 6 代理/波;機械遷移用 sonnet,設計判斷自己做。
4. 波收束=本人逐檔驗收(§5),然後才 commit。

---

## 7. 環境坑(照做,都踩過)

- **工具腳本不得假設執行環境有 ripgrep 或任何外部搜尋工具**(有的機器有、有的沒有——`audit-ui-style-rules.mjs` 曾因 spawn rg 在無 rg 機器上從沒真跑過):一律純 Node `fs`+regex,禁 spawn 外部工具。
- **PowerShell 5.1**:`.ps1` 只寫英文註解(UTF-8 無 BOM 中文=parse 炸);**改檔一律用編輯工具,禁 shell 重導向寫檔**(UTF-16 BOM 毀損坑)。機制腳本已全 Node 化。
- `node --check` 不吃瀏覽器 ESM `.js`——先複製成 `.mjs` 再驗。
- 瀏覽器測試用 Edge(`channel:'msedge'`),playwright-core 從 `../tim-web/poc/node_modules` 借;**不要**在本 repo npm install 任何東西。
- 含 junction 的專案刪除前必先解除連結(`scripts/dev-link.mjs` unlink),遞迴刪除會追進腳手架本體。
- 測試產物(`.test-output/`、`out/`)用完即刪,見 CLAUDE.md 附表。

---

## 8. 波 3 之後的待辦(順序供參,發動前先跟使用者確認)

1. **DataExplorer 擴充**:`'cluster'` 圖型接 ClusterGraph(spec 加 hierarchy 通道);ChartSpecBuilder 抽獨立元件;後端聚合模式(spec 直傳 Graph action,等 B 軌)。
2. **前端軌量產**(使用者發動):按 `tim-web\docs\task-board.json`——P0-1 頁面聚類 → 每群 `_base` 模板 → 生成器測試先行 → 量產頁 extends 只寫差異。
3. **後端軌**(使用者發動):B0-0 威脅建模 → B0-1 舊碼盤點 → B0-2 路由→10 動作對照表;契約釘 commit hash。
4. 掛帳:SKILL 包裝(已評估未實作)、機制 MCP 化(僅選項)。

---

## 9. 與使用者協作的慣例

- 溝通用**繁體中文**;直接、不奉承;壞消息直說(測試紅就說紅)。
- 使用者重視**驗證文化**:宣稱完成要附機器證據;被抓到「說清了但沒清」會嚴重損害信任(發生過,教訓=全類別盤點+機器守門)。
- 已定案決策(§0)別重開;真正的新決策(如動 tim-web、開新波、改公開 API)先問。
- commit 訊息:繁中、首行「波次/主題:摘要」、正文條列;不動 main,推 `main_0707`(或使用者指定的新日期分支)。
