# Visual Pattern Template Expansion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add first-pass reusable templates and components for search/discovery, report/dashboard, input/form-flow, and commercial showcase page patterns.

**Architecture:** Keep the existing crawler-to-generator pipeline. Add new visual/function block kinds in `SiteIntentExtractor`, score templates by slots in `TemplateMatcher`, compile to manifest-declared components in `TemplateCompiler`, and render the new component types in the static runtime. Do not select templates by website category or `SiteKind`.

**Tech Stack:** .NET 8, xUnit/FluentAssertions, JSON component/template manifests, vanilla JS/CSS static package runtime.

---

## Files

- Modify: `packages/csharp/workers/site-crawler-worker/component-libraries/bricks4agent.default/manifest.json`
- Modify: `packages/csharp/workers/site-crawler-worker/component-libraries/bricks4agent.default/README.md`
- Modify: `packages/csharp/workers/site-crawler-worker/template-framework/visual_patterns.json`
- Modify: `packages/csharp/workers/site-crawler-worker/template-framework/README.md`
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

### Task 1: Component and Template Contract Tests

- [ ] Add failing component loader assertions for `SearchBoxPanel`, `FacetFilterPanel`, `ResultList`, `PaginationNav`, `DashboardFilterBar`, `MetricSummaryGrid`, `ChartPanel`, `DataTablePreview`, `StepIndicator`, `StructuredFormPanel`, `ValidationSummary`, `FormActionBar`, `ShowcaseHero`, `ProductCardGrid`, `ProofStrip`, `PricingPanel`, and `CtaBand`.
- [ ] Add failing template loader assertions for `search_results_portal`, `report_dashboard`, `input_flow`, and `commercial_showcase`.
- [ ] Run `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --no-restore --filter "ComponentLibraryLoaderTests|TemplateFrameworkLoaderTests"` and verify the new assertions fail.
- [ ] Add manifest entries, README sections, template slots, and template README coverage.
- [ ] Re-run the same focused tests and verify they pass.

### Task 2: Intent and Matcher Tests

- [ ] Add failing visual snapshot tests for search results, report dashboard, input flow, and commercial showcase block extraction.
- [ ] Add failing matcher tests proving each new slot set picks its visual pattern template with `SiteKind = "unknown"` or a contradictory metadata value.
- [ ] Run `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --no-restore --filter "SiteIntentExtractorTests|TemplateMatcherTests"` and verify failure.
- [ ] Implement visual/static role classification, slot mapping, template scoring, and preferred component mapping.
- [ ] Re-run the same focused tests and verify they pass.

### Task 3: Compiler and Runtime Tests

- [ ] Add failing compiler tests that each new pattern compiles to only declared manifest components and records no generated components.
- [ ] Add failing runtime package assertions for renderer names and CSS selectors for every new component.
- [ ] Run `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --no-restore --filter "TemplateCompilerTests|StaticSitePackageGeneratorTests"` and verify failure.
- [ ] Implement compiler prop builders and static runtime renderers/CSS.
- [ ] Re-run the same focused tests and verify they pass.

### Task 4: Full Verification

- [ ] Run `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --no-restore --filter "ComponentLibraryLoaderTests|TemplateFrameworkLoaderTests|SiteIntentExtractorTests|TemplateMatcherTests|TemplateCompilerTests|StaticSitePackageGeneratorTests"`.
- [ ] Run `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --no-restore`.
- [ ] Run `dotnet build packages/csharp/ControlPlane.slnx --no-restore`.
- [ ] Inspect `git status --short` and summarize the changed files.
