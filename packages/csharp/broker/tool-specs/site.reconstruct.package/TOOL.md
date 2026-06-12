# Site Reconstruct Package

Crawls a confirmed public URL, converts rendered visual/function cues into a component-library `site.json`, enforces generation quality, and writes a local static website package.

## Capability

- Tool ID: `site.reconstruct.package`
- Capability ID: `site.reconstruct_package`
- Route: `site_reconstruct_package`
- Status: `beta`
- Risk: medium

## Required Pre-Execution Confirmation

Before execution, the user must confirm crawl depth as link depth:

- entry page links: `max_depth = 1`
- within two link hops: `max_depth = 2`
- N link hops: `max_depth = N`

Root/current page only, `max_depth = 0`, is supported as an explicit diagnostic/manual mode.

The crawl is breadth-first for `link_depth`: lower layers must be completed before deeper layers when any safety budget is reached. URL path depth is not used as the website layer definition.

For institutional or multi-subdomain sites, callers may include public same-site subdomains with `scope.allowed_host_suffixes`, for example `["ntub.edu.tw"]`. This keeps public HTTP/HTTPS and private-network safety checks while allowing `www.ntub.edu.tw`, `sec.ntub.edu.tw`, and similar subdomains to be reconstructed as one site. Do not use this to widen to unrelated domains.

Rendered visual snapshots may be bounded for performance, but this bound must not be used as the page/route crawl limit.

## Flow

1. Crawl only the confirmed public HTTP/HTTPS scope.
2. Prefer rendered visual/function cues when available; use source HTML as auxiliary evidence.
3. Convert the site intent into template slots and component-library nodes.
4. Enforce the quality gate by default.
5. Write a package whose entry point is `index.html`.
6. Write a portable zip archive by default for artifact delivery.
7. Verify the package files, `site.json` renderability, and archive contents.

The package runtime loads `site.json` and `components/manifest.json`, then renders the component tree. It must not write arbitrary page HTML or a DOM-equivalent clone.

## Strict Quality Gate

Strict mode is enabled by default. It blocks package creation when:

- the document contains unresolved `component_requests`;
- the manifest declares generated components;
- routes use unknown component types;
- route paths are duplicated;
- a route root is not `PageShell`.

On failure the tool returns a structured `quality_report` and does not write the package.

Strict mode also blocks delivery when package verification fails. The tool returns a structured `verification_report`, and failed package artifacts are removed when possible.

## Package Verification

Successful output includes `package.verification_report`. The verifier checks:

- `index.html`
- `runtime.js`
- `styles.css`
- `site.json`
- `components/manifest.json`
- `README.md`
- `index.html` declares `#app` and loads `./runtime.js`
- `runtime.js` loads `./site.json` and `./components/manifest.json`
- `components/manifest.json` declares every component type used by `site.json`
- every non-generated component type used by `site.json` has a `runtime.js` renderer
- generated component types have local `components/generated/<Type>.js` and `.json` assets
- zip archive entries

For normal user delivery, both `package.quality_report.is_passed` and `package.verification_report.is_passed` must be true.

The `package.verification_report.runtime_renderer_types` field lists renderer keys parsed from `runtime.js`.

## Output

The success result includes:

- `crawl_run_id`
- `page_count`
- `excluded_count`
- `package.output_directory`
- `package.entry_point`
- `package.site_json_path`
- `package.manifest_path`
- `package.archive_path`
- `package.files`
- `package.quality_report`
- `package.verification_report`

The result is a reusable reconstruction package, not an equivalent clone of the source website.
