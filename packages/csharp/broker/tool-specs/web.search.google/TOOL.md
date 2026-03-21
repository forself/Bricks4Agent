# web.search.google

Purpose: broker-mediated public web search through Google for high-level query tasks.

Current status: planned. The spec is kept in the registry, but the current public Google entrypoint does not yet yield a stable broker-owned execution adapter.

Rules:

- the high-level model must request this tool through the broker
- the model does not directly call Google
- result summaries must identify the engine and preserve result URLs
- this tool is intended for query and research support, not direct state-changing work
