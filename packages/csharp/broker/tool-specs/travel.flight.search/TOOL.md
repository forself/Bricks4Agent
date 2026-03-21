# travel.flight.search

Purpose: broker-mediated flight lookup for travel planning.

Current status: planned. This spec exists so the broker can register the tool contract before provider selection is finalized.

Rules:

- sources must be defined in policy, not hardcoded in the model prompt
- prices and availability must be treated as time-sensitive
- responses must identify source and retrieval time
