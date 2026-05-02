# Site Crawl Source

Safely crawls a public HTTP/HTTPS website within a confirmed path-depth scope and returns a deterministic source/reference package for downstream generator reconstruction.

## Capability

- Tool ID: `site.crawl.source`
- Capability ID: `site.crawl_source`
- Route: `site_crawl_source`
- Status: `beta`
- Risk: medium

## Required Pre-Execution Confirmation

Before execution, the user must confirm the crawl depth as one of:

- first layer
- within two layers
- N layers

The scope is path-depth based. The default safety posture is same-origin only with path-prefix lock enabled.

## Safety Rules

- Public HTTP/HTTPS URLs only.
- No authenticated access, delegated credentials, cookies, private sessions, localhost, loopback, link-local, or private-network targets.
- Do not widen from the confirmed same-origin/path-prefix scope during execution.
- Budgets are safety limits, not substitutes for user-confirmed path depth.

## Output Contract

The result is a `SiteCrawlResult` package containing `crawl_run_id`, `status`, `root`, `pages`, `excluded`, `extracted_model`, and `limits`.

Pages include source-oriented fields such as `html`, `text_excerpt`, `links`, and `forms`. The extracted model includes page sections, theme tokens, and a route graph for later reconstruction.

## Downstream Generator Rule

Use the crawled site as visual and functional reference only. The downstream converter must reconstruct approximate visual layout and functions with the custom component library; it must not produce a DOM, pixel, label-equivalent, or otherwise equivalent clone. If no existing component maps to a referenced function or layout, the converter phase must generate or propose a new component.
