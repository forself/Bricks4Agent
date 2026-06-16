# site-crawler-worker

Crawls a website and reconstructs a deterministic static package whose components are a closed
projection of the canonical `ui_components` (B) library.

## Broker mode (default)

Run with no subcommand to connect to the broker and serve capabilities
(`site.crawl_source`, `site.generate_package`, `site.reconstruct_package`):

```bash
dotnet run --project packages/csharp/workers/site-crawler-worker
```

## Local `reconstruct` CLI (no broker)

Runs one crawl → reconstruct-package locally, invoking the same production
`SiteReconstructPackageHandler` the broker path uses. Useful for an end-to-end smoke without
standing up a broker.

```bash
dotnet run --project packages/csharp/workers/site-crawler-worker -- \
  reconstruct --url https://www.ntut.edu.tw/ --out ./out --package-name ntut --max-pages 5
```

| Flag | Default | Meaning |
|------|---------|---------|
| `--url` | (required) | Start URL to crawl |
| `--out` | `%TEMP%/bricks4agent-generated-sites` | Output directory |
| `--package-name` | `generated-site` | Package folder / archive name |
| `--max-pages` | `8` | Page budget (visual capture capped at 5) |
| `--max-depth` | `1` | Link depth (same-origin) |
| `--visual` | `true` | Playwright DOM render (needs Chromium) |
| `--archive` | `true` | Also write a deterministic `.zip` |
| `--quality-gate` | `false` | Fail if any component gap / generated component |
| `--json` | `false` | Also print the full `SiteReconstructPackageResult` JSON |
| `--timeout-seconds` | `240` | Crawl wall-clock budget |

Output package: `index.html`, `runtime.js`, `styles.css`, `site.json`,
`components/{manifest.json, b-binding.json}`, `README.md`. `b-binding.json` is the flat
`type -> b_component` index proving the output's vocabulary is a closed projection of B.

> Visual mode launches headless Chromium via Microsoft.Playwright (browsers auto-download on
> first use). Set `--visual false` to skip it (HTML-only crawl, fewer extracted regions).
