# travel.bus.search

Purpose: broker-mediated bus and coach schedule lookup.

Current status: active.

Implementation:

- TDX only (source label `TDX 公車預估到站 API`); there is no public-web fallback. If TDX is not configured, fails, or returns nothing, the tool returns an empty result

- high-level LINE `?bus` queries are routed through `transport.query`, not directly through this tool

Rules:

- responses must identify source and retrieval time

- returned options are candidate schedules, not guaranteed final ticket availability
