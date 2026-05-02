# Site Crawl Source

Safely crawls a public HTTP/HTTPS website within a confirmed link-depth scope and returns rendered visual/function cues plus auxiliary source hints for downstream generator reconstruction.

## Capability

- Tool ID: `site.crawl.source`
- Capability ID: `site.crawl_source`
- Route: `site_crawl_source`
- Status: `beta`
- Risk: medium

## Required Pre-Execution Confirmation

Before execution, the user must confirm the crawl depth as one of:

- entry page links: `max_depth = 1`
- within two link hops: `max_depth = 2`
- N link hops: `max_depth = N`

Root/current path only, `max_depth = 0`, is supported only as an explicit manual/safety mode when requested.

The scope is link-depth based. URL path depth is not used as the website layer definition. The default safety posture is same-origin only with path-prefix lock enabled.

## Safety Rules

- Public HTTP/HTTPS URLs only.
- No authenticated access, delegated credentials, cookies, private sessions, localhost, loopback, link-local, or private-network targets.
- Do not widen from the confirmed same-origin/path-prefix scope during execution.
- Budgets are safety limits, not substitutes for user-confirmed path depth.

## Output Contract

The result is a `SiteCrawlResult` package containing `crawl_run_id`, `status`, `root`, `pages`, `excluded`, `extracted_model`, and `limits`.

Pages include rendered visual snapshots when available: visible regions, layout boxes, text hierarchy, media, links, forms, and source selectors. Source-oriented fields such as `html`, `text_excerpt`, `links`, and `forms` remain auxiliary evidence.

The output is rendered layout cue capture, but it is not full CSS/assets capture, full JavaScript behavior capture, authenticated flow capture, or an equivalent clone.

## Downstream Generator Rule

Use rendered visual cues as the primary reconstruction reference and source HTML only as auxiliary evidence. The downstream converter must map those cues to the custom component library; it must not produce a DOM, pixel, label-equivalent, rendered-behavior, or otherwise equivalent clone. If no existing component maps to a referenced function or layout cue, the converter phase must first compose from atomic components and only then record a component-library gap.
