# GIS service API contract v1

The trusted host configures a trailing-slash HTTP(S) service base URL. The URL
must not include username/password, query or fragment. Requests are relative to
that base; the adapter rejects escape to another origin or parent path. The
service can be independently implemented and deployed. No endpoint starts a
server, exposes a database path, accepts SQL or imports executable code.

| Relative endpoint | Method | Request | Success |
|---|---|---|---|
| `addresses?q=...` | GET | URL-encoded query | `{candidates:[{address,lon,lat,level,match,source?,region?}]}` |
| `tiles/{z}/{x}/{y}.png` | GET | XYZ integers; browser uses z0–18 | PNG bytes, `image/png`, 256×256 |
| `markers` | POST | JSON `{label,lon,lat,time,weight?}` | `{id}` |
| `query` | POST | JSON `{bbox:[west,south,east,north],polygon?,circle?,from?,to?}` | `{items:[{id,label,lon,lat,time,weight}],exact:true,candidates?}` |

Coordinates use WGS84 longitude/latitude; Web Mercator display latitude range is
±85.05112878, longitude ±180. `time`, `from`, `to` are Unix milliseconds, inclusive,
0..8640000000000000. Marker label is nonempty text, max 200 characters; finite
weight is 0..1000000, default 1. Marker coordinates/time are required.

`bbox` does not wrap the dateline. A polygon is one 3..256 vertex ring, boundary
inclusive, even/odd semantics, no holes or spans over 180° longitude. A circle is
`[lon,lat,radiusMetres]`, 0 < radius ≤ 2000000, using spherical distance with
Earth radius 6371008.8m. Circle queries may cross the dateline; the server must
perform exact filtering, not return RTree bbox matches as exact. Combined spatial
and time filters are intersections. `exact:true` applies to filtering, not survey
accuracy or source completeness. A service may reject overly broad queries.

`level` reports source coordinate precision (e.g. address-point/building-vertex),
not a promise of a rooftop or entrance. `match` reports ranking/normalization
(e.g. exact-normalized/prefix). Services must not silently conflate distinct
house numbers such as `1-1` and `11`; normalization details remain service-owned.
The client displays candidates and does not manufacture address coordinates.

Status semantics: 400 invalid/overly broad request; 401/403 absent identity or
capability; 404 missing tile/endpoint; 503 missing/incompatible service data.
Non-2xx responses cause adapter errors surfaced in UI. Requests are abortable.
The current UI expects one bounded result list, not a pagination protocol.

Authentication is host/service integration: a trusted callback supplies headers.
Never embed a token in JSON definitions, URLs or generated static source. Cross-
origin bearer use requires the service to allow the client origin, Authorization
and Content-Type headers, and GET/POST preflight. Cookie CSRF policies are the
host/service's responsibility. Anonymous mode, if offered by a service, cannot
promise reliable per-user isolation. The service must derive record ownership
from a trusted identity, never a caller-supplied owner field.

For an OSM standard relay, tile fetches send the browser's accurate origin-only
Referer (`referrerPolicy: origin`); the service must preserve it upstream and
validate the client origin. The map displays a fixed visible link to OSM copyright.
No tile URL/provider key is accepted from page JSON. Only interactive viewport
requests are made; this component has no prefetch/offline-download facility.
