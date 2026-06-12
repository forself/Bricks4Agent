# Site Template Framework Design

Date: 2026-05-02

Status: proposed

## Purpose

Site rebuild currently satisfies the "component library only" constraint, but the visual result is limited because the generator maps visual regions directly into low-level components such as `AtomicSection`, `TextBlock`, `ImageBlock`, and `CardGrid`. The missing layer is a reusable template framework above the component library and below `site.json` generation.

The template framework must let the system reconstruct a website through reusable page skeletons and typed slots, while still enforcing that final output uses only declared component-library components. It must not generate a one-off component set per source website.

## Non-Goals

- Do not create site-specific React components or JavaScript modules during rebuild.
- Do not clone original DOM, CSS, labels, or behavior equivalently.
- Do not reduce crawl depth, route coverage, or internal-link rewriting to improve layout quality.
- Do not let an LLM freely emit renderable UI. Any LLM involvement must be advisory and converted into validated template intent.

## Pipeline

The intended pipeline becomes:

```text
Rendered Visual Snapshot + Auxiliary Source Extraction
  -> SiteIntentModel
  -> TemplateMatcher
  -> TemplatePlan
  -> TemplateCompiler
  -> GeneratorSiteDocument
  -> StaticSitePackageGenerator
```

`SiteGeneratorConverter` should stop acting as a direct visual-region-to-component mapper for high-level page structure. It should delegate page skeleton and slot decisions to the template framework, then compile selected slots into component nodes.

## Template Framework Concepts

### Template Definition

A template definition describes a reusable website family, such as `institutional_site`, `corporate_site`, or `news_site`. The first implementation should ship only `institutional_site` because the current failing example is a university WebForms website and this template class also covers many public-sector and school sites.

Each template definition contains:

- `template_id`
- `supported_site_kinds`
- page type rules
- global layout policy
- shared slot policy
- allowed component types per slot
- fallback component types per slot

Example shape:

```json
{
  "template_id": "institutional_site",
  "supported_site_kinds": ["university", "school", "public_agency"],
  "page_types": {
    "home": {
      "slots": [
        { "name": "header", "required": true, "accepts": ["MegaHeader", "SiteHeader"] },
        { "name": "hero", "required": false, "accepts": ["HeroCarousel", "HeroBanner", "AtomicSection"] },
        { "name": "quick_links", "required": false, "accepts": ["QuickLinkRibbon", "CardGrid"] },
        { "name": "news", "required": false, "accepts": ["NewsCardCarousel", "NewsGrid", "CardGrid"] },
        { "name": "features", "required": false, "accepts": ["MediaFeatureGrid", "AtomicSection"] },
        { "name": "footer", "required": true, "accepts": ["InstitutionFooter", "SiteFooter"] }
      ]
    },
    "listing": {
      "slots": [
        { "name": "header", "required": true, "accepts": ["MegaHeader", "SiteHeader"] },
        { "name": "content", "required": true, "accepts": ["ArticleList", "CardGrid", "AtomicSection"] },
        { "name": "footer", "required": true, "accepts": ["InstitutionFooter", "SiteFooter"] }
      ]
    },
    "article": {
      "slots": [
        { "name": "header", "required": true, "accepts": ["MegaHeader", "SiteHeader"] },
        { "name": "content", "required": true, "accepts": ["ContentArticle", "ContentSection", "AtomicSection"] },
        { "name": "footer", "required": true, "accepts": ["InstitutionFooter", "SiteFooter"] }
      ]
    }
  }
}
```

The compiler may only choose a component type if it exists in the loaded component manifest. If a preferred component is not present, it must fall back to another accepted type that exists. If no accepted type exists, it must emit a `ComponentRequest` and fall back to the lowest safe generic type already in the manifest.

### Site Intent Model

`SiteIntentModel` is a deterministic intermediate model created from rendered visual snapshots and auxiliary static extraction. It describes intent, not render components.

Initial fields:

- `SiteKind`: `institutional`, `corporate`, `news`, `unknown`
- `Pages`: per-page intent records
- `GlobalNavigation`: normalized shared navigation candidates
- `GlobalHeader`: shared logo/navigation/search/social candidates
- `GlobalFooter`: shared contact/link candidates
- `ThemeHints`: color and typography tokens

Per-page intent records:

- `PageUrl`
- `PageType`: `home`, `listing`, `article`, `department`, `contact`, `unknown`
- `Blocks`: ordered visual intent blocks

Visual intent block kinds:

- `header`
- `mega_nav`
- `hero_carousel`
- `hero_banner`
- `quick_links`
- `news_grid`
- `news_carousel`
- `media_feature_grid`
- `article_list`
- `content_article`
- `form`
- `footer`
- `unknown`

The first implementation can build this model from existing `VisualPageSnapshot`, `ExtractedPageModel`, route depth, URL patterns, link density, media counts, and text density. It does not require LLM output.

### Template Matcher

`TemplateMatcher` chooses:

- site template
- page type per route
- slot assignment per page
- component candidate per slot

Matcher rules must be deterministic and testable. Examples:

- Depth 0 page is usually `home`.
- Page with repeated date/news links and many cards is `listing`.
- Page with one dominant heading and long text body is `article`.
- Top viewport block with logo and high link density maps to `header` / `mega_nav`.
- First large media-rich block on home maps to `hero_carousel` or `hero_banner`.
- Repeated cards with dates/images map to `news_grid` or `news_carousel`.
- Footer-like bottom block with address/contact links maps to `footer`.

The matcher should produce confidence scores and reasons for audit, but low confidence must not block generation. Low confidence slots fall back to generic manifest components.

### Template Compiler

`TemplateCompiler` converts a `TemplatePlan` into `GeneratorSiteDocument`.

Responsibilities:

- Build one `PageShell` per route.
- Apply shared header/footer strategy consistently across pages.
- Fill template slots in declared order.
- Choose only manifest-declared component types.
- Normalize internal links through the existing route map.
- Preserve route count from crawl result.
- Record component gaps as `ComponentRequest` without adding generated components to output.

It must not:

- infer new component types not declared in the manifest;
- mutate the component library manifest with site-specific generated components;
- place raw crawl links as visual navigation unless they are assigned to a header/footer/slot.

## Component Library Evolution

The template framework requires richer reusable components, but they must be added as first-class component-library entries, not generated per site.

First target components:

- `MegaHeader`
- `HeroCarousel`
- `HeroBanner`
- `QuickLinkRibbon`
- `NewsCardCarousel`
- `NewsGrid`
- `MediaFeatureGrid`
- `InstitutionFooter`
- `ArticleList`
- `ContentArticle`

The initial implementation may define these in the default manifest and render them in `runtime.js` using existing atomic substructure. This still satisfies "component library only" because these are reusable library components declared before generation.

## File-Level Design

Planned production files:

- `packages/csharp/workers/site-crawler-worker/Models/TemplateFrameworkContracts.cs`
- `packages/csharp/workers/site-crawler-worker/Services/TemplateFrameworkLoader.cs`
- `packages/csharp/workers/site-crawler-worker/Services/SiteIntentExtractor.cs`
- `packages/csharp/workers/site-crawler-worker/Services/TemplateMatcher.cs`
- `packages/csharp/workers/site-crawler-worker/Services/TemplateCompiler.cs`
- `packages/csharp/workers/site-crawler-worker/template-framework/institutional_site.json`

Planned updates:

- `SiteGeneratorConverter` delegates high-level page construction to the template framework.
- `DefaultComponentLibrary` / default component manifest gain reusable high-level components.
- `StaticSitePackageGenerator` runtime gains renderers for the new reusable components.
- Tool docs clarify that rebuild is template-slot based, not direct DOM-to-component mapping.

## Validation Rules

Every generated package must satisfy:

- `UnknownNodeTypes = 0`
- `GeneratedComponentCount = 0` for site rebuild output
- all component node types are declared in `components/manifest.json`
- all internal same-origin links route to generated local paths
- internal routes do not keep `.aspx`, `.html`, or `.htm` extensions
- home page has header, main content, and footer
- if visual intent has carousel, output uses `HeroCarousel`, `NewsCardCarousel`, or a declared carousel-capable fallback
- route count remains equal to crawl page count unless crawl itself is truncated

## Testing Plan

Unit tests:

- load and validate `institutional_site` template definition;
- classify a university home page intent as `institutional_site/home`;
- assign header, hero carousel, news, quick links, and footer slots;
- compile a template plan using only manifest-declared component types;
- fall back when preferred high-level components are absent;
- record component requests for missing reusable components without adding generated component definitions;
- preserve route count and internal link rewriting.

Integration / smoke:

- run site rebuild against the existing fake broker path;
- run a live-host smoke against `https://www.shu.edu.tw/` depth 3;
- verify generated package has local `index.html`, `site.json`, `runtime.js`, `components/manifest.json`;
- verify browser runtime renders header/main/footer, images/cards/carousel, and local navigation without console errors.

## Risks

- Overfitting `institutional_site` to one university website would recreate the original problem at a higher layer. Mitigation: template definitions must describe site families and slot contracts, not source-specific labels or URLs.
- Too many high-level components at once can destabilize runtime rendering. Mitigation: ship the template framework with a small first component set and generic fallbacks.
- Static pages beyond the rendered visual snapshot cap may have weaker layout intent. Mitigation: page type and shared template strategy come from representative rendered pages, while static extraction fills page-specific content.

## Acceptance Criteria

The work is complete when:

- site rebuild uses `SiteIntentModel -> TemplatePlan -> TemplateCompiler` for page skeleton construction;
- the default rebuild path for the SHU depth-3 example finishes in roughly the current optimized range, not the earlier 10-minute range;
- generated output uses only `bricks4agent.default` component-library components;
- generated output includes high-level template components where available;
- no site-specific generated components are added for SHU;
- all listed validation checks pass in automated tests and the live smoke.
