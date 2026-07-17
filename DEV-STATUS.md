# 開發現況與待辦(TIM 重製案 × Bricks4Agent 元件庫)

> 最後更新:2026-07-17(commit `39f330f`,分支 `main_0707`)。
> 本文件是「接手即可續作」的單一入口:現況、待辦、做法、環境坑全在這裡。
> 規則類文件:[CLAUDE.md](CLAUDE.md) / [AGENTS.md](AGENTS.md)(代理規則)、[AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md)(元件調用)。

---

## 一、案子是什麼

**雙軌重製 + 一條元件庫強化線:**

| 軌 | 內容 | 現況 |
|---|---|---|
| 前端軌(F) | 把 `D:\work\new`(TIM 組織犯罪資料應用系統,React 16)翻寫成**只用本元件庫**;產出放 `D:\proj\newTim\tim-web`(不在本 repo 版控) | 藍圖/任務板已定(`tim-web\docs\`),vertical slice POC 過(login-search 6/6),**量產未開工,等使用者發動** |
| 後端軌(B) | `D:\work\TIMSolution`(.NET FX 4.8.1)移植 .NET 10,API 契約逐位相容、資安修正優先於 parity | 架構已裁決(全域 10 端點/JWT 只取 ID/三層授權/STJ),**未開工** |
| 元件庫線 | 本 repo:補元件、嚴格 CSP、SVG→Canvas、Theme Studio、機制腳本 | **催速中,詳見下節;波 3 是下一步** |

兩個舊專案(`D:\work\new`、`D:\work\TIMSolution`)**唯讀**。`tim-web` 版控歸大專案,工具不得對它做 git 操作。

---

## 二、元件庫線:已完成(倒序)

| 完成 | Commit | 內容 |
|---|---|---|
| 2026-07-17 | `39f330f` | **波 2**:8 支重型圖表(Rose/Org/Hierarchy/Relation/Sankey/Sunburst/Timeline/Flame)+ Sparkline/RegionMap/Progress(circle)/Rating 全遷 CanvasChart;**BaseChart 退役刪除**;SVG 棘輪 31→26 檔;風格稽核 79→0(`FALLBACK_PAINT` 收斂);validate-ui-library 首次全綠(去 rg 依賴) |
| 2026-07-16 | `30e87f6` | **DataExplorer** 統計探索複合件(catalog 新分類 analytics)+ Bar/Line/Pie Canvas 化 |
| 2026-07-15 | `8d466e3` | **波 1**:HeatmapChart/ScatterChart/**ClusterGraph**(5000 節點實測 10.8ms/幀)+ quadtree/force-engine/color-scale/aggregation-engine 純函式核心 |
| 2026-07-14 | `5293873` | **波 0**:SVG 禁用政策 + G 類棘輪守門、`viz/CanvasChart.js` 新基底、`utils/theme-bus.js` 主題匯流排 |
| 2026-07-09 | `9e9b3fa` | **嚴格 CSP 全面達成**:159 處違規清零;`audit-csp.mjs` 守門員(A-F 類硬零) |
| 2026-07-08 | `cb22ba2` | main_0626+main_0707 合併(0707 優先裁決;0626 帶入 26 元件全數 CSP 修正後併入) |
| 2026-07-07 | `3ed9504` | 設計系統(palette 163 色)/Theme Studio/RWD 波/TGOSMapEditor/Leaflet+html2canvas vendoring/junction-dev+snapshot-publish 機制 v2.0.0/create-project 產生器 |

**目前狀態一句話:** catalog **115** 元件、CSP 六類**機器判定全零**、SVG 只剩 26 檔存量(棘輪鎖死只減不增)、瀏覽器驗收電池 67 項全綠。

---

## 三、守門員與驗收辦法(接手先會這個)

**改 `ui_components` 的任何 commit 前,依序跑:**

```bash
# 0) 靜態守門(秒級,無前置)
node tools/scripts/audit-csp.mjs            # CSP A-F 硬零 + G 類 SVG 棘輪
node tools/scripts/validate-ui-library.mjs  # 風格 token 稽核 + 裸 import + 公開面 + demo 引用
npm test                                    # 純函式單元(palette/color-scale/aggregation/force)

# 1) 瀏覽器電池(前置:repo 根起 python -m http.server 8124;Edge 無頭)
node tools/theme-studio/run.mjs             # 14 項:調校台/展示廊/舞台/匯出
node tools/scripts/canvas-chart-smoke.mjs   # 8 項:CanvasChart 基底(DPR/換膚/命中/匯出)
node tools/scripts/wave2-stage-sweep.mjs    # 29 項:重型圖表舞台 + 點擊詳情回歸 + 直掛
node tools/scripts/data-explorer-smoke.mjs  # 8 項:DataExplorer 全鏈
node tools/scripts/cluster-graph-perf.mjs   # 8 項:5000 節點效能 + 世界座標互動
```

**鐵律:**
- 「合規/完成」宣稱**只認機器判定**,不認人工掃描或代理回報。
- SVG 存量清一檔就跑 `node tools/scripts/audit-csp.mjs --write-baseline` 收緊棘輪。
- 新元件三件套(`<Name>.js` + `index.js` + `<Name>.manifest.json`)後必跑 `build-metadata.mjs`;**新分類要動兩處白名單**(`metadata/introspection.js` SEARCH_CATEGORIES + `manifest-schema.js` 分類枚舉)。
- Canvas 色回退禁散裝 hex,唯一來源 = theme-bus 的 `FALLBACK_PAINT`;亮度對比遮罩四常數(`#00000099/#ffffffcc/#000000aa/#ffffffdd`)已入 audit allow。

---

## 四、待辦(優先序 + 做法)

### 1. 波 3:SVG 存量清零(下一步,已規劃)
**目標:** 26 檔 181 處 → 0,G 類棘輪收硬零。
**做法(已定案):**
1. **先手寫 `common/Icon` 的 Canvas 版**(本人做,不發代理):Path2D 直接吃現有 55 條 path 字串、字串尺寸相容(`size:'sm'|數字`)、ThemeBus 換膚重繪、`Icon.register()` 契約不變。這是其餘 25 檔的共同依賴,先立標準。
2. **再發 ~5 路 sonnet 子代理**分檔清剩餘(Button 家族/Picker 家族/Panel/TreeList/WebTextEditor/OSMMapEditor/DrawingBoard…):inline `<svg>` 字串 → `new Icon()`;**檔案互斥分工、機器驗收條件寫進提示詞**(`grep -cE '<svg|createElementNS|data:image/svg'` = 0 + `node --check`)。
3. 特例:游標(cursor)→ PNG data URL;`LeafletMap` 加 `preferCanvas:true`;vendor/ 內的 SVG 不動(第三方豁免)。
4. 收尾:`--write-baseline` → 基線空 → 把 G 類改成硬零(audit-csp.mjs 內註記 TODO)。
5. 全電池重跑 + commit + push `main_0707`。

### 2. DataExplorer 擴充(波 3 後,本人做)
- `'cluster'` 圖型接 ClusterGraph(人→團體→上層團體鑽取;spec 加 `hierarchy` 通道)。
- 後端聚合模式:spec 直傳 Graph action(等後端軌 graph 端點成形)。
- ChartSpecBuilder 抽成獨立可註冊元件(表單另掛)。

### 3. 前端軌量產(等使用者發動)
- 按 `tim-web\docs\task-board.json`:P0-1 頁面聚類(排版+功能證據導向,約十群)→ 每群 `_base` 模板 → 生成器測試先行 → 量產頁 `extends` 只寫差異。
- 模板繼承協議與閘門已寫入 board(clusterProtocol、P{2,3,5}-BASE)。

### 4. 後端軌(等使用者發動)
- B0-0 威脅建模 → B0-1 舊碼盤點(TypeNameHandling/SSRF)→ B0-2 舊路由→10 動作對照表。
- 契約抽取釘 TIMSolution commit hash;快照測試把 STJ 輸出與 Newtonsoft 逐位對齊。

### 5. 掛帳小項
- `viz/index.js` 尚 re-export 大量 manual-only 元件——現況正確,無動作;若日後拆包再議。
- SKILL 包裝(A=全域開案 skill、B=範本內建 CLAUDE/AGENTS)已評估未實作,等指示。
- 機制 MCP 化(團隊中央腳手架服務)僅列為選項。

---

## 五、多代理波工作法(已驗證兩輪,照抄即可)

1. **分工:檔案互斥**——每代理一組不重疊檔案,絕不兩人碰同檔。
2. **提示詞內建驗收**——把機器驗收命令與通過標準寫進代理提示(grep 斷言 + node --check + 行為要點),要求代理自跑後回報輸出。
3. **上限 6 代理/波**;模型:機械遷移用 sonnet,設計判斷留本人(或 opus)。
4. **回報不算數**——波收束後由本人逐檔機器驗收 + 瀏覽器行為驗證。波 2 實例:代理全數回報成功,實測仍揪出兩個行為 bug(`ModalPanel.alert({content})` 被覆蓋、`_handleClick` 沒接線)——**渲染全綠也測不出,必須做互動鏈斷言**。
5. **代理死於 session 上限會留半成品**(曾發生 FeedCard 有 `<link>` 沒 css 檔):逐檔驗收,不能只看回報清單。

---

## 六、環境與慣例(坑都踩過了)

- **git:** sparse-checkout(`packages templates tools` + 根檔案);push 慣例=**不直推 main,推 `main_月日` 日期分支**(現用 `main_0707`)。`tim-web` 不上本 repo 版控。
- **本機沒有 rg**:工具腳本一律純 Node(`audit-ui-style-rules.mjs` 已去 rg 化);新腳本禁 spawn 外部工具。
- **PS 5.1:** `.ps1` 只寫英文註解(UTF-8 無 BOM 中文=parse 炸);改檔一律用工具(Read/Edit/Write),禁 PS 重導向寫檔(UTF-16 毀損坑)。機制腳本已全數 Node 化,PS 僅剩 `tim-web\scripts\build.ps1`。
- **瀏覽器測試:** playwright-core 借 `tim-web/poc/node_modules`(repo 本身零 devDeps);channel=msedge;`node --check` 不吃瀏覽器 ESM `.js`,先複製成 `.mjs` 再驗。
- **vendor/ 豁免一切庫規範**(CSP 掃描、風格稽核、裸 import 驗證都跳過)。
- **民國曆:** DatePicker 用 `format:'taiwan'`;後端回民國字串前端原樣輸出,禁 `new Date()` 轉換;時間聚合**先排序再分組**(民國標籤字典序不可靠)。
- **含 junction 的專案刪除前必先解除連結**(`dev-link` unlink),否則遞迴刪除會追進腳手架。
- Theme Studio 測試鉤子:`window.__ts.openStage(名)` + `window.__stageInst`;展廊卡片有覆蓋層,harness 選擇器要鎖自建容器(如 `#cc-smoke`),別全頁裸選 canvas。
