# travel.flight.search

Purpose: broker-mediated flight lookup for travel planning.

Current status: active.

Rules:

- sources must be defined in policy, not hardcoded in the model prompt
- schedule and availability information must be treated as time-sensitive
- responses must identify source and retrieval time
- returned options are candidate schedules gathered from public travel pages
