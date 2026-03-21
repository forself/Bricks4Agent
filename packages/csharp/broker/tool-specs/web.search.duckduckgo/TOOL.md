# web.search.duckduckgo

Purpose: broker-mediated public web search through DuckDuckGo for high-level query tasks.

Current status: active. This spec is registered in the broker and mapped to a broker-owned search route.

Rules:

- the high-level model must request this tool through the broker
- the model does not directly call DuckDuckGo
- result summaries must identify the engine and preserve result URLs
- this tool is intended for query and research support, not direct state-changing work
