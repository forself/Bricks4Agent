# Visual Pattern Template Corpus Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expand the site rebuild pipeline with a small benchmark corpus, reusable composite components, and higher-level visual/function templates documented inside the component library.

**Architecture:** Public websites are used as visual pattern references, not as one-off component sources. The pipeline remains `SiteIntentExtractor -> TemplateMatcher -> TemplateCompiler -> StaticSitePackageGenerator`; templates may only select components declared by `bricks4agent.default`, and component gaps are recorded as requests rather than generated into the output package.

**Tech Stack:** .NET 8 worker/tests, JSON component manifest/templates, vanilla runtime renderer, Playwright/browser smoke checks.

---

## Benchmark Corpus

Use these public websites as pattern references. Their domains and real-world categories are corpus labels only; they must not drive template or component selection.

- `https://www.ntu.edu.tw/`
- `https://www.nthu.edu.tw/`
- `https://www.mmu.edu.tw/`
- `https://www.gov.tw/`
- `https://www.gov.taipei/`
- `https://www.ey.gov.tw/`
- `https://www.mohw.gov.tw/`
- `https://www.cgh.org.tw/`
- `https://www.ntuh.gov.tw/`

Observed reusable patterns:

- Search-led portal hero: prominent search, hot keywords, role or service shortcuts.
- Service category grid: broad categories with nested links.
- Action grid: high-priority tasks such as registration, contact, complaint, portal, mail, service lookup.
- Tabbed/news board: multiple announcement categories sharing one area.
- Existing generic pieces remain valid: mega header, hero carousel/banner, quick links, card news, media features, footer.

## Files

- Modify: `packages/csharp/workers/site-crawler-worker/component-libraries/bricks4agent.default/manifest.json`
- Create: `packages/csharp/workers/site-crawler-worker/component-libraries/bricks4agent.default/README.md`
- Create: `packages/csharp/workers/site-crawler-worker/template-framework/README.md`
- Modify: `packages/csharp/workers/site-crawler-worker/template-framework/visual_patterns.json`
- Modify: `packages/csharp/workers/site-crawler-worker/Services/SiteIntentExtractor.cs`
- Modify: `packages/csharp/workers/site-crawler-worker/Services/TemplateMatcher.cs`
- Modify: `packages/csharp/workers/site-crawler-worker/Services/TemplateCompiler.cs`
- Modify: `packages/csharp/workers/site-crawler-worker/Services/StaticSitePackageGenerator.cs`
- Modify: `packages/csharp/tests/unit/Workers/SiteCrawler/ComponentLibraryLoaderTests.cs`
- Modify: `packages/csharp/tests/unit/Workers/SiteCrawler/TemplateFrameworkLoaderTests.cs`
- Modify: `packages/csharp/tests/unit/Workers/SiteCrawler/SiteIntentExtractorTests.cs`
- Modify: `packages/csharp/tests/unit/Workers/SiteCrawler/TemplateMatcherTests.cs`
- Modify: `packages/csharp/tests/unit/Workers/SiteCrawler/TemplateCompilerTests.cs`
- Modify: `packages/csharp/tests/unit/Workers/SiteCrawler/StaticSitePackageGeneratorTests.cs`

## Tasks

### Task 1: Documentation Coverage Tests

- [ ] Add a component-library test that loads `bricks4agent.default/README.md` and asserts every manifest component type appears as a documented heading or code token.
- [ ] Add a template-framework test that asserts `hero_news_portal`, `search_service_portal`, and `service_action_portal` load from `visual_patterns.json` and are documented in `template-framework/README.md`.
- [ ] Run `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --no-restore --filter "ComponentLibraryLoaderTests|TemplateFrameworkLoaderTests"` and verify the new tests fail because docs/templates/components are missing.

### Task 2: Component Library Expansion

- [ ] Add manifest entries for `ServiceSearchHero`, `ServiceCategoryGrid`, `ServiceActionGrid`, and `TabbedNewsBoard`.
- [ ] Write `bricks4agent.default/README.md` with component roles, props, and composition rules. The docs must state that composite components are reusable family-level pieces assembled from existing atomic ideas, not source-specific website clones.
- [ ] Re-run the documentation tests and verify they pass.

### Task 3: Template Definitions

- [ ] Extend the template manifest with `search_service_portal` and `service_action_portal`.
- [ ] Use `pattern_tags` for template metadata; `supported_site_kinds` remains only as legacy manifest compatibility.
- [ ] Add slots: `search`, `service_categories`, `service_actions`, and `tabbed_news` where appropriate.
- [ ] Write `template-framework/README.md` describing template selection, page types, and slot-to-component rules.
- [ ] Re-run template loader tests and verify they pass.

### Task 4: Intent Extraction And Matching

- [ ] Add failing tests for search-service and service-action visual snapshots modelled on the corpus patterns.
- [ ] Update `SiteIntentExtractor` to extract search heroes, service category grids, action grids, and tabbed news boards from visual/function evidence.
- [ ] Update `TemplateMatcher` to score template candidates by visual/function slots only; `SiteKind` must not influence template or component selection.
- [ ] Run `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --no-restore --filter "SiteIntentExtractorTests|TemplateMatcherTests"` and verify pass after implementation.

### Task 5: Compiler And Runtime Rendering

- [ ] Add failing compiler/static package tests asserting the new components are emitted and rendered.
- [ ] Update `TemplateCompiler` to build props for search hero, category grid, action grid, and tabbed news from extracted visual blocks.
- [ ] Update `StaticSitePackageGenerator` runtime and CSS for the new components.
- [ ] Run `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --no-restore --filter "TemplateCompilerTests|StaticSitePackageGeneratorTests"` and verify pass.

### Task 6: End-To-End Verification

- [ ] Run `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --no-restore`.
- [ ] Run `dotnet build packages/csharp/ControlPlane.slnx --no-restore`.
- [ ] Run a limited visual/package smoke on representative sites from the corpus with depth 2 or 3, checking generated documents contain only manifest-declared components and no generated local components.
- [ ] Use a local static server and browser smoke check on at least one generated package to verify `index.html`, `site.json`, navigation, header/main/footer, and runtime console health.
