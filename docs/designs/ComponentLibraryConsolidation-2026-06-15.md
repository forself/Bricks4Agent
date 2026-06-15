# 組件庫整併與重構規劃(Component Library Consolidation)

Date: 2026-06-15
Status: **重新分析 + 規劃(待分階段執行)**
基準:`packages/javascript/browser/ui_components`(B,183 檔、~38.8K LOC)為 canonical。
範圍:把產生器側那套 C# schema 詞表(A)有價值的部分**拆解後吸收**進 B,移除其罐頭詞表與現捏邏輯,並同步文件。

---

## 0. 為什麼整併

目前有**兩套互不相連的「組件庫」**:

- **A — 產生器 schema 詞表(C#)**:`ComponentLibraryManifest` + 約 30 個版面區塊型別(`HeroSection`、`NewsGrid`、`MegaHeader`…),渲染語意是內嵌在 [StaticSitePackageGenerator](../../packages/csharp/workers/site-crawler-worker/Services/StaticSitePackageGenerator.cs) 的 JS 字串。Demo #3(網站複製)用它。
- **B — `ui_components`(JS)**:15 類真實可重用組件,中文註解、刻意的 `createComponentState` FSM 契約、`BaseChart`/`BasePanel` 階層、vanilla 無依賴、強資安。page-generator 用它。

**A 不是規劃的詞表,是語料 sample 出來的罐頭。** `HeroSection`/`hero` 是英文西方 landing-page 行話,非設計;`SiteGeneratorConverter.EnsureGeneratedComponent` 還會在 role 湊不上時**現捏** `Generated{Role}Section` 塞進 manifest(射箭再畫靶,且目前在孤兒路徑上)。B 才是設計的那條。本規劃以 B 為基準,把 A 的**紀律與組裝模式**留下、**詞表與現捏**丟掉。

---

## 1. 現況盤點

### A(待拆解吸收)
| 部分 | 處置 |
|------|------|
| 組件詞表(`HeroSection` 等罐頭型別) | **丟**。非規劃,語料 sample。 |
| `EnsureGeneratedComponent` 現捏 | **丟**(且是死碼,見下)。 |
| 內嵌 JS 字串 renderer | **退役**,改用 B 的組件。 |
| schema 驗證紀律([ComponentLibraryLoader.Validate](../../packages/csharp/workers/site-crawler-worker/Services/ComponentLibraryLoader.cs),fail-closed) | **留**,套到 B 的詞表上。 |
| 決定性紀律(Demo #3 位元組穩定) | **留**,產出仍須位元組決定。 |
| 頁面組裝模式(header/sections/forms/footer → 節點樹,`TemplateCompiler`) | **拆解吸收**:改成「以 B 組件組成的 section 樣板」。 |

> 死碼註記:`SiteGeneratorConverter.Convert` 委派給 `TemplateCompiler`;`SiteGeneratorConverter.BuildRoute → BuildSectionNode → EnsureGeneratedComponent` 整串**無活的呼叫者**,是先前版本遺留。Stage 0 直接刪。`TemplateCompiler`(活路)是否仍現捏 / 整段灌 body,Stage 0 一併實讀釘死。

### B(canonical,但有自身債)
- 入口 [ui_components/index.js](../../packages/javascript/browser/ui_components/index.js) 統一匯出 15 類;狀態契約 [component-state.js](../../packages/javascript/browser/ui_components/utils/component-state.js)(`createComponentState`:`snapshot/send/subscribe/replace`,transition 回傳下一狀態)。
- 債:① `index.js` 公開面不齊(viz/utils/binding 例外);② generator 耦合實作檔路徑、繞過 `index.js`;③ 隨機 id(`Notification`/`Tooltip`/`WebTextEditor` 用 `Math.random()`+`Date.now()`)非決定;④ 測試 5/183,viz/editor/map 等大組件全沒測。

---

## 2. 整併決策(接回固定詞表 / fail-closed 那套)

1. **B 的組件清單 = 固定詞表(由人框,非 sample)。** A 的罐頭詞表不吸收。
2. **複合元件是一級公民(= 樹的內節點 / 定言組),不是樣板。** B 本來就富含複合(Panel 家族、DataTable、SearchForm、FormField、ChainedInput、FeatureCard、BaseChart 家族…),它們留。只有「某次爬取才出現、無重用」的區段組裝才是 generator 側樣板。`HeroSection` 這種**區段級** composite 走 §2.5 的升 / 降 / 丟判準。
3. **schema 驗證 + 決定性紀律保留,套到 B 詞表上。** section 樣板對「B 組件閉集」做 schema 驗證、輸出位元組決定。
4. **移除現捏。** role 湊不上 → 退最近的 B 組件 **或** 拒收 + flag(**fail-closed**),永不發明組件。
5. **去罐頭命名。** `hero` 等行話丟掉,用 B 既有組件名 + 規劃詞彙。

對應前面整段結論:**B 詞表 = 固定原子;section 樣板 = 組裝文法;generator = 對它的確定性編譯器(parser + 求值);不再 sample、不再現捏。**

---

## 2.5 三層:原子 / 複合 / 區段複合

接公設 2 的樹:**複合元件 = 內節點(定言組)、區段 = 祖/根(定言祖)、原子 = 葉。** 同一棵樹、同一套紀律:子節點來自閉集、props 固定 schema、決定性、不現捏。差別只在 leaf vs 非 leaf。

| 層 | 是什麼 | 處置 |
|----|--------|------|
| 原子(葉) | `TextInput`、`ImageBlock`、`ActionButton`、`Badge`… | B 既有,留 |
| 中層複合(內節點) | `Panel`/`DataTable`/`SearchForm`/`FormField`/`ChainedInput`/`FeatureCard`/`BaseChart`… | **B 既有公民,留,不動** |
| 區段複合(祖) | hero banner、card grid section、tabbed news… | A 的領域,逐一判(下) |

**區段複合三判準(逐一,不整批升格):**
- **升 B 複合**:真可重用 + 契約乾淨 + **由你命名框定** → 進 B 新開一層(`sections/` 或 `blocks/`),當正式複合元件。
- **降 generator 樣板**:只出現一次、無重用 → generator 側組裝食譜,不進庫,**永不現捏**(fail-closed)。
- **丟**:罐頭(`hero` 命名、`Generated*` 那些)。

**框定那刀在這層最關鍵**:「區段複合層要有哪些、各叫什麼」是設計 act,由你框,**不是把 A 那 30 個語料清單自動升格**。generator 對不上你框的區段複合 → fail-closed 退中層複合或拒收,不發明。

## 2.6 複合元件契約(成為複合的條件)

1. **完全由原子組合**:其**完整展開的葉子全是原子**(無非原子葉、無 raw HTML、無 bespoke code)。本質是「原子組裝的巨集指令 / 快取化模板」——定義可用子巨集(其他複合)以求 DRY,但展開後一律見底到原子。
2. **捕捉常用模式**:達到常見 / 常用的功能或視覺組合;只用一次的不該是複合(直接 inline 原子即可)。複合靠**重用**攤提它的存在。
3. **確定性展開**:同 props → 位元組相同的原子樹(無 `random`/`Date`/順序依賴)。「快取化」只對純函式成立——能被 cache 的前提,就是展開是 props 的純函式。
4. **本身是一條閉合定言**:固定 prop schema(輸入)+ 只展開到既有原子(展開詞表閉集),**不現捏**。
5. **不補洞(fail-closed)**:只透過原子的公開契約組裝;現有原子表達不了某模式時,**缺的是一個原子(框定決定),不是在複合裡寫 code / HTML**。
6. **狀態歸原子**:狀態住在原子各自的 `createComponentState` FSM;複合只做結構 + 事件佈線,自身不持久狀態(這樣才能被當純展開來 cache)。
7. **命名由你框**:名稱與存在是設計 act,非語料 sample(不再有 `hero`)。

## 3. 目標流程

```
爬取結果 → 抽取(role / 內容 / 媒體 / 連結)
        → role 對「固定映射表」查 section 樣板        ← 閉集;湊不上 = fail-closed
        → 樣板以 B 組件組裝成節點樹                    ← 組件全來自 ui_components
        → schema 驗證(對 B 閉集)+ 位元組決定輸出
        → 產出:bundle B 的靜態包 / 或 import B 的程式
```

不再有 A 的 bespoke 詞表、不再有內嵌 renderer、不再有 `EnsureGeneratedComponent`。

---

## 4. 分階段重構(不用重來)

- **Stage 0 — 清理 + 標定(低風險)**:實讀 `TemplateCompiler` 釘死「活路是否現捏 / 整段灌 body」;刪 `SiteGeneratorConverter` 孤兒方法(含 `EnsureGeneratedComponent`);把 A 的罐頭詞表標 deprecated;文件改向 B canonical。
- **Stage 1 — 詞表 + 映射**:以 B 組件清單定義固定詞表;建 `role → B 組件組裝樣板` 的固定映射(取代 hero 等);移除所有 fabrication 路徑,改 fail-closed。
- **Stage 2 — 產出改用 B**:site-replica 產出改成 B 組件的組裝(bundle `ui_components` 進靜態包),退役 `StaticSitePackageGenerator` 內嵌 renderer;保留位元組決定。
- **Stage 3 — B 自身的債**:`index.js` 面拉齊;隨機 id 抽成可注入(determinism-clean);viz/editor/map 補測試。

每階段:測試先行、各自 commit、跑驗證、**改對應文件**。

---

## 5. 受影響文件(異動時同步)

- 本檔(新增)。
- [ComponentLibraryPublicSurface.md](ComponentLibraryPublicSurface.md) → 標 B canonical + 公開面拉齊計畫。
- [GeneratorComponentLibraryUsage.md](GeneratorComponentLibraryUsage.md) → generator 改用 B + 退役內嵌 renderer。
- [DemoFeaturesStrengthening-2026-06-14.md](DemoFeaturesStrengthening-2026-06-14.md) 與架構報告 → 校正 #3「組件庫複製」定義:從「自訂詞表」改成「基於 `ui_components` 的組裝」。

## 6. 驗收

- 產出不再出現 bespoke / 語料詞表(`hero` 等消失);詞表 = B 的閉集;role 湊不上 **fail-closed**;產出仍位元組決定;對應文件同步更新。

---

## 7. 詞表盤點與補齊提案(**proposal,待框定**)

我參考常見網站(Google/YouTube/Amazon/GitHub/Wikipedia/政府入口/新聞/儀表板)整理常用模式,**與 B 既有交叉比對**只列缺口、名稱為提案。**這是檢索提案,不是已框定詞表——由你選 / 改名 / 拒。**

### B 現況(已相當完整,不重列)
按鈕族、表單原子(Text/Number/Checkbox/Radio/Toggle/Dropdown/Date/Time/Color/MultiSelect)、Badge/Tag/Divider/Tooltip/LoadingSpinner/Progress/ImageViewer/Avatar;複合:FeatureCard/PhotoCard/StatCard、Breadcrumb/Pagination、SimpleDialog/Notification、FormField/SearchForm、DataTable/TabContainer/Panel 家族/SideMenu、ConnectionCard/FeedCard/Timeline、charts、複合輸入(Address/Chained/PersonInfoList…)。

### 7.1 原子缺口(提案名,待定)
| 提案原子 | 對應常見模式 | B 現況 |
|---|---|---|
| `Icon` | 圖示系統(幾乎每站) | ✅ **已加**(`common/Icon`,固定 SVG 閉集,未知=fail-closed) |
| `Link` | 受控連結(帶 scope / 安全;site-gen 正需要) | ✅ **已加**(`common/Link`,scope + 協定白名單) |
| `Text` / `Heading` | 排版原子(字級 / 標題 / 段落) | ✅ **已加**(`common/Text`、`common/Heading`) |
| `Textarea` | 多行文字 | 缺(只有 `TextInput`) |
| `Slider` | 範圍滑桿 | 缺 |
| `Skeleton` | 載入骨架 | 缺(只有 spinner) |
| `Rating` | 星等 | 缺 |
| `MediaPlayer` | 影音 | 缺 |
| `CodeBlock` | 程式碼區塊 | 缺 |

> **第一批(基礎原子)已落地**:`Icon`/`Link`/`Text`/`Heading`,皆 `createComponentState` FSM、idempotent 樣式、無 random/Date(確定性),`FoundationAtoms.test.js` 13 例全綠。

### 7.2 中層複合缺口(提案;= 原子展開)
| 提案複合 | = 哪些原子 | 對應模式 | B 現況 |
|---|---|---|---|
| `Alert`(行內) | `Icon`+`Text`+close | 行內訊息條(vs `Notification`=toast) | 缺 |
| `EmptyState` | `Icon`+`Heading`+`Text`+`ActionButton` | 空狀態 | 缺 |
| `DropdownMenu` | button +(`Link`/`ActionButton` 清單) | 動作選單(vs `Dropdown`=select) | 缺 |
| `CardGrid` | grid of `FeatureCard`/`PhotoCard` | 卡片網格 | 部分(`DocumentWall`/`PhotoWall` 特例) |
| `List`/`ListItem` | repeat(`Avatar?`/`Text`/`Badge`/`ActionButton`) | 通用清單 | 部分(`TreeList`/`ListInput`) |
| `FilterBar` | repeat(`Dropdown`/`Checkbox`/`Tag`) | 篩選列 | 缺 |
| `DescriptionList` | repeat(`Text` label + `Text` value) | 鍵值表 | 缺 |
| `StatGrid` | grid of `StatCard` | 指標網格 | 部分(`StatCard` 單張) |
| `TagInput` | `TextInput`+repeat `Tag` | 多標籤輸入 | 缺 |
| `StepIndicator` | repeat(`Badge`/`Text`+`Divider`) | 步驟指示 | 部分(`WorkflowPanel`) |

### 7.3 區段複合候選(§2.5 那層,**由你框 + 命名**)
| 候選 | = 哪些(中層複合 + 原子) | B 現況 |
|---|---|---|
| 站頭導覽 | logo + 主選單(`Link`)+ `SearchForm` + `Avatar`/`AuthButton` | 缺(`SideMenu` 是側邊) |
| 站尾 | 連結群組 + 版權 `Text` + logo | 缺 |
| 橫幅(原 `hero`,**等你命名**) | 媒體(`ImageViewer`)+`Heading`+`Text`+`ActionButton` | 缺 |
| 內容區 | `Heading`+`Text`+ 媒體 | 部分 |

> 區段複合層的「有哪些、各叫什麼」是你的框定;7.3 只是候選,不自動升格。
