# B4A GIS components (opt-in beta)

This module contains **only browser components, JSON definitions/generation,
instance-scoped factory integration and an HTTP API adapter**. It contains no
GIS backend, SQLite/MBTiles database, server authentication implementation,
data importer, tile renderer or service deployment package.

Those responsibilities live in the independent **simGis** project. A compatible
service may be implemented by any project; B4A does not require simGis, .NET,
a database file, service executable or a particular filesystem path.

## Generate and connect

From B4A repository root, with Node 22:

```sh
node modules/offline-gis/tools/generate.mjs modules/offline-gis/fixtures/gis.page.json /new/client-site https://gis.example.test/custom/api/
```

The final argument is trusted deployment configuration, outside page JSON.
It writes `service-config.js` and permits that origin in the generated CSP.
Omit it to use relative `api/gis/` on the same origin. Serve the generated static
site with any web server. B4A does not launch a GIS server. Changing the deployed
service origin requires updating both service-config.js and CSP.

For a host-managed application:

```js
import Page from './GisPage.js';
import { mountGis } from './modules/offline-gis/frontend/runtime.js';
import { GisApi } from './modules/offline-gis/frontend/api.js';
const api = new GisApi('https://gis.example.test/custom/api/', {
  headers: () => ({ Authorization: `Bearer ${hostManagedToken}` }),
});
const app = await mountGis(Page, container, { api });
// On navigation:
app.destroy();
```

Cross-origin services must explicitly allow the client origin through CORS and
must be permitted by host CSP. The adapter does not send cross-origin cookies.
Authentication/authorization and per-user data isolation are service duties;
client visibility or possession of generated code grants no server privileges.
No credentials or executable code belong in the page JSON.

## Components and generation

`OfflineGisMap`, `GisSearch`, `GisScope`, `GisTimeline` are composable through a
local factory. Manifests follow B4A's existing schema. `PageGenerator` creates a
class using existing `DynamicToolRenderer`; no global registry, existing component,
route, authentication default or common dependency is changed.

The UI provides Canvas map navigation, markers, density grid, date filtering/
playback, circle/polygon selection and result listing. Exact address ranking and
spatial query answers come from the configured service. Density grid is not a
statistically significant hotspot analysis. Map rendering uses service-provided
PNG tiles and has no public CDN fallback. The JSON fixture is a UI test definition,
not a database or national map pack.

See [API contract](docs/API-CONTRACT.md) and [split record](docs/BOUNDARY-SPLIT.md).

## Frontend-only tests

```sh
node --test modules/offline-gis/tests/core.test.mjs
PLAYWRIGHT_MODULE=/absolute/node_modules/playwright-core/index.mjs CHROMIUM_PATH=/usr/bin/chromium node modules/offline-gis/tests/browser.mjs
```

The browser test uses a test-only HTTP contract double on a separate origin.
It runs the actual B4A JSON generator, rendered components and adapter without
.NET, SQLite, data tools or simGis. Cross-project real-service tests are maintained
in simGis and take the B4A checkout path explicitly; they are not B4A product runtime.
