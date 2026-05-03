# Site Template Framework

The template framework is the layer above the component library. It maps visual intent into route-level page skeletons and slot contracts. Templates do not create components; they can only reference component types declared by the loaded component library manifest.

Templates are selected by visual and functional layout patterns, not by website category. Bundled templates use `pattern_tags`; `supported_site_kinds` is accepted only as legacy metadata for older manifests and is not used for template scoring.

## Templates

### `hero_news_portal`

Use when the visual source is organized as header, large hero or carousel, quick-link band, news or card carousel, feature/content sections, and footer.

Home slots:

- `header`: `MegaHeader` or `SiteHeader`
- `hero`: `HeroCarousel`, `HeroBanner`, or `AtomicSection`
- `quick_links`: `QuickLinkRibbon`, `CardGrid`, or `AtomicSection`
- `news`: `NewsCardCarousel`, `NewsGrid`, or `CardGrid`
- `features`: `MediaFeatureGrid`, `CardGrid`, or `AtomicSection`
- `content`: `ContentArticle`, `ContentSection`, or `AtomicSection`
- `footer`: `InstitutionFooter` or `SiteFooter`

### `search_service_portal`

Use when the visual source is organized around an optional hero, a prominent search area, service/category discovery cards, action shortcuts, tabbed announcements, news/card sections, and footer.

Home slots:

- `header`: `MegaHeader` or `SiteHeader`
- `hero`: `HeroCarousel`, `HeroBanner`, or `AtomicSection`
- `search`: `ServiceSearchHero`, `HeroBanner`, or `AtomicSection`
- `news`: `NewsCardCarousel`, `NewsGrid`, or `CardGrid`
- `tabbed_news`: `TabbedNewsBoard`, `NewsGrid`, `ArticleList`, or `CardGrid`
- `service_categories`: `ServiceCategoryGrid`, `CardGrid`, or `AtomicSection`
- `service_actions`: `ServiceActionGrid`, `QuickLinkRibbon`, `CardGrid`, or `AtomicSection`
- `features`: `MediaFeatureGrid`, `ServiceCategoryGrid`, `CardGrid`, or `AtomicSection`
- `footer`: `InstitutionFooter` or `SiteFooter`

### `service_action_portal`

Use when the visual source is organized as a hero plus a strong action grid, followed by tabbed notices/news, optional service/category discovery, and footer.

Home slots:

- `header`: `MegaHeader` or `SiteHeader`
- `hero`: `HeroCarousel`, `HeroBanner`, or `AtomicSection`
- `service_actions`: `ServiceActionGrid`, `QuickLinkRibbon`, `CardGrid`, or `AtomicSection`
- `news`: `NewsCardCarousel`, `NewsGrid`, or `CardGrid`
- `tabbed_news`: `TabbedNewsBoard`, `NewsGrid`, `ArticleList`, or `CardGrid`
- `features`: `MediaFeatureGrid`, `CardGrid`, or `AtomicSection`
- `service_categories`: `ServiceCategoryGrid`, `CardGrid`, or `AtomicSection`
- `footer`: `InstitutionFooter` or `SiteFooter`

### `search_results_portal`

Use when the visual source is organized around a keyword search, filters/facets, result rows or cards, and pagination/result navigation.

Home slots:

- `header`: `MegaHeader` or `SiteHeader`
- `search_box`: `SearchBoxPanel`, `ServiceSearchHero`, or `AtomicSection`
- `filter_panel`: `FacetFilterPanel` or `AtomicSection`
- `result_list`: `ResultList`, `ArticleList`, `CardGrid`, or `AtomicSection`
- `pagination`: `PaginationNav`, `QuickLinkRibbon`, or `AtomicSection`
- `footer`: `InstitutionFooter` or `SiteFooter`

### `report_dashboard`

Use when the visual source is organized around filters, KPI metrics, chart panels, data table previews, and report actions.

Home slots:

- `header`: `MegaHeader` or `SiteHeader`
- `filter_bar`: `DashboardFilterBar`, `FacetFilterPanel`, or `AtomicSection`
- `metric_summary`: `MetricSummaryGrid` or `AtomicSection`
- `chart_panel`: `ChartPanel`, `MediaFeatureGrid`, or `AtomicSection`
- `data_table`: `DataTablePreview`, `ArticleList`, or `AtomicSection`
- `footer`: `InstitutionFooter` or `SiteFooter`

### `input_flow`

Use when the visual source is organized around a step/progress indicator, form fields, validation hints, and flow actions.

Home slots:

- `header`: `MegaHeader` or `SiteHeader`
- `step_indicator`: `StepIndicator` or `AtomicSection`
- `form_fields`: `StructuredFormPanel`, `FormBlock`, or `AtomicSection`
- `validation_summary`: `ValidationSummary`, `ContentSection`, or `AtomicSection`
- `action_bar`: `FormActionBar`, `QuickLinkRibbon`, or `AtomicSection`
- `footer`: `InstitutionFooter` or `SiteFooter`

### `commercial_showcase`

Use when the visual source is organized around a product/offer hero, product cards, feature grids, proof/trust strips, pricing packages, and CTA bands.

Home slots:

- `header`: `MegaHeader` or `SiteHeader`
- `showcase_hero`: `ShowcaseHero`, `HeroBanner`, or `AtomicSection`
- `product_cards`: `ProductCardGrid`, `MediaFeatureGrid`, `CardGrid`, or `AtomicSection`
- `feature_grid`: `MediaFeatureGrid`, `CardGrid`, or `AtomicSection`
- `proof_strip`: `ProofStrip`, `MetricSummaryGrid`, or `AtomicSection`
- `pricing_panel`: `PricingPanel`, `ProductCardGrid`, `CardGrid`, or `AtomicSection`
- `cta_band`: `CtaBand`, `QuickLinkRibbon`, or `AtomicSection`
- `footer`: `InstitutionFooter` or `SiteFooter`

## Selection Rules

- `SiteIntentExtractor` extracts visual/function blocks such as `hero`, `search`, `service_categories`, `service_actions`, `tabbed_news`, `quick_links`, and `news`.
- `TemplateMatcher` scores templates only by those blocks and by how many slots can be filled by the page intent.
- A slot may select only components declared in `bricks4agent.default/manifest.json` or another explicitly loaded manifest.
- Missing preferred components are reported as `ComponentRequest`; the compiler falls back to accepted library components and does not generate source-specific component code for these templates.
