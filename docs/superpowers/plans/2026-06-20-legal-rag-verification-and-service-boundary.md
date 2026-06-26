# Legal RAG Verification And Service Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the earlier legal RAG proof-of-concept as a verified, low-coupling core capability for state externalization and evidence-backed retrieval.

**Architecture:** Keep the existing broker endpoints and routes, but move retrieval behavior into a BrokerCore service that depends only on `BrokerDb`, `SharedContextEntry`, `VectorEntry`, `EmbeddingService`, and optional `RagPipelineService`. Legal RAG verification uses offline fixtures, deterministic fake embeddings, SQLite FTS5, and vector rows, so it does not depend on the live law site or Ollama.

The retrieval core is also hosted by `packages/csharp/rag-service`, a standalone minimal API process with `/healthz` and `/rag/retrieve`. The host depends on BrokerCore and SQLite, not on broker conversation, LINE, approval, or agent-dispatch layers.

**Tech Stack:** C# / .NET 8, BaseOrm, SQLite FTS5, BrokerCore services, broker verify executable, npm signed validation scripts.

**Status:** Completed on 2026-06-20. Verified with solution build, signed broker-scope verification, signed DB validation, agent registry/config tests, and standalone `rag-service` smoke testing.

---

### Task 1: Add Deterministic Legal RAG Verification

**Files:**
- Modify: `packages/csharp/broker/verify/Program.cs`

- [x] **Step 1: Write the failing test**

Add a broker verify block that creates a temp SQLite DB, initializes it, imports a tiny Consumer Protection Act fixture, manually inserts deterministic vectors, dispatches `rag_retrieve`, and asserts:
- `SharedContextEntry` contains the legal article.
- `memory_fts` returns it for a CJK legal query.
- vector retrieval returns the same legal entry with a positive vector score.
- tag filtering excludes unrelated entries.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj`

Expected: FAIL because the deterministic fake embedding provider cannot override `EmbeddingService.EmbedAsync`, or because the retrieval core is still duplicated and not testable through a stable service boundary.

### Task 2: Introduce BrokerCore RAG Retrieval Boundary

**Files:**
- Create: `packages/csharp/broker-core/Services/Fts5TextNormalizer.cs`
- Create: `packages/csharp/broker-core/Services/RagRetrievalService.cs`
- Modify: `packages/csharp/broker-core/Services/EmbeddingService.cs`
- Modify: `packages/csharp/broker/Adapters/InProcessDispatcher.cs`
- Modify: `packages/csharp/broker/Handlers/Rag/RagRetrieveHandler.cs`
- Modify: `packages/csharp/broker/Helpers/Fts5Utility.cs`

- [x] **Step 1: Make embedding deterministic-testable**

Change `EmbeddingService.EmbedAsync`, `EmbedBatchAsync`, and `EmbedWithModelAsync` to `virtual` so verify can use a subclass without network access.

- [x] **Step 2: Add FTS5 normalizer in BrokerCore**

Move reusable CJK query/content normalization into `Fts5TextNormalizer`.

- [x] **Step 3: Add `RagRetrievalService`**

Implement fulltext, semantic vector, RRF fusion, optional query rewrite, optional rerank, tag filtering, and convlog inclusion in BrokerCore.

- [x] **Step 4: Thin broker adapters**

Update dispatcher and handler to parse payloads and call `RagRetrievalService` instead of maintaining duplicate retrieval logic.

### Task 3: Update Manuals To Match Verified Reality

**Files:**
- Modify: `docs/manuals/current-user-manual.zh-TW.md`
- Modify: `docs/manuals/current-technical-manual.zh-TW.md`

- [x] **Step 1: Document the legal RAG module**

State that the old legal RAG POC exists and is now verified through offline legal fixture tests.

- [x] **Step 2: Document guarantees and limits**

Clarify that deterministic verification covers SQLite state externalization, FTS5 retrieval, vector rows, fake embedding vector retrieval, RRF, and tag filtering. Live law-site seeding and live Ollama embeddings remain operational/integration paths.

- [x] **Step 3: Document commands**

Mention `npm run validate:broker-scope:signed` and `npm run validate:db:signed` for Windows SAC/WDAC hosts.

### Task 4: Verify And Clean Up

**Files:**
- Inspect: `package.json`
- Inspect: `.gitignore`

- [x] **Step 1: Build**

Run: `dotnet build packages/csharp/ControlPlane.slnx`

- [x] **Step 2: Broker verify**

Run: `npm run validate:broker-scope:signed`

- [x] **Step 3: DB validation**

Run: `npm run validate:db:signed`

- [x] **Step 4: Cleanup**

Confirm no new `packages/csharp/broker/broker.db*` or `.test-output/` artifacts remain from this work.
