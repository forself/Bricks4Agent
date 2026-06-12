# 網站模板框架

模板框架位於元件庫之上，負責把視覺意圖轉成頁面骨架與 slot 合約。模板不建立元件，只能引用已載入元件庫 manifest 中宣告的元件型別。

模板依視覺與功能版型選擇，不依網站類別選擇。內建模板使用 `pattern_tags`；`supported_site_kinds` 只保留為舊 manifest 的相容欄位，不參與模板評分。

## 模板

### `hero_news_portal`

適用於首頁由頁首、大型 hero 或輪播、快速連結、新聞或卡片輪播、內容/特色區與頁尾構成的版型。

首頁 slots:

- `header`: `MegaHeader` 或 `SiteHeader`
- `hero`: `HeroCarousel`, `HeroBanner`, 或 `AtomicSection`
- `quick_links`: `QuickLinkRibbon`, `CardGrid`, 或 `AtomicSection`
- `news`: `NewsCardCarousel`, `NewsGrid`, 或 `CardGrid`
- `features`: `MediaFeatureGrid`, `CardGrid`, 或 `AtomicSection`
- `content`: `ContentArticle`, `ContentSection`, 或 `AtomicSection`
- `footer`: `InstitutionFooter` 或 `SiteFooter`

### `search_service_portal`

適用於有可選 hero、明顯搜尋區、服務/分類卡片、行動捷徑、分頁公告、新聞/卡片區與頁尾的版型。

首頁 slots:

- `header`: `MegaHeader` 或 `SiteHeader`
- `hero`: `HeroCarousel`, `HeroBanner`, 或 `AtomicSection`
- `search`: `ServiceSearchHero`, `HeroBanner`, 或 `AtomicSection`
- `news`: `NewsCardCarousel`, `NewsGrid`, 或 `CardGrid`
- `tabbed_news`: `TabbedNewsBoard`, `NewsGrid`, `ArticleList`, 或 `CardGrid`
- `service_categories`: `ServiceCategoryGrid`, `CardGrid`, 或 `AtomicSection`
- `service_actions`: `ServiceActionGrid`, `QuickLinkRibbon`, `CardGrid`, 或 `AtomicSection`
- `features`: `MediaFeatureGrid`, `ServiceCategoryGrid`, `CardGrid`, 或 `AtomicSection`
- `footer`: `InstitutionFooter` 或 `SiteFooter`

### `service_action_portal`

適用於 hero 加強行動捷徑，後續接分頁公告/新聞、服務分類、特色卡片與頁尾的版型。

首頁 slots:

- `header`: `MegaHeader` 或 `SiteHeader`
- `hero`: `HeroCarousel`, `HeroBanner`, 或 `AtomicSection`
- `service_actions`: `ServiceActionGrid`, `QuickLinkRibbon`, `CardGrid`, 或 `AtomicSection`
- `news`: `NewsCardCarousel`, `NewsGrid`, 或 `CardGrid`
- `tabbed_news`: `TabbedNewsBoard`, `NewsGrid`, `ArticleList`, 或 `CardGrid`
- `features`: `MediaFeatureGrid`, `CardGrid`, 或 `AtomicSection`
- `service_categories`: `ServiceCategoryGrid`, `CardGrid`, 或 `AtomicSection`
- `footer`: `InstitutionFooter` 或 `SiteFooter`

### `search_results_portal`

適用於關鍵字搜尋、篩選器、搜尋結果列/卡片、分頁或結果導覽構成的版型。

首頁 slots:

- `header`: `MegaHeader` 或 `SiteHeader`
- `search_box`: `SearchBoxPanel`, `ServiceSearchHero`, 或 `AtomicSection`
- `filter_panel`: `FacetFilterPanel` 或 `AtomicSection`
- `result_list`: `ResultList`, `ArticleList`, `CardGrid`, 或 `AtomicSection`
- `pagination`: `PaginationNav`, `QuickLinkRibbon`, 或 `AtomicSection`
- `footer`: `InstitutionFooter` 或 `SiteFooter`

### `report_dashboard`

適用於篩選列、KPI 指標、圖表面板、資料表預覽與報表操作構成的版型。

首頁 slots:

- `header`: `MegaHeader` 或 `SiteHeader`
- `filter_bar`: `DashboardFilterBar`, `FacetFilterPanel`, 或 `AtomicSection`
- `metric_summary`: `MetricSummaryGrid` 或 `AtomicSection`
- `chart_panel`: `ChartPanel`, `MediaFeatureGrid`, 或 `AtomicSection`
- `data_table`: `DataTablePreview`, `ArticleList`, 或 `AtomicSection`
- `footer`: `InstitutionFooter` 或 `SiteFooter`

### `input_flow`

適用於步驟/進度指示、表單欄位、驗證提示與流程操作構成的版型。

首頁 slots:

- `header`: `MegaHeader` 或 `SiteHeader`
- `step_indicator`: `StepIndicator` 或 `AtomicSection`
- `form_fields`: `StructuredFormPanel`, `FormBlock`, 或 `AtomicSection`
- `validation_summary`: `ValidationSummary`, `ContentSection`, 或 `AtomicSection`
- `action_bar`: `FormActionBar`, `QuickLinkRibbon`, 或 `AtomicSection`
- `footer`: `InstitutionFooter` 或 `SiteFooter`

### `commercial_showcase`

適用於產品/方案 hero、產品卡片、特色格狀區、信任背書、價格方案與 CTA 區構成的版型。

首頁 slots:

- `header`: `MegaHeader` 或 `SiteHeader`
- `showcase_hero`: `ShowcaseHero`, `HeroBanner`, 或 `AtomicSection`
- `product_cards`: `ProductCardGrid`, `MediaFeatureGrid`, `CardGrid`, 或 `AtomicSection`
- `feature_grid`: `MediaFeatureGrid`, `CardGrid`, 或 `AtomicSection`
- `proof_strip`: `ProofStrip`, `MetricSummaryGrid`, 或 `AtomicSection`
- `pricing_panel`: `PricingPanel`, `ProductCardGrid`, `CardGrid`, 或 `AtomicSection`
- `cta_band`: `CtaBand`, `QuickLinkRibbon`, 或 `AtomicSection`
- `footer`: `InstitutionFooter` 或 `SiteFooter`

## 選擇規則

- `SiteIntentExtractor` 會抽取 `hero`, `search`, `service_categories`, `service_actions`, `tabbed_news`, `quick_links`, `news` 等視覺/功能區塊。
- `TemplateMatcher` 只依這些區塊與頁面 intent 可填入的 slots 評分。
- slot 只能選擇 `bricks4agent.default/manifest.json` 或明確載入的 manifest 中宣告的元件。
- 若偏好的元件不存在，會回報 `ComponentRequest`；compiler 會退回到可接受的元件庫元件，不會為這些模板產生網站專屬元件程式碼。
