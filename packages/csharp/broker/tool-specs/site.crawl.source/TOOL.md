# Site Crawl Source

Safely crawls a public HTTP/HTTPS website within a confirmed path-depth scope and returns static public HTML source plus deterministic visual/function cues for downstream generator reconstruction.

## Capability

- Tool ID: `site.crawl.source`
- Capability ID: `site.crawl_source`
- Route: `site_crawl_source`
- Status: `beta`
- Risk: medium

## Required Pre-Execution Confirmation

Before execution, the user must confirm the crawl depth as one of:

- first layer under the path: `max_depth = 1`
- within two layers: `max_depth = 2`
- N layers: `max_depth = N`

Root/current path only, `max_depth = 0`, is supported only as an explicit manual/safety mode when requested.

The scope is path-depth based. The default safety posture is same-origin only with path-prefix lock enabled.

## Safety Rules

- Public HTTP/HTTPS URLs only.
- No authenticated access, delegated credentials, cookies, private sessions, localhost, loopback, link-local, or private-network targets.
- Do not widen from the confirmed same-origin/path-prefix scope during execution.
- Budgets are safety limits, not substitutes for user-confirmed path depth.

## Output Contract

The result is a `SiteCrawlResult` package containing `crawl_run_id`, `status`, `root`, `pages`, `excluded`, `extracted_model`, and `limits`.

Pages include source-oriented fields such as `html`, `text_excerpt`, `links`, and `forms`. The extracted model provides deterministic cues: sections, text hierarchy, links, forms, limited inline/CSS-token hints, and a route graph.

The output is not rendered layout capture, screenshot capture, full CSS/assets capture, JavaScript behavior capture, or functional flow capture.

## Downstream Generator Rule

Use the extracted cues as static visual and functional reference only. The downstream converter must map those cues to the custom component library; it must not produce a DOM, pixel, label-equivalent, rendered-behavior, or otherwise equivalent clone. If no existing component maps to a referenced function or layout cue, the converter phase must generate or propose a new component.
