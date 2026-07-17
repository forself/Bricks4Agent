# 交接文件 — AI 代理接續開發用

> **你是接手的 AI 代理。** 本文件自足,不依賴先前任何對話、記憶系統或其他代理的上下文;照本文件即可續作。
> **上一個已推送功能基準:** commit `39f330f`(波 2),分支 `main_0707`;目前工作樹已完成波 3 + JSON 客製元件系統,尚未 commit/push。
> 本文件自身的版本以 `git log -1 -- DEV-STATUS.md` 為準(文件修訂不代表功能基準變動)。
> 配套規則文件:[CLAUDE.md](CLAUDE.md) / [AGENTS.md](AGENTS.md)(代理規則)、[AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md)(元件調用契約,動手寫頁面前必讀)。

---

## 0. 任務與邊界(先讀這節)

**大案子:** 把 TIM 組織犯罪資料應用系統(React 16 舊系統)重製為**只用本元件庫**的前端 + .NET 10 契約相容後端。三條線:

| 線 | 內容 | 狀態 | 你現在動不動 |
|---|---|---|---|
| **元件庫線(本 repo)** | 補元件、嚴格 CSP、SVG→Canvas、Theme Studio、機制腳本 | 波 3 已完成、待提交 | **先覆核 §4 證據;下一波需使用者發動** |
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
git status -sb                              # 應在 main_0707；提交前會看到本節所列待提交變更
node tools/scripts/audit-csp.mjs            # CSP A-G 全類硬零
node tools/scripts/test-audit-csp-hard-zero.mjs # G 類負向回歸:即使列入 baseline 仍必須 fail;須與 audit 串行
node tools/scripts/validate-ui-library.mjs  # 風格稽核/裸 import/公開面/demo 引用
npm test                                    # 頁面生成器產碼 + 元件路徑測試(根 package.json)
npm --prefix packages/javascript/browser test   # 四套純函式單元(palette/色階/聚合/力學)
npm run custom-components:check             # 客製 JSON registry deterministic + schema/引用驗證
npm run test:custom-components              # 客製分類/build/runtime/factory/folder/lifecycle
npm run test:studio:self-host                # 唯一 Tool JSON + renderer/Link provenance/相容入口 19/19
```

**瀏覽器驗收電池**（Studio 三支會自行啟動 random-port/no-store server 與 fresh Edge；其他既有 harness 依各腳本需求啟 server。repo 本身零 runtime/dev dependency）：

```bash
node tools/theme-studio/run.mjs             # 18 項(Theme JSON self-host/catalog/頁內說明/token/scoped/CSP)
node tools/scripts/studio-integration-smoke.mjs # 16 項(說明連結/同 renderer/DOM identity/雙 JSON round-trip/provenance/CSS 注入拒絕)
node tools/scripts/canvas-chart-smoke.mjs   # 8 項
node tools/scripts/wave2-stage-sweep.mjs    # 29 項
node tools/scripts/data-explorer-smoke.mjs  # 8 項
node tools/scripts/cluster-graph-perf.mjs   # 8 項(5000 節點效能)
node tools/scripts/icon-canvas-smoke.mjs    # 13 項(Icon Canvas/DPR/ThemeBus/同步延遲掛載/互動/lifecycle)
node tools/scripts/wave3-stage-sweep.mjs    # 24 項(波 3 全 registry/API/語意/lifecycle)
node tools/scripts/custom-component-studio-smoke.mjs --require-browser # 13 項(Studio/runtime/dynamic form/export/import/security/lifecycle)
```

---

## 2. 必須內化的架構事實

- **catalog 115 元件**,權威來源=`ui_components/metadata/component-catalog.json`;元件契約 `new X(options)` → `.mount(el)` → `.destroy()`(個別元件用 `render`,harness 已示範相容處理)。
- **CSP 守門員** `tools/scripts/audit-csp.mjs`:七類全部硬零(A `<style>` 注入/B setAttribute('style')/C innerHTML 模板 `style=`/D 模板 `on*=`/E eval/F `javascript:`/G SVG)。`tools/scripts/svg-baseline.json` 已為空物件,只保留盤點快照語意,**不能豁免 G 類命中**;`test-audit-csp-hard-zero.mjs` 會把單一命中寫進 baseline 再確認 audit 仍 exit 1 並於 finally 還原/清理。**合規宣稱只認機器判定。**
- **樣式作法**:CSSOM(`cssText`/`setProperty`)為主;hover/focus 用事件;偽元素/@media/大量子孫選擇器 → 同目錄 `.css` + 同源 `<link>` 注入(注意 inline CSSOM 蓋樣式表,@media 覆蓋需 `!important`)。
- **Canvas 體系**:`CanvasChart` 提供 DPR 背景儲存/ResizeObserver/ThemeBus 重繪/hit-region(rect/circle/Path2D,`isPointInPath` 要乘 dpr)/DOM tooltip(textContent)/`exportPNG(scale)`/`niceTicks/fmt/ellipsis/wrapText` 排版輔助。子類契約=實作 `draw(ctx,w,h)` + `addRegion()` + 選配 `getTooltip(data)`。**點擊回拋走 `options.onPointClick`——基底不會自動呼叫你的 `_handleClick`,要嘛建構子裡預設接線、要嘛自己 addEventListener(波 2 曾因此出過死代碼 bug)。**
- **`ModalPanel.alert()` 只認 `message` 字串**;要放 DOM 內容:取回傳的 modal,把 message `<p>` `replaceWith(node)`(TimelineChart/FlameChart 有現成範例)。
- **色彩**:單一來源=`editor/richtext-palette.js` 的 MATERIAL(16 色相;**這份 palette 無 deep-purple/light-blue,有 blue-grey/grey**——注意 theme.css 另有歷史語意 token 如 `--cl-deep-purple`,兩者是不同層,別混為一談)→ `ui_components/editor/gen-palette-css.mjs` 產 palette.css → theme.css 引用。程式取色用 `utils/color-scale.js`(sequential/diverging/categorical/hierarchical)。Canvas 內色回退一律 `FALLBACK_PAINT`(theme-bus 匯出),**元件內禁散裝 hex**;亮度對比遮罩四常數(`#00000099/#ffffffcc/#000000aa/#ffffffdd`)已在稽核 allow 名單。
- **vendor/ 豁免一切庫規範**(CSP/風格/裸 import 掃描都跳過):Leaflet 1.9.4 + html2canvas,本地優先、缺檔才退 CDN。
- **聚合**:`utils/aggregation-engine.js` 白名單 fail-closed;**時間聚合先按日期排序再分組**(民國標籤字典序不可靠)。
- **新元件流程**:三件套(`<Name>.js`+`index.js`+`<Name>.manifest.json`)→ 類別 barrel export → 需要的話進 ComponentFactory → `node ui_components/metadata/build-metadata.mjs`。**新「分類」要動兩處白名單**:`metadata/introspection.js` SEARCH_CATEGORIES + `metadata/manifest-schema.js` 分類枚舉。

---

## 3. 現況(哪裡了)

**一句話:** catalog 115、CSP A-G 全類硬零、runtime SVG 0 檔/0 處、波 3 全綠；JSON 客製元件與由單一 Tool PageDefinition 自舉產生的整合 Studio 已完成，整體工作樹待 commit/push。

| 完成 | Commit | 內容 |
|---|---|---|
| 07-17 | 工作樹(待提交) | **Studio 自舉**：唯一 `studio.page.json` → `DynamicPageRenderer(tool)`；樣式客製/元件組合同頁 tabs；頁內操作／分類說明與正式 Link 導覽；可信 commands/state binding/control provenance；Theme/Custom 實際下載上傳 round-trip；Edge 18/18 + 19/19 + 16/16 |
| 07-17 | 工作樹(待提交) | **JSON 客製元件**:`atomic/composite/template` 自動分類、definitions folder + deterministic registry、runtime/factory/dynamic form、Custom Component Studio、definition/runtime 14/14 + targeted integration 30/30 + Edge E2E 13/13 |
| 07-17 | 工作樹(待提交) | **波 3**:26 檔 181 處 SVG 清零;Icon/按鈕/Picker/Panel/TreeList/WebTextEditor/OSM/DrawingBoard/DocumentWall/PhotoWall 全 Canvas;G 類改硬零 + 負向回歸;104 項 Edge 電池全綠 |
| 07-17 | `39f330f` | **波 2**:8 支重型圖表 + Sparkline/RegionMap/Progress/Rating 遷 Canvas;BaseChart 刪除;棘輪 31→26;風格稽核歸零(FALLBACK_PAINT 收斂) |
| 07-16 | `30e87f6` | **DataExplorer** 統計探索複合件 + Bar/Line/Pie Canvas 化 |
| 07-15 | `8d466e3` | **波 1**:Heatmap/Scatter/**ClusterGraph**(5000 節點 10.8ms/幀)+ 力學/色階/聚合核心 |
| 07-14 | `5293873` | **波 0**:SVG 禁用政策+棘輪、CanvasChart 基底、theme-bus |
| 07-09 | `9e9b3fa` | 嚴格 CSP 159 處清零 + audit-csp 守門員 |
| 07-08 | `cb22ba2` | main_0626+main_0707 合併(0707 優先;0626 的 26 元件 CSP 修正後併入) |
| 07-07 | `3ed9504` | 設計系統/Theme Studio/TGOSMapEditor/vendoring/junction+snapshot 機制 v2/create-project |

---

## 4. 最新完成任務:波 3 —— SVG 存量清零

**結果:** 26 檔 181 處 → **0 檔 0 處**,`svg-baseline.json` 已清空,G 類已與 A-F 同級硬零。主力 Icon、Button、Picker、Panel、TreeList、WebTextEditor、OSMMapEditor、DrawingBoard、DocumentWall、PhotoWall 均已轉 Canvas/DOM-safe 呈現。

**已落地:**
1. `common/Icon` 已改為 DPR-aware Canvas/Path2D,保留 size alias、`Icon.register()`、ThemeBus/currentColor、鍵盤 click、spin、destroy,並新增 `pathData`/文字 glyph 與公開 `redraw()`。
2. 其餘 25 檔由互斥代理分組遷移後,主代理逐檔覆核並修正語意圖示、首次重繪、active 重繪、ThemeBus/子元件生命週期與 factory destroy。
3. Leaflet `preferCanvas` 硬設 true(呼叫端不能 opt-out),非同步載入加 destroyed guard;OSMMapEditor 追蹤並銷毀 6 個子元件;vendor/ 未動。
4. `audit-csp` G 類改硬零,baseline 清空;新增不可被 baseline 繞過的負向回歸。
5. 全量驗收完成;**尚未執行 commit/push**(本輪沒有使用者明確授權提交)。

**驗證證據(2026-07-17):** audit-csp A-J/G 0;負向回歸 PASS;validate-ui-library 282 source/261 import + 9 demos browser PASS;兩組 npm test PASS(四套純函式 63/63);客製元件 definition/runtime 14/14、targeted integration 30/30、Edge E2E 13/13;Tool/page-generator 63/63;Studio 18/18 + 19/19 + 16/16;SPA net8.0 build 0 warning/0 error;既有五支 Edge harness 因 Theme 增加三項現為 70/70;Icon 13/13;Wave 3 24/24。ClusterGraph 最終獨占 CPU 重測 BH=8.77ms、draw=2.99ms、8/8 通過;效能 harness 不應與其他瀏覽器壓測並跑。legacy harness 已移除字串 `waitForFunction`、inline `addScriptTag` 與產品層 `openStage` 依賴，能在 strict CSP + JSON self-host Studio 下真實執行。

### 4.1 最新追加：JSON 客製元件系統

- 權威契約：[CUSTOM-COMPONENTS.md](CUSTOM-COMPONENTS.md)；定義放 `packages/javascript/browser/custom_components/definitions/*.json`。
- `build-registry.mjs` 會驗證 path/symlink/名稱碰撞/引用/cycle/kind，產生 deterministic `registry.json`；瀏覽器不直接列舉資料夾。
- `CustomComponentRegistry` 採整批原子註冊與全圖重驗證，具 ownership-aware `dispose()`，預設可接 ComponentFactory；`CustomComponentRenderer` 負責 group/leaf/custom 遞迴、值 round-trip、runtime unsafe option 過濾與反序 destroy。
- `DynamicPageRenderer` 可用 `customComponents: { definitions, folder }` 在 async init 先全數取得、再單次註冊並接 FieldResolver；自建 registry 不污染全域 factory。SPA `DefinitionRuntimePage` 僅從可信 class/constructor options 取得來源，不信任頁面 JSON；目前自動接線為 dynamic form，靜態產碼不會自動打包 JSON folder。
- Studio 主入口：`tools/theme-studio/index.html`；「樣式客製」與「元件組合」共用 `tools/theme-studio/studio.page.json`、同一個 `DynamicToolRenderer` 與同一份 DOM。兩側均有 JSON-native 頁內說明與正式 `Link` 往返；組合頁另列三層規則、definitions 路徑與驗收指令。`tools/custom-component-studio/index.html` 僅為導向第二頁籤的相容 URL。
- 新增驗收：definition/runtime 14/14；Adapter/FieldResolver/SPA targeted integration 30/30；真實 Edge E2E 13/13（含下載 JSON、拒絕錯誤副檔名/破損/危險/超限匯入、匯入 template、factory lifecycle、值 round-trip、dynamic form、CSP/SVG=0）。

### 4.2 Studio JSON 自舉

- 新增 `ToolPageDefinition`：純 JSON 僅允許 `group/component/tabs/slot`、safe state path 與 allowlisted event；函式/getter/raw HTML/prototype key/未知 command/component 全部 fail closed。
- 新增 `DynamicToolRenderer`，並接進 `DynamicPageRenderer` tool mode 與 `PageGenerator.generate(type:'tool')` 靜態 wrapper。狀態更新只觸及重疊 bindings；setter 缺失只替換單一 component；內部控制重畫後 provenance 同步更新。
- Studio chrome 全由正式 ComponentFactory 元件建立。`List`/`TreeList` 補 action/active/data API，`UploadButton` 補 stable input id，`NumberInput.setValue()` 預設不回觸 onChange，作為自舉所需的正式元件能力。
- Theme 匯入會先把 token、catalog component、單一 class 與 CSS value 完整驗證到暫存快照，全部通過才一次套用；拒絕 selector/declaration/URL 注入且失敗不留下部分狀態。`customCss()` 匯出前再做防禦性驗證。
- 機器證據：Tool/page-generator Vitest 7 files 63/63；Studio static/fresh-Edge self-host 19/19；Theme 18/18；頁內說明、正式 Link 同頁導覽、跨頁籤、雙 JSON round-trip、匯入原子性與防禦性匯出 16/16；Custom runtime/browser 13/13；全部維持 CSP/SVG hard-zero。

---

## 5. 驗收協定(每次改 ui_components 都要)

```
靜態:audit-csp → validate-ui-library → npm test(根)→ npm --prefix packages/javascript/browser test
動態:五支既有瀏覽器 harness + Icon + Wave 3(§1);效能 harness 單獨跑
客製元件:npm run custom-components:check → npm run test:custom-components → npm run test:custom-components:browser
Studio:npm run test:studio:self-host → npm run test:theme-studio:browser → npm run test:studio:browser
```

**鐵律(前人血淚,條條有事故背書):**
1. **回報不算數**——子代理宣稱完成後,逐檔機器驗收 + 行為驗證。波 2 實例:六路代理全報成功,實測仍揪出兩個 bug(alert content 被覆蓋、click 沒接線)——**渲染全綠也測不出,必須做互動鏈斷言**(dispatch 真實 click → 斷言彈窗文字非空)。
2. **代理死於 session 上限會留半成品**(曾發生寫了 `<link>` 沒建 .css):驗收看檔案,不看回報清單。
3. 改繪製路徑後,`canvas-chart-smoke` 的像素斷言(換膚變紅)是最後防線,別跳過。
4. harness 選擇器要鎖自建容器(如 `#cc-smoke`)——展示廊卡片本身是 canvas 圖表且有覆蓋層,全頁裸選 canvas 會誤中。
5. Studio 測試鉤子：`window.__studio`、`window.__toolPageRenderer`、`window.__studioControls.records`、`window.__ts`、`window.__customComponentStudio`；來源證明看 `window.__studio.definitionUrl` + `definitionHash`。

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
- `test-audit-csp-hard-zero.mjs` 會在 runtime root 與 baseline 暫放負向 fixture,只能與正式 audit **串行**執行;腳本 finally 會還原,跑完仍要確認 fixture 不存在且 baseline=`{}`。

---

## 8. 波 3 之後的待辦(順序供參,發動前先跟使用者確認)

1. **DataExplorer 擴充**:`'cluster'` 圖型接 ClusterGraph(spec 加 hierarchy 通道);ChartSpecBuilder 抽獨立元件;後端聚合模式(spec 直傳 Graph action,等 B 軌)。
2. **前端軌量產**(使用者發動):按 `tim-web\docs\task-board.json`——P0-1 頁面聚類 → 每群 `_base` 模板 → 生成器測試先行 → 量產頁 extends 只寫差異。
3. **後端軌**(使用者發動):B0-0 威脅建模 → B0-1 舊碼盤點 → B0-2 路由→10 動作對照表;契約釘 commit hash。
4. 掛帳:SKILL 包裝(已評估未實作)、機制 MCP 化(僅選項)。
5. 客製元件後續可選：static `PageGenerator.generate()` 自動物化 runtime/definitions；現況已支援直接 runtime 與 dynamic form，靜態產物須由啟動程式自行 `loadFolder()`。

---

## 9. 與使用者協作的慣例

- 溝通用**繁體中文**;直接、不奉承;壞消息直說(測試紅就說紅)。
- 使用者重視**驗證文化**:宣稱完成要附機器證據;被抓到「說清了但沒清」會嚴重損害信任(發生過,教訓=全類別盤點+機器守門)。
- 已定案決策(§0)別重開;真正的新決策(如動 tim-web、開新波、改公開 API)先問。
- commit 訊息:繁中、首行「波次/主題:摘要」、正文條列;不動 main,推 `main_0707`(或使用者指定的新日期分支)。
