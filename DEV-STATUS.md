# 交接文件 — AI 代理接續開發用

> **你是接手的 AI 代理。** 本文件自足,不依賴先前任何對話、記憶系統或其他代理的上下文;照本文件即可續作。
> **目前已推送基準:** 分支 `main`(Tim2026 fork 同步線經 PR #10 併入;`main_0707` 已落後 23 個提交,不再是基準)。各項基準釘 commit:.NET 10 平台遷移 `d89fa4e`、.NET 10 零警告/安全建置 `51dd0f3`、Form Application Studio `396e9fa`(`git log -1 -- global.json` 等路徑查詢現在會回傳 `9e877f3` 同步提交,不能再當錨點);最新狀態以 `git log main` 為準,工作樹狀態一律另查 `git status`。
> 本文件自身的版本以 `git log -1 -- DEV-STATUS.md` 為準(文件修訂不代表功能基準變動)。
> 配套規則文件:[CLAUDE.md](CLAUDE.md) / [AGENTS.md](AGENTS.md)(代理規則)、[AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md)(元件調用契約,動手寫頁面前必讀)。

---

## 0. 任務與邊界(先讀這節)

**大案子:** 把 TIM 組織犯罪資料應用系統(React 16 舊系統)重製為**只用本元件庫**的前端 + .NET 10 契約相容後端。三條線:

| 線 | 內容 | 狀態 | 你現在動不動 |
|---|---|---|---|
| **元件庫線(本 repo)** | 補元件、嚴格 CSP、SVG→Canvas、Theme Studio、表單應用生成器 | 波 3 已推送；Form Application Studio 已完成；Tim2026 fork 已同步、三批稽核修復已併入 `main`(§4.4) | **覆核 §4 證據後可續作** |
| 前端軌(F) | 舊頁面翻寫,現於 `D:\work\Tim2026` 的 TimWeb 前端進行 | 已在 Tim2026 進行中;`D:\proj\newTim\tim-web` 現僅為舊版語意參考(Tim2026 `AGENTS.md`);進度以 Tim2026 為準 | **不動,等使用者發動** |
| 後端軌(B) | .NET FX 4.8.1 → **.NET 10**,契約逐位相容;現於 `D:\work\Tim2026` 的 TimNet10 後端進行 | 已在 Tim2026 進行中;B4A 內嵌於 `packages/Bricks4Agent`,由 `TimWeb/b4a.lock.json` 釘版;進度以 Tim2026 為準 | **不動,等使用者發動** |

> ⚠ 本 repo 的 SDK-style 專案、SPA template、生成後端與 BaseOrm canonical implementation 已統一為 **net10.0**；另保留一份 BaseOrm .NET Framework 4.8 相容實作。這項基礎設施升級不代表後端軌(B)的舊系統契約翻寫已啟動。

**硬邊界(違反=事故):**
1. `D:\work\new`、`D:\work\TIMSolution` 兩個舊專案**唯讀**,任何產出禁止寫入。
2. `D:\proj\newTim\tim-web` **不上本 repo 版控**(版控歸使用者的大專案),**不得對它做任何 git 操作**;檔案可讀、經使用者同意可改。
3. git push **不直推 main**,推工作分支再開 PR 併入(最近一次 PR 是 `codex/unified-checkpoint-20260805` → PR #10(08-30);其後 `11e8e95` 至 `34ba862` 共 16 個提交直接落在 `main`,未經 PR;使用者若開新分支會告知)。
4. 本 repo 是大專案剪枝後的工作副本,已設 `git sparse-checkout set docs packages templates tools`——**不要**碰 sparse 設定、不要「還原」看似被刪的路徑(例如 `.github/` 有版控但不在 cone 內,本機看不到不代表被刪)。

**已定案、不得重開的決策**(使用者已裁決,別再提替代方案):
- **SVG 全面禁用、只允許 Canvas**（含 UI 小圖示），由 `audit-csp.mjs` hard-zero 執法；空 baseline 不得豁免命中（§2）。
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
- 重製案文件(參考,勿版控):`D:\proj\newTim\tim-web\docs\`(blueprint.md、task-board.json;tim-web 現為舊版語意參考,現行進度見 `D:\work\Tim2026`)

**開工先跑(全部應綠;有紅先修再開工):**

```bash
cd D:/proj/newTim/Bricks4Agent
git status -sb                              # 應在 main 或當輪工作分支；提交前會看到本節所列待提交變更
node tools/scripts/audit-csp.mjs            # CSP A–J 全類硬零(含 G 類 SVG)
node tools/scripts/test-audit-csp-hard-zero.mjs # G 類負向回歸:即使列入 baseline 仍必須 fail;須與 audit 串行
node tools/scripts/validate-ui-library.mjs  # 風格稽核/裸 import/公開面/demo 引用
npm --prefix packages/javascript/browser install # 首次:安裝 devDependencies vitest/jsdom(下兩行的 Vitest 部分需要)
npm test                                    # test-all.js 生成器範例 + test:ui-components + test:custom-components + 元件 Vitest 套件(根 package.json)
npm --prefix packages/javascript/browser test   # 五套純函式單元(palette/id-url 安全/色階/聚合/力學,86 斷言)+ Vitest 元件套件
npm run custom-components:check             # 客製 JSON registry deterministic + schema/引用驗證
npm run test:custom-components              # 客製分類/build/runtime/factory/folder/lifecycle
npm run test:studio:self-host                # 唯一 Tool JSON + renderer/Link provenance/相容入口 19/19
npm run test:form-designer                   # 表單應用定義/驗證/SQL/API/PageDefinition + layout helpers
npm run test:form-designer:self-host         # Form Application Studio JSON 自舉與正式元件 provenance
npm run test:form-designer:dotnet            # 四種 provider 生成後端實際 net10.0 build
npm run test:dotnet10                        # 35 個 net10.0 專案；任何建置警告都視為錯誤
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj
dotnet test packages/csharp/tests/integration/Integration.Tests.csproj
dotnet test templates/spa/backend.Tests/SpaApi.Template.Tests.csproj
```

**瀏覽器驗收電池**（Studio 三支會自行啟動 random-port/no-store server 與 fresh Edge；其他既有 harness 依各腳本需求啟 server。元件庫本身零 runtime dependency;`packages/javascript/browser` 另有 devDependencies vitest/jsdom）：

```bash
node tools/theme-studio/run.mjs             # 相容別名:實際執行 studio-integration-smoke.mjs(16 項,同 test:studio:browser)
node tools/scripts/studio-integration-smoke.mjs # 16 項(說明連結/同 renderer/DOM identity/雙 JSON round-trip/provenance/CSS 注入拒絕)
node tools/scripts/canvas-chart-smoke.mjs   # 8 項
node tools/scripts/wave2-stage-sweep.mjs    # 29 項
node tools/scripts/data-explorer-smoke.mjs  # 8 項
node tools/scripts/cluster-graph-perf.mjs   # 8 項(5000 節點效能)
node tools/scripts/icon-canvas-smoke.mjs    # 13 項(Icon Canvas/DPR/ThemeBus/同步延遲掛載/互動/lifecycle)
node tools/scripts/wave3-stage-sweep.mjs    # 24 項(波 3 全 registry/API/語意/lifecycle)
node tools/scripts/custom-component-studio-smoke.mjs --require-browser # 13 項(Studio/runtime/dynamic form/export/import/security/lifecycle)
node tools/scripts/form-application-studio-smoke.mjs --require-browser # 17 項(schema/design/generate/secret isolation/drag/resize/lifecycle)
```

---

## 2. 必須內化的架構事實

- **catalog 116 元件**,權威來源=`ui_components/metadata/component-catalog.json`;元件契約 `new X(options)` → `.mount(el)` → `.destroy()`(個別元件用 `render`,harness 已示範相容處理)。

- **CSP 守門員** `tools/scripts/audit-csp.mjs`:十類全部硬零——JS 源碼 A `<style>` 注入/B setAttribute('style')/C innerHTML 模板 `style=`/D 模板 `on*=`/E eval/F `javascript:`(僅 `utils/security.js` 豁免),SVG 為 G,HTML 字面另有 H `<style>`/I `style=`/J `on*=`。`tools/scripts/svg-baseline.json` 已為空物件,只保留盤點快照語意,**不能豁免 G 類命中**;`test-audit-csp-hard-zero.mjs` 會把單一命中寫進 baseline 再確認 audit 仍 exit 1 並於 finally 還原/清理。**合規宣稱只認機器判定。**

- **樣式作法**:CSSOM(`cssText`/`setProperty`)為主;hover/focus 用事件;偽元素/@media/大量子孫選擇器 → 同目錄 `.css` + 同源 `<link>` 注入(注意 inline CSSOM 蓋樣式表,@media 覆蓋需 `!important`)。

- **Canvas 體系**:`CanvasChart` 提供 DPR 背景儲存/ResizeObserver/ThemeBus 重繪/hit-region(rect/circle/Path2D,`isPointInPath` 要乘 dpr)/DOM tooltip(textContent)/`exportPNG(scale)`/`niceTicks/fmt/ellipsis/wrapText` 排版輔助。子類契約=實作 `draw(ctx,w,h)` + `addRegion()` + 選配 `getTooltip(data)`。**點擊回拋走 `options.onPointClick`——基底不會自動呼叫你的 `_handleClick`,要嘛建構子裡預設接線、要嘛自己 addEventListener(波 2 曾因此出過死代碼 bug)。**

- **`ModalPanel.alert()` 只認 `message` 字串**;要放 DOM 內容:取回傳的 modal,把 message `<p>` `replaceWith(node)`(TimelineChart/FlameChart 有現成範例)。**`confirm/alert/prompt` 三個 static helper 現在都帶 `destroyOnClose: true`**——`close()` 後該 modal 自動銷毀,別再持有或重開同一個實例(舊行為從不 destroy,正是「越用越慢」的根因)。

- **raw HTML opt-in 只認 `raw()`**:`utils/security.js` 的標記以 `Symbol.for('bricks4agent.rawHtml')` 品牌化,`isRawHtml()` 查**自身**屬性。手寫或 `JSON.parse` 出來的 `{__html: "…"}` 一律不再被當成授權(`__html` 只留相容欄位),API 回傳的純資料因此無法冒充。

- **動態頁預設不再拉整套元件庫**:`binding/LazyComponentFactory.js` 只 dynamic import 定義實際引用到的元件,是 `DynamicToolRenderer` 的預設 factory;找不到的名稱回退到 eager `ComponentFactory` 的即時 registry,所以 `ComponentFactory.register()`(含客製元件)對 tool 頁仍可見。eager `ComponentFactory` 本身未變。

- **`lazyTabs` 預設為 `true`**(`DynamicPageRenderer`／`DynamicDetailRenderer`):分頁面板元素立即存在,內容延到首次啟用才產生;要回到建構時就畫完全部分頁,明確傳 `lazyTabs: false`。

- **`PageGenerator` 會驗識別字**:`definition.name`／`field.name`／`behaviors.*` 必須是合法 JS IdentifierName(中文欄位名仍可用),失敗走 `{ code: null, errors: [...] }` 契約回報,不再把定義字串未跳脫地寫進產出程式碼。

- **色彩**:單一來源=`editor/richtext-palette.js` 的 MATERIAL(16 色相;**這份 palette 無 deep-purple/light-blue,有 blue-grey/grey**——注意 theme.css 另有歷史語意 token 如 `--cl-deep-purple`,兩者是不同層,別混為一談)→ `ui_components/editor/gen-palette-css.mjs` 產 palette.css → theme.css 引用。程式取色用 `utils/color-scale.js`(sequential/diverging/categorical/hierarchical)。Canvas 內色回退一律 `FALLBACK_PAINT`(theme-bus 匯出),**元件內禁散裝 hex**;亮度對比遮罩四常數(`#00000099/#ffffffcc/#000000aa/#ffffffdd`)已在稽核 allow 名單。

- **vendor/ 豁免一切庫規範**(CSP/風格/裸 import 掃描都跳過):Leaflet 1.9.4 + html2canvas,本地優先、缺檔才退 CDN。

- **聚合**:`utils/aggregation-engine.js` 白名單 fail-closed;**時間聚合先按日期排序再分組**(民國標籤字典序不可靠)。

- **新元件流程**:三件套(`<Name>.js`+`index.js`+`<Name>.manifest.json`)→ 類別 barrel export → 需要的話進 ComponentFactory → `node ui_components/metadata/build-metadata.mjs`。**新「分類」要動兩處白名單**:`metadata/introspection.js` SEARCH_CATEGORIES + `metadata/manifest-schema.js` 分類枚舉。

---

## 3. 現況(哪裡了)

**一句話:** catalog 116、CSP A–J 全類硬零、runtime SVG 0 檔/0 處、波 3 與客製元件 Studio 已推送；repo 已加入由 JSON 自舉的 Form Application Studio，可把 schema 視覺化編排後生成表單、.NET 10 API/BaseOrm 與 SQL，未給連線字串時使用本地 SQLite；35 個 .NET 10 專案已達零警告並由 CI 以 warnings-as-errors 強制執行；Tim2026 embedded fork 已同步回本 repo 並經三批稽核（洩漏／資安／效能）修復後由 PR 併入 `main`；08-30 之後另有 16 個提交直接落在 `main`（見下表；09-01 交接見 [docs/reports/CurrentIssuesAndHandoff-2026-09-01.md](docs/reports/CurrentIssuesAndHandoff-2026-09-01.md)）。

| 完成 | Commit | 內容 |
|---|---|---|
| 09-22 | `34ba862` | 共用表單控制項（DatePicker／Dropdown／FormField／FormRow）不再互相重疊；新增 `tools/component-layout-regression` 契約測試 |
| 09-02 | `3e717e9` | `QueryDefinitionAdapter`：payload 綁定為空值時保留 `payloadDefaults` |
| 09-02 | `d5f620f` | 匯出動作支援格式化顯示值 payload（`$rowDisplay`／`$selectionDisplay`） |
| 09-02 | `a201c8d` | `TgosMap` 移除已公開的後備憑證 |
| 09-02 | `035399a` | `RegionMap` 支援已發佈的舊版 asset base |
| 09-02 | `1eaa805` | page-generator 新增快取式 `LazyModuleLoader` |
| 09-02 | `046d388` | 修復 Vitest 元件套件並併入 `npm test`（需先 `npm --prefix packages/javascript/browser install`）；新增 09-01 交接報告 |
| 09-02 | `9418cc1` | FieldResolver 的 TextArea 依 maxLength 決定 `autoResize`（未設或 >200 才自動增高） |
| 09-02 | `85a89c0` | page-generator 新增 `DeferredHydration` 集中延後水合生命週期（DynamicListRenderer 採用） |
| 09-01 | `8911b90` | 已發佈的元件庫 HTML 文件不再連出元件庫目錄（自含） |
| 09-01 | `6495133` | 強化出貨範本與快照完整性：新增 `quality-gates.yml`、`spa-template-smoke`、`snapshot-integrity.mjs`／`verify-consumer-snapshots`；`theme-studio/run.mjs` 改為 studio-integration-smoke 相容別名 |
| 09-01 | `df2c31a` | DynamicToolRenderer 預先建立的元件實例不再帶 mount 前的舊值進畫面 |
| 08-31 | `738be65` | DynamicToolRenderer 綁定值未變就不重推（無變動同步 1900 次 DOM 變動 → 0） |
| 08-31 | `6e13e09` | 修兩處空過閘門（TreeList 展示樣本、dom-equivalence 逐屬性 style）；新增 `npm run test:ui-components` |
| 08-31 | `4b6a654` | 三個靜默行為可診斷：未品牌 `{ __html }` 一次性警告、`data-tab-pending`、`dom-equivalence.mjs` |
| 08-30 | `11e8e95` | 文件對齊現況；`lazyTabs` 改為預設 `true`；dev server JWT 金鑰改 `crypto.randomBytes` 等殘留資安項 |
| 08-30 | `4a5efe8` | **三批稽核與修復（洩漏／資安／效能）**：ModalPanel 對話框改 `destroyOnClose`、MapEditor/MapEditorV2 補 `destroy()`、PanelManager stack 清理；PageGenerator 六個依語境跳脫函式 + 識別字驗證；dev server 綁 loopback + Host/Origin 守衛；`raw()` 改 `Symbol.for` 品牌；新增 `LazyComponentFactory`、`page-gen --pages/--all`、`lazyTabs`（當時預設 `false`）。公開介面不變、生成輸出逐位相同 |
| 08-25 | `45b326b` | **WebTextEditor TOC id 屬性注入 XSS 修復**：`isSafeId` 單一事實來源 + TOC 插入 escapeAttr + sanitizeHTML 丟棄不安全 id；`sanitizeUrl` 補 backslash protocol-relative open-redirect 缺口；新增 `security.id-url.test.mjs`（23 斷言） |
| 08-24 | `9e877f3` | **Tim2026 embedded fork 同步**：把長期在 fork 線上開發的狀態鏡射回本 repo（.NET 10 遷移、元件目錄擴充、每份文件的 Markdown + HTML 雙格式） |
| 07-23 | `51dd0f3` | **.NET 10 零警告與密碼相容性**：MFA、AccountLock、AuditLog nullable 契約修正；8 個過時 PBKDF2 建構式改為靜態 API，但保留既有 iterations/salt/hash 大小與儲存格式；Broker、MFA、SPA template 固定相容性向量通過；CI 對所有建置警告 fail closed |
| 07-23 | `d89fa4e` | **全 repo .NET 10 平台遷移**：35 個 SDK-style 專案與生成契約統一為 `net10.0`；BaseOrm canonical 路徑改為 `net10/`；保留明確 allowlist 的 BaseOrm .NET Framework 4.8 相容版本 |
| 07-23 | `396e9fa` | **Form Application Studio**：schema→欄位清單+12欄拖拉/縮放畫布→design JSON/PageDefinition/.NET 10 Minimal API+BaseOrm/SQL；JSON 自舉；連線字串留白→本地 SQLite；預設 secret 不落產物；unit 11/11+self-host 8/8+Edge 17/17 |
| 07-17 | `e92a4d6` | **波 3 + JSON 客製元件 + Studio 自舉**：SVG 清零、三層 JSON 客製元件、Theme/Custom 同頁工具與完整驗收 |
| 07-17 | `39f330f` | **波 2**:8 支重型圖表 + Sparkline/RegionMap/Progress/Rating 遷 Canvas;BaseChart 刪除;棘輪 31→26;風格稽核歸零(FALLBACK_PAINT 收斂) |
| 07-16 | `30e87f6` | **DataExplorer** 統計探索複合件 + Bar/Line/Pie Canvas 化 |
| 07-15 | `8d466e3` | **波 1**:Heatmap/Scatter/**ClusterGraph**(5000 節點 10.8ms/幀)+ 力學/色階/聚合核心 |
| 07-14 | `5293873` | **波 0**:SVG 禁用政策+棘輪、CanvasChart 基底、theme-bus |
| 07-09 | `9e9b3fa` | 嚴格 CSP 159 處清零 + audit-csp 守門員 |
| 07-08 | `cb22ba2` | main_0626+main_0707 合併(0707 優先;0626 的 26 元件 CSP 修正後併入) |
| 07-07 | `3ed9504` | 設計系統/Theme Studio/TGOSMapEditor/vendoring/junction+snapshot 機制 v2/create-project |

**.NET 10 驗證證據（2026-07-23）:** `npm run test:dotnet10` 以完整 `--warnaserror` 建置 35/35 專案，全部 0 warning/0 error；Release 測試為 Unit 410/410、Integration 27/27、SPA template 10/10。PBKDF2 固定向量覆蓋 Broker 兩條登入路徑、MFA 舊格式與 SPA template；GitHub CI 的 JavaScript/policy 與 .NET/generated-backend 兩個 job 均通過，產物清理檢查亦通過。

---

## 4. 完成任務線:波 3 —— SVG 存量清零(最新一輪見 §4.4)

**結果:** 26 檔 181 處 → **0 檔 0 處**,`svg-baseline.json` 已清空,G 類已與 A-F 同級硬零。主力 Icon、Button、Picker、Panel、TreeList、WebTextEditor、OSMMapEditor、DrawingBoard、DocumentWall、PhotoWall 均已轉 Canvas/DOM-safe 呈現。

**已落地:**
1. `common/Icon` 已改為 DPR-aware Canvas/Path2D,保留 size alias、`Icon.register()`、ThemeBus/currentColor、鍵盤 click、spin、destroy,並新增 `pathData`/文字 glyph 與公開 `redraw()`。
2. 其餘 25 檔由互斥代理分組遷移後,主代理逐檔覆核並修正語意圖示、首次重繪、active 重繪、ThemeBus/子元件生命週期與 factory destroy。
3. Leaflet `preferCanvas` 硬設 true(呼叫端不能 opt-out),非同步載入加 destroyed guard;OSMMapEditor 追蹤並銷毀 6 個子元件;vendor/ 未動。
4. `audit-csp` G 類改硬零,baseline 清空;新增不可被 baseline 繞過的負向回歸。
5. 全量驗收完成，波 3 與後續 Studio／生成器工作均已提交並推送；目前狀態以 §3 與 `git log main` 為準。

**驗證證據(2026-07-17，平台遷移前):** audit-csp A-J/G 0;負向回歸 PASS;validate-ui-library 282 source/261 import + 9 demos browser PASS;兩組 npm test PASS(四套純函式 63/63);客製元件 definition/runtime 14/14、targeted integration 30/30、Edge E2E 13/13;Tool/page-generator 63/63;Studio 18/18 + 19/19 + 16/16;SPA backend build 0 warning/0 error;既有五支 Edge harness 因 Theme 增加三項現為 70/70;Icon 13/13;Wave 3 24/24。ClusterGraph 最終獨占 CPU 重測 BH=8.77ms、draw=2.99ms、8/8 通過;效能 harness 不應與其他瀏覽器壓測並跑。legacy harness 已移除字串 `waitForFunction`、inline `addScriptTag` 與產品層 `openStage` 依賴，能在 strict CSP + JSON self-host Studio 下真實執行。

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

### 4.3 Form Application Studio

- 核心契約位於 `packages/javascript/browser/form-application/`：嚴格驗證 schema、provider、欄位與 CRUD allowlist，生成 normalized definition、design JSON、PageDefinition、provider SQL/rollback、.NET 10 Model/Service/Endpoints/bootstrap 與整合說明。

- 正式 `layout/FormDesigner` 提供 schema 欄位清單、改欄位/顯示名、切換輸入元件、增刪欄位，以及 12 欄畫布的滑鼠/鍵盤拖拉與縮放；已註冊 `ComponentFactory` 並納入 catalog。

- 工具入口 `tools/form-application-studio/index.html`；唯一頁面定義為 `studio.page.json`，由 `DynamicPageRenderer(tool)` 產生，沒有另刻一套工具 UI。

- 連線政策：空白或缺少連線字串一律落到本地 SQLite `data/<application_id>.db`；外部 provider 必須明確指定且提供非空連線字串。secret 只存 controller 記憶體，預設不進 definition/design/bundle；只有使用者明確勾選才進 `backend/appsettings.Development.json`。

- Studio/CLI 只預覽與生成，絕不連線或套用 SQL。實際資料庫變更仍須先確認資料表、目標欄位、來源、寫入規則、例外處理與 rollback/驗證計畫。

- 驗證證據（2026-07-23，平台遷移前）：核心 11/11、自舉 8/8、真實 Edge 17/17；提交前獨立 CLI 稽核曾抓到外部 provider 的 definition JSON 被二次正規化為 SQLite，修正後已新增單元與 Edge 產物一致性斷言。CLI help/未知參數、首次生成/byte-identical 再生成、PostgreSQL provider 保留與 secret 0 命中均通過，CLI 產出的 Web 專案 build 0 warning/0 error。全量回歸另含 page-generator、四套純函式、客製元件 14/14、UI static+9 demos、CSP A-J/G 正負向、Theme/Custom Studio 19/19+18/18+16/16+13/13、Canvas/Wave2/Data/Icon/Wave3 8/8+29/29+8/8+13/13+24/24、ClusterGraph 8/8（BH 7.15ms、draw 2.46ms）及 SPA backend build 0 warning/0 error；`.test-output/` 已清理。

### 4.4 最新完成:三批稽核(洩漏／資安／效能)

commit `4a5efe8`(2026-08-30),外加 `45b326b`(2026-08-25)的 WebTextEditor XSS 修復。**公開介面不變、生成輸出逐位相同**。

- **洩漏(「越用越慢、記憶體持續成長」的根因)**:`ModalPanel.confirm/alert/prompt` 建立的對話框從不 destroy——現在三個 static helper 都帶 `destroyOnClose: true`,`ModalPanel`/`DrawerPanel` 的 `destroy()` 也補回 `super.destroy()`;`MapEditor`/`MapEditorV2` 原本完全沒有 `destroy()`(洩漏的 MapEditorV2 會無條件 `preventDefault` Ctrl+C/V,劫持整頁複製貼上),已補上;`PanelManager.unregister` 現在會 `_purgeFromStack` 清掉 `modalStack`/`focusStack`,`enterModal`/`enterFocus` 改為冪等。`templates/spa/frontend/components/Panel/` 是 BasePage 實際使用的第二份副本,同步修復。

- **資安**:`PageGenerator` 原本把定義字串未跳脫寫進生成程式碼,改為六個依語境的跳脫函式 + 識別字驗證(`_collectIdentifierErrors` 檢 `definition.name`／每個 `field.name`／`behaviors.onInit|onSave|onDelete` 與 `fieldTriggers`);`tools/spa-generator/server.js` 與 `templates/spa/scripts/web/server.js` 兩支 dev server 都改為預設綁 `127.0.0.1` 並加 Host/Origin 守衛;`raw()` 標記改用 `Symbol.for` 品牌 + 自有屬性檢查;`tools/lib/app-generator.js` 的輸出路徑加上 prefix 檢查擋路徑穿越;開發用 JWT 金鑰改為每次啟動隨機。`45b326b` 另補 `isSafeId` 收斂 WebTextEditor TOC 的 heading id 與 `sanitizeUrl` 的 backslash open-redirect 缺口。

- **效能**:新增 `binding/LazyComponentFactory.js`(動態頁不再載入整套元件庫,詳見 §2);`tools/page-gen.js` 新增 `--pages <id,id>`／`--all` 批次模式;`DynamicDetailRenderer`/`DynamicPageRenderer` 新增 `lazyTabs`(該提交預設 `false`;**`11e8e95` 起改為預設 `true`**);DataTable 勾選改定向更新;EditableTable 儲存格編輯不再整批重建元件;TreeList 選取/展開改定向更新;`create-project` 不再複製 `bin/obj`;metadata 管線去重,`component-catalog.json` 由 239,500 bytes 降為 126,597 bytes。

- **其他**:`TgosMap` 的 SVG 圖釘改 Canvas + `Path2D`、硬編碼色碼改主題 token,使 audit-csp 的 SVG 硬零與樣式稽核首次全綠。

- **新增純函式覆蓋**:`ui_components/utils/security.id-url.test.mjs`(23 斷言),`npm --prefix packages/javascript/browser test` 因此由四套 63 斷言變成五套 86 斷言。

**本輪驗證證據(2026-08-30):** `npm test`、`audit-csp`(0 CSP / 0 SVG)、`validate:ui-library`、`build-metadata --check` 全數通過;生成輸出逐位相同;`dotnet build` 0 錯誤。上述測試數字皆為該 commit 的自陳證據,接手時請自行重跑 §1 清單確認。

---

## 5. 驗收協定(每次改 ui_components 都要)

```
靜態:audit-csp → validate-ui-library → npm test(根)→ npm --prefix packages/javascript/browser test
動態:五支既有瀏覽器 harness + Icon + Wave 3(§1);效能 harness 單獨跑
客製元件:npm run custom-components:check → npm run test:custom-components → npm run test:custom-components:browser
Studio:npm run test:studio:self-host → npm run test:theme-studio:browser → npm run test:studio:browser(test:theme-studio:browser 現為相容別名,與 test:studio:browser 跑同一支 studio-integration-smoke.mjs)
Form Application:npm run test:form-designer:all → 生成產物的 .NET 10 build
.NET:npm run test:dotnet10 → 35/35 專案零警告；另跑 unit/integration/SPA template 三組 dotnet test
```

GitHub Actions：`.github/workflows/ci.yml` 在 PR→`main` 與 push→`main`
執行可攜式 JavaScript/政策守門、全部 .NET 10 專案 warnings-as-errors 建置及四 provider 生成後端 build。
`.github/workflows/quality-gates.yml`（windows-latest；push→`main` 與所有 PR）另跑 JS 守門，並在
`npm install --no-save --ignore-scripts playwright-core@1.61.1` 後以系統 Edge 跑六支自架 smoke：
`validate:ui-library:browser`、`test:spa-template:browser`、`test:theme-studio:browser`、`test:custom-components:browser`、
`test:studio:browser`、`test:form-designer:browser`。依賴 8124 server 與 `../tim-web/poc` 的
canvas-chart／wave2／data-explorer／cluster-graph／icon-canvas／wave3 harness 仍只在本機驗收。

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

- 瀏覽器測試用 Edge(`channel:'msedge'`)。自架 harness(validate-ui-library、spa-template、studio、custom-component、form-application 等)先解析 `playwright`/`playwright-core`,找不到才退到 `../tim-web/poc/node_modules`(spa-template-smoke 不退);依賴 8124 的 canvas/wave2/data-explorer/cluster/icon/wave3 仍直接 import `../../../tim-web/poc`;**不要**在本 repo npm install 任何東西(注意:`npm test` 的 Vitest 部分現需先 `npm --prefix packages/javascript/browser install`,見 §1)。

- 含 junction 的專案刪除前必先解除連結(`scripts/dev-link.mjs` unlink),遞迴刪除會追進腳手架本體。

- 測試產物(`.test-output/`、`out/`)用完即刪,見 CLAUDE.md 附表。

- `test-audit-csp-hard-zero.mjs` 會在 runtime root 與 baseline 暫放負向 fixture,只能與正式 audit **串行**執行;腳本 finally 會還原,跑完仍要確認 fixture 不存在且 baseline=`{}`。

---

## 8. 波 3 之後的待辦(順序供參,發動前先跟使用者確認)

1. **Form Application 後續**：若要真正套用 SQL／連接既有資料庫，先依高影響資料規則確認寫入計畫。多表關聯、migration diff、既有 API 反向匯入目前不在 v1。

2. **DataExplorer 擴充**:`'cluster'` 圖型接 ClusterGraph(spec 加 hierarchy 通道);ChartSpecBuilder 抽獨立元件;後端聚合模式(spec 直傳 Graph action,等 B 軌)。

3. **前端軌／後端軌**(使用者發動):已移至 `D:\work\Tim2026` 進行(TimWeb 前端 + TimNet10 後端),待辦與進度以 Tim2026 的文件為準(入口為其 `README`／`AGENTS.md`),不在本 repo 追蹤。

4. 掛帳:SKILL 包裝(已評估未實作)、機制 MCP 化(僅選項)。

5. 客製元件後續可選：static `PageGenerator.generate()` 自動物化 runtime/definitions；現況已支援直接 runtime 與 dynamic form，靜態產物須由啟動程式自行 `loadFolder()`。

---

## 9. 與使用者協作的慣例

- 溝通用**繁體中文**;直接、不奉承;壞消息直說(測試紅就說紅)。

- 使用者重視**驗證文化**:宣稱完成要附機器證據;被抓到「說清了但沒清」會嚴重損害信任(發生過,教訓=全類別盤點+機器守門)。

- 已定案決策(§0)別重開;真正的新決策(如動 tim-web、開新波、改公開 API)先問。

- commit 訊息:近期已改用 conventional-commit 前綴的英文首行(`fix:`／`fix(security):`／`docs:`／`chore:`),正文仍以繁中條列;不直推 main,推工作分支再開 PR(或使用者指定的分支)。
