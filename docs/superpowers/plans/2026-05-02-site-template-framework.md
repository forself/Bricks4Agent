# Site Template Framework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a reusable site template framework so website rebuilds are compiled from visual intent into manifest-declared components only.

**Architecture:** `SiteGeneratorConverter` will delegate high-level page structure to `SiteIntentExtractor -> TemplateMatcher -> TemplateCompiler`. The compiler emits `GeneratorSiteDocument` using only `bricks4agent.default` components, recording `ComponentRequest` gaps instead of creating site-specific generated components.

**Tech Stack:** .NET 8, xUnit, FluentAssertions, System.Text.Json, existing `site-crawler-worker` contracts, static runtime JavaScript.

---

## File Structure

Create:

- `packages/csharp/workers/site-crawler-worker/Models/TemplateFrameworkContracts.cs`
- `packages/csharp/workers/site-crawler-worker/Services/TemplateFrameworkLoader.cs`
- `packages/csharp/workers/site-crawler-worker/Services/SiteIntentExtractor.cs`
- `packages/csharp/workers/site-crawler-worker/Services/TemplateMatcher.cs`
- `packages/csharp/workers/site-crawler-worker/Services/TemplateCompiler.cs`
- `packages/csharp/workers/site-crawler-worker/template-framework/institutional_site.json`
- `packages/csharp/tests/unit/Workers/SiteCrawler/TemplateFrameworkLoaderTests.cs`
- `packages/csharp/tests/unit/Workers/SiteCrawler/SiteIntentExtractorTests.cs`
- `packages/csharp/tests/unit/Workers/SiteCrawler/TemplateMatcherTests.cs`
- `packages/csharp/tests/unit/Workers/SiteCrawler/TemplateCompilerTests.cs`

Modify:

- `packages/csharp/workers/site-crawler-worker/SiteCrawlerWorker.csproj`
- `packages/csharp/workers/site-crawler-worker/component-libraries/bricks4agent.default/manifest.json`
- `packages/csharp/workers/site-crawler-worker/Services/SiteGeneratorConverter.cs`
- `packages/csharp/workers/site-crawler-worker/Services/StaticSitePackageGenerator.cs`
- `packages/csharp/tests/unit/Workers/SiteCrawler/SiteGeneratorConverterTests.cs`
- `packages/csharp/tests/unit/Workers/SiteCrawler/StaticSitePackageGeneratorTests.cs`

---

### Task 1: Template Framework Contracts And Loader

**Files:**
- Create: `packages/csharp/workers/site-crawler-worker/Models/TemplateFrameworkContracts.cs`
- Create: `packages/csharp/workers/site-crawler-worker/Services/TemplateFrameworkLoader.cs`
- Create: `packages/csharp/workers/site-crawler-worker/template-framework/institutional_site.json`
- Modify: `packages/csharp/workers/site-crawler-worker/SiteCrawlerWorker.csproj`
- Test: `packages/csharp/tests/unit/Workers/SiteCrawler/TemplateFrameworkLoaderTests.cs`

- [ ] **Step 1: Write failing loader tests**

Add tests that call `new TemplateFrameworkLoader().LoadDefault()` and assert:

```csharp
templates.Templates.Should().ContainSingle(template => template.TemplateId == "institutional_site");
templates.Templates[0].PageTypes.Should().ContainKey("home");
templates.Templates[0].PageTypes["home"].Slots.Should().Contain(slot => slot.Name == "hero" && slot.Accepts.Contains("HeroCarousel"));
```

- [ ] **Step 2: Run focused test and verify failure**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter TemplateFrameworkLoaderTests
```

Expected: compile failure because template contracts and loader do not exist.

- [ ] **Step 3: Add contracts**

Define JSON-backed records/classes:

```csharp
public sealed class TemplateFrameworkManifest { public List<TemplateDefinition> Templates { get; set; } = new(); }
public sealed class TemplateDefinition { public string TemplateId { get; set; } = string.Empty; public List<string> SupportedSiteKinds { get; set; } = new(); public Dictionary<string, TemplatePageTypeDefinition> PageTypes { get; set; } = new(); }
public sealed class TemplatePageTypeDefinition { public List<TemplateSlotDefinition> Slots { get; set; } = new(); }
public sealed class TemplateSlotDefinition { public string Name { get; set; } = string.Empty; public bool Required { get; set; } public List<string> Accepts { get; set; } = new(); public string Fallback { get; set; } = "AtomicSection"; }
```

Also define intent/plan types used by later tasks:

```csharp
public sealed class SiteIntentModel { public string SiteKind { get; set; } = "unknown"; public List<SiteIntentPage> Pages { get; set; } = new(); public ExtractedHeader GlobalHeader { get; set; } = new(); public ExtractedFooter GlobalFooter { get; set; } = new(); }
public sealed class SiteIntentPage { public string PageUrl { get; set; } = string.Empty; public string PageType { get; set; } = "unknown"; public int Depth { get; set; } public string Title { get; set; } = string.Empty; public List<SiteIntentBlock> Blocks { get; set; } = new(); }
public sealed class SiteIntentBlock { public string Id { get; set; } = string.Empty; public string Kind { get; set; } = "unknown"; public string Slot { get; set; } = string.Empty; public ExtractedSection Section { get; set; } = new(); public double Confidence { get; set; } public List<string> Reasons { get; set; } = new(); }
public sealed class TemplatePlan { public string TemplateId { get; set; } = string.Empty; public List<TemplatePagePlan> Pages { get; set; } = new(); public List<ComponentRequest> ComponentRequests { get; set; } = new(); }
public sealed class TemplatePagePlan { public string PageUrl { get; set; } = string.Empty; public string PageType { get; set; } = "unknown"; public List<TemplateSlotPlan> Slots { get; set; } = new(); }
public sealed class TemplateSlotPlan { public string SlotName { get; set; } = string.Empty; public string ComponentType { get; set; } = string.Empty; public string FallbackComponentType { get; set; } = string.Empty; public SiteIntentBlock? Block { get; set; } public double Confidence { get; set; } public List<string> Reasons { get; set; } = new(); }
```

- [ ] **Step 4: Add loader**

Implement `TemplateFrameworkLoader` with the same candidate-path strategy as `ComponentLibraryLoader`:

```csharp
yield return Path.Combine(AppContext.BaseDirectory, "template-framework/institutional_site.json");
yield return Path.Combine(Directory.GetCurrentDirectory(), "template-framework/institutional_site.json");
yield return Path.Combine(Directory.GetCurrentDirectory(), "packages/csharp/workers/site-crawler-worker/template-framework/institutional_site.json");
```

Validate non-empty `template_id`, at least one supported kind, each page type has slots, and each slot has at least one accepted component.

- [ ] **Step 5: Add institutional template JSON**

Define `home`, `listing`, and `article` page types with these first-class slots:

```json
["header", "hero", "quick_links", "news", "features", "content", "footer"]
```

Accepted components must include the high-level reusable types from the spec, with existing low-level fallbacks such as `SiteHeader`, `AtomicSection`, `CardGrid`, `ContentSection`, and `SiteFooter`.

- [ ] **Step 6: Copy template assets in project file**

Add:

```xml
<Content Include="template-framework\**\*" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 7: Run focused tests and commit**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter TemplateFrameworkLoaderTests
```

Expected: pass.

Commit:

```powershell
git add packages/csharp/workers/site-crawler-worker/Models/TemplateFrameworkContracts.cs packages/csharp/workers/site-crawler-worker/Services/TemplateFrameworkLoader.cs packages/csharp/workers/site-crawler-worker/template-framework/institutional_site.json packages/csharp/workers/site-crawler-worker/SiteCrawlerWorker.csproj packages/csharp/tests/unit/Workers/SiteCrawler/TemplateFrameworkLoaderTests.cs
git commit -m "feat: add site template framework loader"
```

---

### Task 2: Deterministic Site Intent Extraction

**Files:**
- Create: `packages/csharp/workers/site-crawler-worker/Services/SiteIntentExtractor.cs`
- Test: `packages/csharp/tests/unit/Workers/SiteCrawler/SiteIntentExtractorTests.cs`

- [ ] **Step 1: Write failing tests**

Create a crawl with a university home page, rendered header/hero/carousel/footer regions, and static fallback sections. Assert:

```csharp
intent.SiteKind.Should().Be("university");
intent.Pages.Single(page => page.Depth == 0).PageType.Should().Be("home");
intent.Pages[0].Blocks.Select(block => block.Kind).Should().Contain(["header", "hero_carousel", "news_carousel", "footer"]);
intent.Pages[0].Blocks.Should().NotContain(block => block.Section.Headline == "Static Source Hero");
```

Also test a depth-1 page with repeated items becomes `listing`, and a long text page becomes `article`.

- [ ] **Step 2: Run focused tests and verify failure**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter SiteIntentExtractorTests
```

Expected: compile failure because `SiteIntentExtractor` does not exist.

- [ ] **Step 3: Implement extractor**

Rules:

- Prefer `VisualPageSnapshot.Regions` over static `ExtractedPageModel.Sections`.
- Preserve visual order by region bounds `y`.
- Classify site kind as `university` when title/text/url contains `university`, `college`, `school`, `大學`, `學院`, or `學校`; otherwise `institutional`.
- `Depth == 0` is `home`.
- Repeated items or many links/dates become `listing`.
- Long body text with low item count becomes `article`.
- Region roles map to intent kinds:

```csharp
"header" or "nav" -> "header";
"carousel" with first large media block on home -> "hero_carousel";
"hero" with multiple media/items -> "hero_carousel";
"hero" -> "hero_banner";
"card_grid" or "visual_grid" with short labels -> "quick_links";
"news" or carousel/list with date-like text -> "news_carousel";
"gallery" or image-rich grid -> "media_feature_grid";
"footer" -> "footer";
```

- [ ] **Step 4: Run focused tests and commit**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter SiteIntentExtractorTests
```

Expected: pass.

Commit:

```powershell
git add packages/csharp/workers/site-crawler-worker/Services/SiteIntentExtractor.cs packages/csharp/tests/unit/Workers/SiteCrawler/SiteIntentExtractorTests.cs
git commit -m "feat: extract deterministic site intent"
```

---

### Task 3: Template Matcher

**Files:**
- Create: `packages/csharp/workers/site-crawler-worker/Services/TemplateMatcher.cs`
- Test: `packages/csharp/tests/unit/Workers/SiteCrawler/TemplateMatcherTests.cs`

- [ ] **Step 1: Write failing tests**

Assert that a university home intent produces:

```csharp
plan.TemplateId.Should().Be("institutional_site");
home.Slots.Select(slot => slot.SlotName).Should().Contain(["header", "hero", "quick_links", "news", "footer"]);
home.Slots.Single(slot => slot.SlotName == "hero").ComponentType.Should().Be("HeroCarousel");
home.Slots.Single(slot => slot.SlotName == "news").ComponentType.Should().Be("NewsCardCarousel");
```

Add a fallback test using a manifest without `HeroCarousel`; expected component type is `HeroBanner` or `AtomicSection`, and `ComponentRequests` records the missing preferred type.

- [ ] **Step 2: Run focused tests and verify failure**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter TemplateMatcherTests
```

Expected: compile failure because `TemplateMatcher` does not exist.

- [ ] **Step 3: Implement matcher**

Use deterministic slot mapping:

```csharp
header -> header
hero_carousel or hero_banner -> hero
quick_links -> quick_links
news_grid or news_carousel or article_list -> news/content
media_feature_grid -> features
content_article -> content
footer -> footer
```

Choose the first accepted component present in the loaded component manifest. If the preferred accepted type is absent but a fallback accepted type exists, use the fallback and add `ComponentRequest` with reason `preferred_component_missing`. If no accepted type exists, use the slot fallback and record `component_gap`.

- [ ] **Step 4: Run focused tests and commit**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter TemplateMatcherTests
```

Expected: pass.

Commit:

```powershell
git add packages/csharp/workers/site-crawler-worker/Services/TemplateMatcher.cs packages/csharp/tests/unit/Workers/SiteCrawler/TemplateMatcherTests.cs
git commit -m "feat: match site intent to reusable templates"
```

---

### Task 4: Template Compiler And Converter Wiring

**Files:**
- Create: `packages/csharp/workers/site-crawler-worker/Services/TemplateCompiler.cs`
- Modify: `packages/csharp/workers/site-crawler-worker/Services/SiteGeneratorConverter.cs`
- Test: `packages/csharp/tests/unit/Workers/SiteCrawler/TemplateCompilerTests.cs`
- Test: `packages/csharp/tests/unit/Workers/SiteCrawler/SiteGeneratorConverterTests.cs`

- [ ] **Step 1: Write failing compiler tests**

Assert:

```csharp
document.Routes.Should().HaveCount(crawl.Pages.Count);
Flatten(document.Routes[0].Root).Select(node => node.Type).Should().Contain(["MegaHeader", "HeroCarousel", "QuickLinkRibbon", "NewsCardCarousel", "InstitutionFooter"]);
document.ComponentLibrary.Components.Should().NotContain(component => component.Generated);
document.ComponentRequests.Should().BeEmpty();
allInternalLinks.Should().OnlyContain(link => link.StartsWith("/") && !link.Contains(".aspx"));
```

Add a fallback test where manifest lacks `HeroCarousel` and assert `ComponentRequests` is non-empty while output still validates.

- [ ] **Step 2: Run focused tests and verify failure**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter "TemplateCompilerTests|SiteGeneratorConverterTests"
```

Expected: compile/test failure because compiler does not exist and converter still emits low-level high structure.

- [ ] **Step 3: Implement compiler**

The compiler builds:

- `PageShell` root per route.
- `MegaHeader` or `SiteHeader` from shared header intent.
- Slot component nodes in template order.
- `InstitutionFooter` or `SiteFooter` from shared footer intent.
- `FormBlock` for extracted forms.

Component props:

```csharp
MegaHeader: title, logo_url, logo_alt, utility_links, primary_links, search_enabled
HeroCarousel: title, body, slides
HeroBanner: title, body, media_url, media_alt
QuickLinkRibbon: title, links
NewsCardCarousel: title, items
NewsGrid: title, items
MediaFeatureGrid: title, items
ArticleList: title, items
ContentArticle: title, body, media
InstitutionFooter: source_url, logo_url, logo_alt, contact_text, links
```

Link normalization must remove `.aspx`, `.html`, and `.htm`, route same-origin crawled URLs to generated paths, and avoid rendering raw page URL names as standalone visual navigation.

- [ ] **Step 4: Wire converter**

In `SiteGeneratorConverter.Convert`:

```csharp
var intent = new SiteIntentExtractor().Extract(crawl);
var templates = new TemplateFrameworkLoader().LoadDefault();
var plan = new TemplateMatcher(templates, manifest).Match(intent);
return new TemplateCompiler(manifest).Compile(crawl, intent, plan);
```

Retain old direct helpers only as private fallback if template loading fails; do not call `EnsureGeneratedComponent` in the normal rebuild path.

- [ ] **Step 5: Run focused tests and commit**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter "TemplateCompilerTests|SiteGeneratorConverterTests"
```

Expected: pass.

Commit:

```powershell
git add packages/csharp/workers/site-crawler-worker/Services/TemplateCompiler.cs packages/csharp/workers/site-crawler-worker/Services/SiteGeneratorConverter.cs packages/csharp/tests/unit/Workers/SiteCrawler/TemplateCompilerTests.cs packages/csharp/tests/unit/Workers/SiteCrawler/SiteGeneratorConverterTests.cs
git commit -m "feat: compile rebuilds through template plans"
```

---

### Task 5: High-Level Component Library And Runtime Renderers

**Files:**
- Modify: `packages/csharp/workers/site-crawler-worker/component-libraries/bricks4agent.default/manifest.json`
- Modify: `packages/csharp/workers/site-crawler-worker/Services/StaticSitePackageGenerator.cs`
- Test: `packages/csharp/tests/unit/Workers/SiteCrawler/StaticSitePackageGeneratorTests.cs`
- Test: `packages/csharp/tests/unit/Workers/SiteCrawler/ComponentLibraryLoaderTests.cs`

- [ ] **Step 1: Write failing runtime tests**

Assert generated runtime contains renderers for:

```csharp
renderMegaHeader
renderHeroCarousel
renderQuickLinkRibbon
renderNewsCardCarousel
renderInstitutionFooter
```

Assert `DefaultComponentLibrary.Create()` includes all high-level component types and no generated components.

- [ ] **Step 2: Run focused tests and verify failure**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter "StaticSitePackageGeneratorTests|ComponentLibraryLoaderTests"
```

Expected: fail because high-level components and renderers are not registered.

- [ ] **Step 3: Extend manifest**

Add reusable component definitions for:

```text
MegaHeader, HeroCarousel, HeroBanner, QuickLinkRibbon, NewsCardCarousel,
NewsGrid, MediaFeatureGrid, InstitutionFooter, ArticleList, ContentArticle
```

Each props schema must declare exactly the props emitted by `TemplateCompiler`.

- [ ] **Step 4: Extend runtime**

Register renderers in `componentRenderers`, style the new classes, and render high-level nodes by composing existing runtime primitives. Internal links must still pass through `configureLink`.

- [ ] **Step 5: Run focused tests and commit**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter "StaticSitePackageGeneratorTests|ComponentLibraryLoaderTests|ComponentSchemaValidatorTests"
```

Expected: pass.

Commit:

```powershell
git add packages/csharp/workers/site-crawler-worker/component-libraries/bricks4agent.default/manifest.json packages/csharp/workers/site-crawler-worker/Services/StaticSitePackageGenerator.cs packages/csharp/tests/unit/Workers/SiteCrawler/StaticSitePackageGeneratorTests.cs
git commit -m "feat: add reusable site template components"
```

---

### Task 6: Full Verification And Live Smoke

**Files:**
- No planned production changes.

- [ ] **Step 1: Run focused site crawler tests**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter SiteCrawler
```

Expected: all site crawler tests pass.

- [ ] **Step 2: Run full unit suite**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --no-restore
```

Expected: all unit tests pass.

- [ ] **Step 3: Build worker**

Run:

```powershell
dotnet build packages/csharp/workers/site-crawler-worker/SiteCrawlerWorker.csproj
```

Expected: build succeeds.

- [ ] **Step 4: Live SHU depth-3 rebuild smoke**

Run the existing local broker/flow harness for `https://www.shu.edu.tw/` with depth `3`. Verify:

```text
route count equals crawl page count
unknown component types = 0
generated component count = 0
home page includes MegaHeader, HeroCarousel or HeroBanner, NewsCardCarousel or NewsGrid, InstitutionFooter
same-origin .aspx/.html/.htm links are rewritten to extensionless local routes
package contains index.html, runtime.js, styles.css, site.json, components/manifest.json
```

- [ ] **Step 5: Browser package smoke**

Open generated `index.html` through a local static server and verify the page renders header/main/footer without console errors.

- [ ] **Step 6: Commit final fixes**

If verification requires small fixes, commit them with:

```powershell
git add <changed-files>
git commit -m "test: verify site template framework"
```

---

## Self-Review

- Spec coverage: contracts, deterministic intent extraction, matcher, compiler, manifest-only output, component requests, runtime rendering, link rewriting, route preservation, and live SHU smoke are covered by Tasks 1-6.
- Placeholder scan: no task uses open-ended TODO/TBD language; each task has concrete files, assertions, commands, and expected results.
- Type consistency: contracts use `TemplateFrameworkManifest`, `SiteIntentModel`, `TemplatePlan`, and `GeneratorSiteDocument` consistently across loader, extractor, matcher, compiler, and converter wiring.

## Execution Choice

The user has already asked to proceed. Because subagents were not explicitly requested in this turn, execute this plan inline with `superpowers:executing-plans` and use commits at the task checkpoints when practical.
