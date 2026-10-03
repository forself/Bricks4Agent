# travel.rail.search

Purpose: broker-mediated Taiwan Railways timetable and ticket lookup.

Current status: active.

Implementation:

- TDX only (source label `TDX 台鐵時刻表 API`); there is no public-web fallback. If TDX is not configured, fails, or returns nothing, the tool returns an empty result

- high-level LINE `?rail` queries are routed through `transport.query`, not directly through this tool

Rules:

- sources must be declared in source policy

- this tool is for TRA / 台鐵 / 火車 lookups, not THSR / 高鐵

- schedule results must be treated as time-sensitive

- responses must identify source and retrieval time

- returned options are candidate schedules from the TDX timetable
