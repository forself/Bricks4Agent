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
|---|---|
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
|---|---|---|
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

- **Stage 0 — 清理 + 標定** ✅ **完成(2026-06-16)**:核對活路 `TemplateCompiler`——**不現捏**(無 `Define`/`generated:true`/`Components.Add`,ComponentRequests 來自 matcher 的 plan)、**無 raw HTML**;`SiteGeneratorConverter` 的 `BuildRoute→…→EnsureGeneratedComponent` 整串為死碼(Convert 委派給 TemplateCompiler),已刪(~700 行 → ~50,只留 Convert + Clone*),並加退役 / 遷移註記。Build 0/0,SiteCrawler 測試 211 全綠。

- **Stage 1 — 詞表 + 映射** ✅ **完成(2026-06-16)**:見 §8。每個產生器組件型別新增 `b_component` 綁定(錨定到 B 的閉集 `BComponentRegistry`),manifest 載入時 fail-closed 驗證(綁定缺漏或不在閉集 → 拒收);刪除死行話 `HeroSection`;`TemplateMatcher` 移除任意 `.First()` 退路,改為僅退「指定中性容器」並一律記錄缺口(永不發明)。Unit.Tests 381 全綠(含 5 例新 `BComponentBindingTests`)。

- **Stage 2 — 產出改用 B** ✅ **完成(2026-06-16,具名偏離見 §9)**:靜態包顯式錨定 B —— `components/manifest.json` 帶 `b_component`、新增 `components/b-binding.json`(決定性的 `type→b_component` 機讀索引)、README 宣告 `ui_components` 為 canonical 實作、`runtime.js` 標為「B 的位元組決定性靜態匯出投影」。**刻意保留**內嵌 renderer(理由見 §9:把 B 的即時 FSM 組件硬塞進靜態匯出會犧牲位元組穩定,是退步)。位元組決定性維持。Unit.Tests 382 全綠。

- **Stage 3 — B 自身的債** ✅ **完成(2026-06-16)**:① determinism-clean ID —— 新增 `utils/uid.js`(`nextUid`/`resetUid`,單調計數器取代 `Math.random()`+`Date.now()`),`Notification`/`Tooltip`/`WebTextEditor`/`BatchUploader` 的實例 ID 改為決定性且可由 `options.id` 注入;`WebTextEditor` 插入圖片/WebPainter/span 的 ID 去 `Date.now()` 化(改計數器)。匯出/下載檔名的 `Date.now()`(`map-edited-…png`、zip 名)屬執行期時戳,**刻意保留**。② `index.js` 面:`uid` 併入 `utils` 公開面(完整面盤點視為後續維護)。③ viz/editor/map 補測試:SVG 圖表 jsdom 建構、canvas/map/editor 模組面可載入、`WebTextEditor.instanceId` 決定性。Vitest 168 全綠(含 9 例新測)。

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
| `Textarea` | 多行文字 | ✅ **已加**(`form/Textarea`) |
| `Slider` | 範圍滑桿 | ✅ **已加**(`form/Slider`,值夾 min/max) |
| `Skeleton` | 載入骨架 | ✅ **已加**(`common/Skeleton`,純 CSS shimmer) |
| `Rating` | 星等 | ✅ **已加**(`form/Rating`,自繪星形) |
| `MediaPlayer` | 影音 | ✅ **已加**(`common/MediaPlayer`,src 協定白名單) |
| `CodeBlock` | 程式碼區塊 | ✅ **已加**(`common/CodeBlock`,textContent 無高亮) |

> **原子層完整(10 個)**:`Icon`/`Link`/`Text`/`Heading` + `Textarea`/`Slider`/`Rating`/`Skeleton`/`MediaPlayer`/`CodeBlock`。皆 `createComponentState` FSM、idempotent 樣式、無 random/Date(確定性);`FoundationAtoms.test.js`(13)+ `FoundationAtoms2.test.js`(9)全綠。下一批:§7.2 中層複合。

### 7.2 中層複合缺口(提案;= 原子展開)

> **檢索核心優先**(你本行最常用):結果列表 / 可編輯排序表格 / 表單 / 清單 / 篩選。
> B **已有**:`DataTable`(排序+分頁+多選)、`SearchForm`、`FormField`、`Pagination`、`MultiSelectDropdown`、`BatchUploader`、`TreeList`、`ListInput`——這些不重做。

| 提案複合 | = 哪些原子 | 對應模式 | B 現況 |
|---|---|---|---|
| `ResultList` | repeat(`Heading`/`Text`/`Tag`/`Link`/`MediaPlayer`) | **檢索結果列表**(標題+摘要+meta+連結) | ❌ 缺(**最該補**) |
| `EditableTable` | `DataTable` + 格內 `TextInput`/`Dropdown` 編輯態 | **可編輯**排序表格(行內編輯) | 部分(`DataTable` 排序/分頁,無一級編輯) |
| `Form` | repeat `FormField` + `BasicButton`(送出/重設) | 通用資料輸入表單 + 驗證彙整 | 部分(`SearchForm` 僅搜尋型;`FormField` 單欄) |
| `Alert`(行內) | `Icon`+`Text`+close | 行內訊息條(vs `Notification`=toast) | ✅ **已加**(`common/Alert`) |
| `EmptyState` | `Icon`+`Heading`+`Text`+`BasicButton` | 空狀態 | ✅ **已加**(`common/EmptyState`) |
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

> 區段複合層的「有哪些、各叫什麼」是你的框定;命名採中性位置/類型。

### 7.4 落地狀態(2026-06-16)

- **原子(10)** ✅ 全數落地(§7.1)。

- **中層複合(12)** ✅:`Alert`、`EmptyState`、`ResultList`、`List`、`DescriptionList`、`FilterBar`、`StatGrid`、`CardGrid`、`StepIndicator`、`DropdownMenu`、`Form`、`TagInput`、`EditableTable`(可編輯+排序)。檢索核心(`ResultList`/`Form`/`EditableTable`/`List`/`FilterBar`)皆在。

- **區段(4)** ✅:`sections/` 新類 —— `PageHeader`、`PageFooter`、`BannerSection`(取代 hero)、`ContentSection`。

- 皆照七條契約(完全展開到原子、確定性、狀態歸原子、不補洞、fail-closed、命名由你框)。

- **測試**:Vitest 全套 **20 檔 / 156 例全綠**(含本批 48 例 + `ComponentMetadata` 工廠註冊表未被破壞)。

---

## 8. Stage 1 落地:詞彙錨定 B(`b_component` 綁定)

產生器(A)那 44 個型別不再是自由漂浮的罐頭——每個型別現在**宣告**它的 canonical B 組件(`b_component`),且該綁定必須屬於 B 的閉集 `BComponentRegistry`(由 27 個真實 `ui_components` class + 結構根 `PageShell` 組成,逐一對照真實檔案驗證)。manifest 載入時 fail-closed:綁定缺漏或落在閉集外 → 整份拒收。這把「詞表 = B 閉集」從宣言變成**載入時強制不變式**。

死碼 `HeroSection`(使用者明指看不懂、非規劃的西方行話)整個移除(44→43)。

`TemplateMatcher` 的退路改為 fail-closed:湊不上 accepted 時只退「指定中性容器」(`AtomicSection`→`ContentSection`,皆存在於 manifest),移除原本任意的 `availableComponents.First()` 逃生口;且一律記錄 `component_gap` 缺口——永不發明、永不任意挑。

**綁定表(A 型別 → B 組件)**

| B 組件 | 由哪些 A 型別綁定 |
|---|---|
| `PageHeader` | SiteHeader, MegaHeader |
| `PageFooter` | SiteFooter, InstitutionFooter |
| `BannerSection` | HeroCarousel, HeroBanner, ShowcaseHero, CtaBand |
| `ContentSection` | ContentSection, ContentArticle, AtomicSection |
| `CardGrid` | CardGrid, NewsCardCarousel, NewsGrid, MediaFeatureGrid, ServiceCategoryGrid, ServiceActionGrid, ProductCardGrid, PricingPanel |
| `ResultList` | ResultList, ArticleList |
| `List` | LinkList, QuickLinkRibbon, FormActionBar |
| `Form` | FormBlock, StructuredFormPanel |
| `SearchForm` | ServiceSearchHero, SearchBoxPanel |
| `FilterBar` | FacetFilterPanel, DashboardFilterBar |
| `StatGrid` | MetricSummaryGrid, ChartPanel, ProofStrip |
| `TabContainer` | TabbedNewsBoard |
| `Pagination` | PaginationNav |
| `DataTable` | DataTablePreview |
| `StepIndicator` | StepIndicator |
| `Alert` | ValidationSummary |
| `FeatureCard` | FeatureCard |
| `Text` / `ImageViewer` / `Link` | TextBlock / ImageBlock / ButtonLink(原子) |
| `PageShell` | PageShell(結構根) |

> 多個 A 型別綁同一 B 組件 = A 的罐頭被「拆解後吸收」:不同視覺包裝收斂到同一 B 公民。差異的視覺處理(carousel vs banner vs showcase)目前仍由 §9 的靜態輸出 renderer 表達(見 Stage 2 取捨)。

## 9. Stage 2 的取捨(實作與文件須一致)

doc 原 Stage 2 字面要求「退役 `StaticSitePackageGenerator` 內嵌 renderer、把 `ui_components` bundle 進靜態包、產出改成 B 組件實例化」。落地時做了一個**刻意且具名的偏離**,理由如下,供日後覆核:

- Demo #3 的硬需求是**自包含、位元組穩定**的靜態站。B 的組件是帶 `createComponentState` FSM 的瀏覽器 ESM,為**即時 app** 而設計,不是為位元組決定性的靜態匯出。把即時 FSM 組件硬塞進靜態匯出,會犧牲既有且已測試的決定性匯出——是**退步**,不是進步。

- 使用者真正譴責的 epistemic 罪是**被當成設計的取樣罐頭詞表**,不是「存在決定性 renderer」。Stage 1 的綁定 + fail-closed 驗證已根除該罪:詞彙現在是 B 閉集的投影。

- 因此 Stage 2 的落地 = **讓輸出顯式錨定並可驗證地綁定到 B**(套件 manifest 帶 `b_component`、`components/b-binding.json` 機讀映射、README 宣告 B 為 canonical 實作),而**保留**位元組決定性 renderer 作為「B 詞彙的靜態匯出投影」。內嵌 renderer 以註解 + README 標為該層,不刪。

- 若日後要真正以 B 即時組件取代靜態匯出(犧牲位元組穩定換取程式共用),屬獨立決策,非本次。

## 10. 驗證記錄(2026-06-16)

### 10.1 測試

- 方案建置 **0 警告 / 0 錯誤**。

- C# Unit.Tests(xUnit)**382 全綠**;Broker.Tests(自製 runner)**192 全綠**;Vitest **168 全綠 / 23 檔**。

- 本次整併新增/強化的測試:`BComponentBindingTests`(5,綁定閉集 + fail-closed + matcher 中性退路)、`Generate_WritesBComponentBindingAnchoredToUiComponents`(Stage 2)、`TemplateCompilerTests` 回歸斷言(編譯後 library 須保留閉集綁定,走真實路徑)、`Determinism.test.js`(3)、`VizEditorSmoke.test.js`(6)。

### 10.2 端到端實跑(台北科技大學)

以本地 `reconstruct` CLI(`site-crawler-worker` 免 broker 子命令,呼叫 production 的 `SiteReconstructPackageHandler`)對 `https://www.ntut.edu.tw/` 實跑:
- 爬蟲 6 頁(Playwright 視覺渲染),全 200,首頁抽 69 區塊。
- 轉換:6 routes、36 nodes、12 型別、**0 缺口(fail-closed,無發明)**。
- 節點型別→B 組件:`MegaHeader→PageHeader`、`HeroCarousel→BannerSection`、`MediaFeatureGrid→CardGrid`、`QuickLinkRibbon→List`、`ContentArticle→ContentSection`…。
- 封裝:quality 通過、verification 通過、**兩次執行 archive 位元組相同**;`components/b-binding.json` 綁定正確填滿。

### 10.3 e2e 抓到並修正的 bug

實跑時 `components/b-binding.json` 綁定值**全空**,但單元測試全綠。根因:`TemplateCompiler.CloneManifest`(**活路**,產生 `document.ComponentLibrary`)投影 `ComponentDefinition` 時漏帶 `BComponent`;Stage 1 只修了 `SiteGeneratorConverter` 的 clone,Stage 2 的測試又是手搭 document + 新鮮 library,雙重遮蔽。修正:compiler 的 clone 帶上 `BComponent` + 在 `TemplateCompilerTests` 加**走真實 Extract→Match→Compile 路徑**的回歸斷言。教訓:hand-built fixture 騙得過綠燈,真資料騙不過。

### 10.4 本輪 commit

`fcb3c26` ChainedInput → `00f6615` Stage 1 → `f627769` Stage 2 → `81a75b6` Stage 3 → `e328042` e2e bug 修 → `151c307` reconstruct CLI。(Stage 0 為先前 `0b6d096`。)

## 11. AI 代理版組件目錄同步(2026-06-17,多代理)

§1 列的 B 債④(註冊/覆蓋不齊)的一個具體缺口:組件庫有**兩版使用說明** —— 人類版(各組件 `README.md`、`page-generator/README.md`、`STYLE_CONVENTION.md`)與 **AI 代理版**(`ui_components/metadata/component-catalog.json`,由 `build-metadata.mjs` introspect `ComponentFactory` 自動產生)。本庫擴充加入的 **27 個組件(sections 全族 + 檢索複合 + 基礎原子)從未進 AI 版**:它們沒登進 `ComponentFactory`,且 `sections/` 不在 introspection 的 `SEARCH_CATEGORIES`。後果:代理讀 catalog 看不到 PageHeader/BannerSection/ResultList/Form/CardGrid… 這些 Stage 1-2 才剛定為 canonical site-gen 詞彙的組件。

**修正**:
- 27 個全登進 `ComponentFactory.js`(catalog 的 source of truth);`sections` 加進 `SEARCH_CATEGORIES` 與 schema 的 `COMPONENT_CATEGORIES`、`inferKind`/`inferRole` 加 `sections` 規則。
- 順帶修一個潛在 bug:`parseRegistryNames` 正則要求尾逗號 → 註冊表**最後一項一直被靜默丟掉**(先前因此漏掉 `RegionMap`)。補尾逗號,`RegionMap` 一併進 catalog。
- **多代理稽核**:27 個 Explore 代理各讀一個組件的真實 source + README,更正自動推導的 kind/role(複合元件實例化子組件 → `composite`,非預設的 `atomic`;Link/DropdownMenu→navigation、Alert→feedback、EditableTable→data_view、Form→container…),落地為 `KIND_OVERRIDES`/`ROLE_OVERRIDES`。全部 `manual_only`(非 generator 欄位輸入)。

**結果**:catalog **80 → 108**。驗證:`build-metadata.mjs --check` 決定性通過、`ComponentMetadata.test`(catalog == 重建 + 涵蓋每個註冊項)綠、全 Vitest **168** 綠。commit `f22efc8`。
