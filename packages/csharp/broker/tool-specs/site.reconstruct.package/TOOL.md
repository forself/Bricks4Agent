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

## Flow

1. Crawl only the confirmed public HTTP/HTTPS scope.
2. Prefer rendered visual/function cues when available; use source HTML as auxiliary evidence.
3. Convert the site intent into template slots and component-library nodes.
4. Enforce the quality gate by default.
5. Write a package whose entry point is `index.html`.

The package runtime loads `site.json` and `components/manifest.json`, then renders the component tree. It must not write arbitrary page HTML or a DOM-equivalent clone.

## Strict Quality Gate

Strict mode is enabled by default. It blocks package creation when:

- the document contains unresolved `component_requests`;
- the manifest declares generated components;
- routes use unknown component types;
- route paths are duplicated;
- a route root is not `PageShell`.

On failure the tool returns a structured `quality_report` and does not write the package.

## Output

The success result includes:

- `crawl_run_id`
- `page_count`
- `excluded_count`
- `package.output_directory`
- `package.entry_point`
- `package.site_json_path`
- `package.manifest_path`
- `package.files`
- `package.quality_report`

The result is a reusable reconstruction package, not an equivalent clone of the source website.
