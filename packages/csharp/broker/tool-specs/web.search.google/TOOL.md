# web.search.google

Purpose: broker-mediated public web search through Google for high-level query tasks.

Current status: active. The high-level `?search` path now prefers this Google route and falls back to DuckDuckGo only when Google fails.

Rules:

- the high-level model must request this tool through the broker
- the model does not directly call Google
- result summaries must identify the engine and preserve result URLs
- this tool is intended for query and research support, not direct state-changing work
