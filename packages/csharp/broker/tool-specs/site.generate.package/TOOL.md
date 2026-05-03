# Site Generate Package

Generates a local static website package from either a `SiteCrawlResult` or a valid `GeneratorSiteDocument`.

By default this tool enforces the site generation quality gate. The package is written only when the document can be rendered with the loaded component library without generated components or unresolved component requests. Set `enforce_quality_gate = false` only for diagnostic package output.

Set `create_archive = true` to also write a portable zip file. If `archive_path` is omitted, the archive is created next to the package directory as `<package_name>.zip`.

## Capability

- Tool ID: `site.generate.package`
- Capability ID: `site.generate_package`
- Route: `site_generate_package`
- Status: `beta`
- Risk: medium

## Package Contract

The package entry point is `index.html`. The HTML file is only a shell containing `#app` and loading `runtime.js`.

The runtime loads:

- `site.json`
- `components/manifest.json`

It then renders the site from the component tree in `site.json`.

The worker loads the component library manifest from `Generator:ComponentLibraryPath` when configured. If it is empty, the bundled default manifest at `component-libraries/bricks4agent.default/manifest.json` is used.

## Component Rule

The generated package must render only component types declared in `components/manifest.json`.

If source cues cannot be represented by a built-in component, the conversion step must add a generated local component definition to the manifest and record a `component_requests` entry. The runtime may render that generated component only because it is declared in the manifest.

The generator must not produce arbitrary page HTML, DOM-equivalent clones, pixel-equivalent clones, or hidden free-form layout outside the component tree.

In strict mode, generated component definitions and component requests are treated as quality failures. The caller receives a structured `quality_report` and no package is written.

## Output

The result includes:

- `output_directory`
- `entry_point`
- `site_json_path`
- `manifest_path`
- `archive_path`
- `files`
- `quality_report`
