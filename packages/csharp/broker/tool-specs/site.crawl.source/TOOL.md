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

Root/current page only, `max_depth = 0`, is supported only as an explicit manual/safety mode when requested.

The scope is link-depth based. URL path depth is not used as the website layer definition. Link-depth crawls are breadth-first: lower layers must be completed before deeper layers when any safety budget is reached.

For institutional or multi-subdomain sites, callers may include public same-site subdomains with `scope.allowed_host_suffixes`, for example `["ntub.edu.tw"]`. This keeps public HTTP/HTTPS and private-network safety checks while allowing `www.ntub.edu.tw`, `sec.ntub.edu.tw`, and similar subdomains to be crawled as one site. Do not use this to widen to unrelated domains.

## Safety Rules

- Public HTTP/HTTPS URLs only.
- No authenticated access, delegated credentials, cookies, private sessions, localhost, loopback, link-local, or private-network targets.
- Do not widen from the confirmed same-origin or allowed same-site host-suffix scope during execution.
- Budgets are safety limits, not substitutes for user-confirmed path depth.

## Output Contract

The result is a `SiteCrawlResult` package containing `crawl_run_id`, `status`, `root`, `pages`, `excluded`, `extracted_model`, and `limits`.

Pages include rendered visual snapshots for a bounded set of representative pages when available: visible regions, layout boxes, text hierarchy, media, links, forms, and source selectors. The crawl can still include more pages/routes than rendered snapshots; source-oriented fields such as `html`, `text_excerpt`, `links`, and `forms` remain auxiliary evidence.

The output is rendered layout cue capture, but it is not full CSS/assets capture, full JavaScript behavior capture, authenticated flow capture, or an equivalent clone.

## Downstream Generator Rule

Use rendered visual cues as the primary reconstruction reference and source HTML only as auxiliary evidence. The downstream converter must map those cues to the custom component library; it must not produce a DOM, pixel, label-equivalent, rendered-behavior, or otherwise equivalent clone. If no existing component maps to a referenced function or layout cue, the converter phase must first compose from atomic components and only then record a component-library gap.
