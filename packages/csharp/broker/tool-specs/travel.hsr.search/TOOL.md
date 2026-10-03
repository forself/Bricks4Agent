# travel.hsr.search

Purpose: broker-mediated Taiwan High Speed Rail timetable lookup.

Current status: active.

Implementation:

- TDX only (source label `TDX 高鐵時刻表 API`); there is no public-web fallback. If TDX is not configured, fails, or returns nothing, the tool returns an empty result

- high-level LINE `?hsr` queries are routed through `transport.query`, not directly through this tool

Rules:

- sources must be declared in source policy

- this tool is for THSR / 高鐵 lookups, not TRA / 台鐵 / 火車

- schedule results must be treated as time-sensitive

- responses must identify source and retrieval time

- returned options are candidate schedules from the TDX timetable
