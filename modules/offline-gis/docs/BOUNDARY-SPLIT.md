# Architecture split — 2026-10-27

The user explicitly assigned GIS tiles, queries, storage and server APIs to the
independent **simGis** project. This supersedes the combined beta architecture.

## Preservation and scope

Original B4A commit: `4d3588b3c875e82cd9e86ad83ca301cd177b7d40` (40 module files).
20 files remain in B4A; 20 service files were copied byte-for-byte before removal.
The complete original commit stays in B4A history. A full tracked snapshot is at
`/workspace/b4a-baseline/offline-gis-presplit-4d3588b.tar.gz`.
simGis preservation commit: `bfb5cda534e357ca692a844c66612c5c13cb6240`.
Its `SPLIT-INVENTORY.json` maps every original file. Subsequent split commits do
not rewrite either preservation commit. simGis has no Git remote.

- `/workspace/Bricks4Agent/modules/offline-gis`: opt-in browser components,
  JSON definition, schema/manifest, existing PageGenerator/DynamicToolRenderer
  integration, local factory, HTTP adapter and API contract. No service startup,
  database, data preparation or server authentication implementation remains.
- `/workspace/simGis`: independently runnable .NET API, access policy, geometry,
  SQLite/MBTiles, fixture/data preparation, service publishing and service tests.
  Only the explicit cross-project test accepts a B4A path. Production code and
  normal service package do not resolve or load B4A.

B4A's trusted host configures the service base URL and authorization header
callback. Low-trust JSON cannot choose endpoints, credentials or executable code.
Any independently implemented service conforming to API-CONTRACT.md can be used.
Cross-origin access is opt-in via exact server origin allowlists; default-deny
authentication and per-endpoint capabilities remain unchanged. Anonymous mode
remains explicitly shared and cannot provide private per-user isolation.

## Compatibility and tests

The split changes only files introduced under `modules/offline-gis` in B4A.
A diff against upstream `714e0cca748c2bbfcd4c2bc53a90c3644ea922ba` outside that
directory is empty. No LeafletMap, OSMMapEditor, TGOS, shared dependency, registry,
existing authentication default or public route has changed.

Passed after split: B4A 8 core tests; generator-to-browser mock test on a subpath;
simGis Release build (0 warnings/errors); 12 Python data/backend tests; six-RID
publish-plan test; real cross-project browser integration with separate client
and service origins, local tiles, markers, address lookup, circle/polygon query,
density, time/playback and lifecycle cleanup. Both browser runs blocked every
unconfigured origin and observed zero external requests/page errors.
CORS tests cover allowed/unknown/default origins and no credential sharing.
Backend regressions retain read-only bearer case/trailing-slash denial, >10000
points with a small circle, antimeridian, address subnumber identity, sidecar
no-overwrite, malformed input, geometry boundary and filesystem containment.

## Outstanding inputs and stopped operations

Only synthetic fixture data is present: five fabricated addresses, 92 simple
unlabeled PNG tiles. Taiwan/Penghu/Kinmen/Matsu real map and address completeness
are NOT verified. The basic renderer is not production OSM cartography. A legal
local PBF (plus Osmium on the preparation host), mature styled/labelled rendering
with fonts and coastline/land data, or a licensed ready PNG MBTiles pack remains
needed. Deployment does not require Osmium, Python or a database service.
Windows/macOS and ARM native execution have not been tested; six-RID plan support
or cross-publishing is not evidence of native platform operation.

Geofabrik HEAD returned HTTP 403; issuer/reason is unresolved. It was not retried.
The Library helper tool-discovery request was explicitly blocked by proxy CONNECT
403 before mutation; no upload happened, no alternative endpoint/write route was
used, and retry remains stopped. The old Library beta is stale and not the split
result. Local packages and source are retained; no push, PR, remote repository,
merge or deployment occurred.

Earlier combined source/portable packages and docs/history evidence describe the
pre-split beta and are superseded architecturally; they remain preserved rather
than relabeled as new deliverables. Their SHA values remain in historical
DELIVERY-STATUS.md. The new split is saved by new local Git commits.

## Final split verification

- `npm test` in B4A: passed, including 63 Vitest files / 1148 tests and
  generator/UI/custom-component suites (Node test totals 69, 14 and 85).
- `npm run audit:csp`: passed, 444 files, zero CSP/SVG violations.
- `npm run validate:ui-library`: passed; its optional general browser smoke
  was not requested. Dedicated GIS mock and cross-project browser tests passed.
- simGis Linux x64 service-only native fixture: clean extraction into a path
  with spaces, PATH `/usr/bin:/bin`, no installed dotnet/Node/Python needed at
  runtime; strace found zero `/workspace/` or toolchain accesses. Native SQLite
  lookup/write/query, local PNG tile and `/gis/` subpath passed. Root UI returns
  404 and no B4A public directory is packaged.
- Archive `/workspace/simGis-linux-x64-FIXTURE-split.tar.gz`, 48,771,642 bytes;
  SHA256 `9c7930c99bd537a81a13f36d6d73d3434fba6db9bd5eba72a7e36ca659a05a2a`.
  Archive listing and extraction passed. Launch `./run.sh` inside the extracted
  `simgis-linux-x64-fixture` directory, status at
  `http://127.0.0.1:5085/gis/api/gis/status`. Explicit anonymous shared data.
  OS dependencies remain glibc, libgcc/libstdc++, zlib, ICU and OpenSSL; a
  self-contained .NET package is not an OS-independent native binary.
- The archive was made before the final split commits, from the tested service
  build; it contains native runtime + synthetic data/launcher, not a source
  snapshot. Source commits also include tests/docs not inside the native bundle.
- Existing B4A .NET aggregate was not rerun for this frontend-only split.
  Pre-split baseline had pre-existing dependency/security warnings and failures
  documented in simGis `docs/history/VERIFICATION.md`; they are not resolved by
  moving this additive module. simGis's own build and tests were rerun and passed.

Reproduction (activate the provided cloud SDK/Node with
`source /workspace/b4a-baseline/env.sh` if needed):

```sh
# B4A root
node --test modules/offline-gis/tests/core.test.mjs
PLAYWRIGHT_MODULE=/workspace/toolchains/browser-test/node_modules/playwright-core/index.mjs node modules/offline-gis/tests/browser.mjs
npm test
npm run audit:csp
npm run validate:ui-library
# simGis root
dotnet build backend/SimGis.csproj -c Release --warnaserror
python3 -m unittest discover -s tests -p 'test_*.py' -v
node --test tests/publish.test.mjs
PLAYWRIGHT_MODULE=/workspace/toolchains/browser-test/node_modules/playwright-core/index.mjs node tests/cross-project.mjs /workspace/Bricks4Agent
node tools/package-fixture.mjs linux-x64 /new/path/simgis-linux-x64-fixture
# Archive the directory, then verify on Linux x64 (requires strace for test only):
node tests/portable.mjs /path/to/service-fixture.tar.gz
```

Logs for the unchanged B4A aggregate are retained at
`/workspace/b4a-baseline/split-npm-test.log`, `split-csp.log` and
`split-ui-library.log`. No Library upload was attempted during this split.
