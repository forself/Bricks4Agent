# bricks4agent.default Component Library

This library is the only component source the site rebuild generator may use by default. A generated package can include only component types declared in `manifest.json`; unsupported visual patterns must become `ComponentRequest` entries unless the library itself is expanded.

Composite components are reusable family-level structures assembled from the same atomic ideas used by the base library. They must not be named after, or shaped exclusively for, a single source website.

## Component Index

- `PageShell`: Top-level route container.
- `SiteHeader`: Basic site title, logo, utility links, and primary links.
- `HeroSection`: Text-led introduction section.
- `ContentSection`: General text content section.
- `LinkList`: General related-link list.
- `FormBlock`: Non-submitting form representation.
- `SiteFooter`: Basic footer with attribution, contact text, logo, and links.
- `AtomicSection`: Generic section container for atom-based fallback assembly.
- `TextBlock`: Heading and body text atom.
- `ImageBlock`: Image/media atom.
- `ButtonLink`: Link action atom.
- `CardGrid`: Generic grid or carousel-like card container.
- `FeatureCard`: Card atom for news, features, programs, and media.
- `MegaHeader`: Multi-tier header with logo, utility links, primary navigation, and search affordance.
- `HeroCarousel`: Large visual carousel for image-led home pages.
- `HeroBanner`: Single-image or text-led hero band.
- `QuickLinkRibbon`: Compact link ribbon for common actions.
- `NewsCardCarousel`: Horizontal news card carousel.
- `NewsGrid`: Grid of news or announcement cards.
- `MediaFeatureGrid`: Media-rich feature grid.
- `ServiceSearchHero`: Search-led hero for portal-like home pages.
- `ServiceCategoryGrid`: Grid of audience, topic, or task categories with nested links.
- `ServiceActionGrid`: High-priority action grid for registration, lookup, contact, portal, or service tasks.
- `TabbedNewsBoard`: Multi-category news and announcement board.
- `SearchBoxPanel`: Search input panel with suggestions.
- `FacetFilterPanel`: Filter/facet panel for narrowing results.
- `ResultList`: Search or discovery result list.
- `PaginationNav`: Pagination or result navigation strip.
- `DashboardFilterBar`: Report/dashboard filter and action toolbar.
- `MetricSummaryGrid`: KPI and metric card grid.
- `ChartPanel`: Chart-like visual panel represented from extracted values.
- `DataTablePreview`: Table preview for report and listing data.
- `StepIndicator`: Step/progress indicator for form flows.
- `StructuredFormPanel`: Non-submitting structured form panel.
- `ValidationSummary`: Validation and required-field summary.
- `FormActionBar`: Form-flow action bar.
- `ShowcaseHero`: Commercial-style hero with media and calls to action.
- `ProductCardGrid`: Product or offer card grid.
- `ProofStrip`: Trust, logo, or statistic proof strip.
- `PricingPanel`: Pricing/package option panel.
- `CtaBand`: Call-to-action band.
- `InstitutionFooter`: Footer with contact text and curated links.
- `ArticleList`: Listing/index page for news or articles.
- `ContentArticle`: Article body with optional media.

## Composite Component Contracts

### `MegaHeader`

Roles: `navigation`, `header`, `mega_nav`.

Required props: `title`, `utility_links`, `primary_links`, `search_enabled`.

Use when the visual source shows a logo plus multiple navigation tiers, utility links, and grouped primary navigation.

### `ServiceSearchHero`

Roles: `search`, `hero`, `service_portal`.

Required props: `title`, `body`, `query_placeholder`, `hot_keywords`, `actions`.

Use for portal home pages where search is the primary first-screen function. `hot_keywords` and `actions` use the standard link object shape: `label`, `url`, `source_url`, `scope`.

### `ServiceCategoryGrid`

Roles: `service_categories`, `category_grid`, `portal`.

Required props: `title`, `categories`.

Each category has `title`, `body`, and `links`. Use for category directories, audience menus, and large service blocks.

### `ServiceActionGrid`

Roles: `service_actions`, `quick_actions`, `portal`.

Required props: `title`, `actions`.

Each action has `label`, `url`, `source_url`, `scope`, and `kind`. Use for high-priority tasks such as appointment registration, online services, contact, complaints, portal entry, or service lookup.

### `TabbedNewsBoard`

Roles: `news`, `announcements`, `tabs`.

Required props: `title`, `tabs`.

Each tab has `label` and `items`. Each item uses the standard card item shape: `title`, `body`, `url`, `source_url`, `scope`, `media_url`, `media_alt`.

### `SearchBoxPanel`

Roles: `search_box`, `search`, `discovery`.

Required props: `title`, `body`, `query_placeholder`, `suggestions`.

Use for search-led pages where the extracted visual pattern shows a keyword field and optional suggested searches.

### `FacetFilterPanel`

Roles: `filter_panel`, `facets`, `filters`.

Required props: `title`, `filters`.

Use for sidebar, topbar, or chip-based filters. Each filter has `label`, `value`, and `count`.

### `ResultList`

Roles: `result_list`, `results`, `listing`.

Required props: `title`, `summary`, `items`.

Use for search result rows or cards. Items use the standard card item shape.

### `PaginationNav`

Roles: `pagination`, `pager`.

Required props: `links`.

Use for page-number, next/previous, or result navigation strips.

### `DashboardFilterBar`

Roles: `filter_bar`, `dashboard_filters`, `toolbar`.

Required props: `title`, `filters`, `actions`.

Use for report filters, date ranges, export controls, and other dashboard-level toolbar actions.

### `MetricSummaryGrid`

Roles: `metric_summary`, `kpi`, `stats`.

Required props: `title`, `metrics`.

Use for KPI cards. Each metric has `label`, `value`, and `detail`.

### `ChartPanel`

Roles: `chart_panel`, `chart`, `visualization`.

Required props: `title`, `body`, `series`.

Use for chart-like areas when the rebuild can preserve visual hierarchy and approximate values without reimplementing the original charting library.

### `DataTablePreview`

Roles: `data_table`, `table`.

Required props: `title`, `columns`, `rows`.

Use for report or listing tables. Rows contain a `cells` string array.

### `StepIndicator`

Roles: `step_indicator`, `steps`, `progress`.

Required props: `steps`.

Use for wizard or multi-step input flows. Each step has `label` and `status`.

### `StructuredFormPanel`

Roles: `form_fields`, `form`, `input`.

Required props: `title`, `fields`.

Use for non-submitting reconstructed input areas. Each field has `name`, `id`, `label`, `type`, and `required`.

### `ValidationSummary`

Roles: `validation_summary`, `validation`, `notice`.

Required props: `title`, `messages`.

Use for required-field notices, validation hints, and form instructions.

### `FormActionBar`

Roles: `action_bar`, `form_actions`, `actions`.

Required props: `actions`.

Use for submit, continue, back, cancel, and reset controls represented as disabled or local-safe actions.

### `ShowcaseHero`

Roles: `showcase_hero`, `product_hero`, `hero`.

Required props: `title`, `body`, `media_url`, `media_alt`, `actions`.

Use for product, offer, or campaign hero sections with prominent calls to action.

### `ProductCardGrid`

Roles: `product_cards`, `products`, `offers`.

Required props: `title`, `items`.

Use for products, offers, packages, and visual feature cards. Items use the standard card item shape.

### `ProofStrip`

Roles: `proof_strip`, `proof`, `trust`.

Required props: `title`, `items`.

Use for trust signals, partner logos expressed as labels, statistics, or short proof points. Each item has `label`, `value`, and `detail`.

### `PricingPanel`

Roles: `pricing_panel`, `pricing`, `plans`.

Required props: `title`, `plans`.

Use for pricing or package option areas. Each plan has `title`, `price`, `body`, `features`, and `action`.

### `CtaBand`

Roles: `cta_band`, `cta`, `conversion`.

Required props: `title`, `body`, `actions`.

Use for final or mid-page calls to action.

## Library Rules

- New templates can reference only component types declared in `manifest.json`.
- New components must be documented here in the component index and, for composite components, in the contracts section.
- Component props emitted by `TemplateCompiler` must be declared in the manifest schema.
- Generated packages must keep custom generated components out of `components/generated/` for supported visual-pattern templates.
