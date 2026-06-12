# Visual Pattern Template Expansion Design

## Purpose

Expand the site rebuild pipeline with four reusable page-pattern families:

- Search/discovery pages.
- Report/dashboard pages.
- Input/form-flow pages.
- Commercial showcase pages.

These are visual and functional page structures, not website categories. Template and component selection must continue to ignore `SiteKind` and use extracted visual/function blocks only.

## References

- Google Cloud's faceted search documentation is used as a reference for search result filtering patterns: https://cloud.google.com/spanner/docs/full-text-search/facets
- CDC COVE dashboard pages are used as public examples of dashboard/report surfaces with chart/table modules: https://www.cdc.gov/cove/dashboards/index.html
- MDN's `<input>` reference and client-side validation guide are used as form/input pattern references: https://developer.mozilla.org/docs/Web/HTML/Reference/Elements/input and https://developer.mozilla.org/en-US/docs/Learn_web_development/Extensions/Forms/Form_validation

## Architecture

The existing pipeline remains unchanged:

`SiteIntentExtractor -> TemplateMatcher -> TemplateCompiler -> StaticSitePackageGenerator`

The expansion adds reusable blocks and templates to the existing library contract:

- `SiteIntentExtractor` recognizes new visual/function block kinds from rendered regions and static sections.
- `visual_patterns.json` declares four new template IDs and their slots.
- `TemplateMatcher` scores templates by slots such as `search_box`, `filter_panel`, `metric_summary`, `form_fields`, and `showcase_hero`.
- `TemplateCompiler` maps slot plans to component-library nodes.
- `StaticSitePackageGenerator` renders only component types declared by `bricks4agent.default/manifest.json`.

## Pattern Families

### `search_results_portal`

Primary signals:

- Keyword search input or search panel.
- Facet/filter sidebar or filter chips.
- Result list, summaries, snippets, and pagination.

Slots:

- `header`
- `search_box`
- `filter_panel`
- `result_list`
- `pagination`
- `footer`

Components:

- `SearchBoxPanel`
- `FacetFilterPanel`
- `ResultList`
- `PaginationNav`

### `report_dashboard`

Primary signals:

- Filter/date/export toolbar.
- KPI or metric cards.
- Chart-like panels.
- Data table preview.

Slots:

- `header`
- `filter_bar`
- `metric_summary`
- `chart_panel`
- `data_table`
- `footer`

Components:

- `DashboardFilterBar`
- `MetricSummaryGrid`
- `ChartPanel`
- `DataTablePreview`

### `input_flow`

Primary signals:

- Step or progress indicator.
- Structured input fields or form controls.
- Required/validation hints.
- Submit/continue/cancel action bar.

Slots:

- `header`
- `step_indicator`
- `form_fields`
- `validation_summary`
- `action_bar`
- `footer`

Components:

- `StepIndicator`
- `StructuredFormPanel`
- `ValidationSummary`
- `FormActionBar`

### `commercial_showcase`

Primary signals:

- Product or offer hero with primary CTA.
- Product cards or feature cards.
- Proof/trust/logo/stat strip.
- Pricing or package panel.
- Final CTA band.

Slots:

- `header`
- `showcase_hero`
- `product_cards`
- `feature_grid`
- `proof_strip`
- `pricing_panel`
- `cta_band`
- `footer`

Components:

- `ShowcaseHero`
- `ProductCardGrid`
- `ProofStrip`
- `PricingPanel`
- `CtaBand`

## Data Rules

- Links are normalized through existing local-route logic so same-origin crawled pages become local generated routes.
- Components can only use props declared in the manifest.
- If a preferred component is missing, `TemplateMatcher` records a `ComponentRequest` and falls back to an accepted component.
- The compiler must not create source-specific generated components for these supported patterns.

## Testing

The implementation must add focused tests for:

- Manifest and README coverage for every new component.
- Template loader coverage for every new template.
- Intent extraction for the four new visual/function patterns.
- Template matching that proves contradictory `SiteKind` does not affect selection.
- Compiler output that uses only manifest-declared components.
- Runtime and CSS support for all new components.

Verification commands:

- `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --no-restore --filter "ComponentLibraryLoaderTests|TemplateFrameworkLoaderTests|SiteIntentExtractorTests|TemplateMatcherTests|TemplateCompilerTests|StaticSitePackageGeneratorTests"`
- `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --no-restore`
- `dotnet build packages/csharp/ControlPlane.slnx --no-restore`
