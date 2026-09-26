# travel.flight.search

Purpose: broker-mediated flight lookup for travel planning.

Current status: active.

Implementation:

- TDX only (source label `TDX 航班即時資訊 API (FIDS)`); there is no public-web fallback. If TDX is not configured, fails, or returns nothing, the tool returns an empty result

- high-level LINE `?flight` queries are routed through `transport.query`, not directly through this tool

Rules:

- sources must be defined in policy, not hardcoded in the model prompt

- schedule and availability information must be treated as time-sensitive

- responses must identify source and retrieval time

- returned options are candidate flights from TDX flight information (FIDS)
