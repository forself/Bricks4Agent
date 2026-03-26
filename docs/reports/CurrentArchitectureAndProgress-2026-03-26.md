# Bricks4Agent Current Architecture And Progress

Date: 2026-03-26
Status: current working report for the live system and main development direction

## 1. Executive Summary

`Bricks4Agent` is no longer just an agent CLI or a page/code generator. The project has already moved into a control-plane direction:

- LINE ingress
- broker-governed routing
- high-level conversation and task planning
- governed execution
- per-user managed workspaces
- artifact generation and delivery
- browser-governed capability groundwork
- Azure VM IIS deployment groundwork

That is the real shape of the system now.

The honest summary is:

- the system is already useful as a broker-mediated AI operations prototype
- the architecture is becoming coherent
- several core paths are genuinely live
- but the whole platform is still uneven in maturity

This is not a finished platform. It is a serious POC with a growing amount of real infrastructure behind it.

## 2. Core Architectural Position

The system is now built around three clearly different roles.

### 2.1 High-level entry model

This layer handles:

- user conversation
- clarification
- query rewriting
- candidate intent formation
- workflow confirmation
- execution-model suggestion

This layer is allowed to interpret. It is not allowed to execute arbitrary side effects by itself.

### 2.2 Broker / control plane

The broker is responsible for:

- parsing
- workflow gating
- trust and taint boundaries
- memory projection
- execution-intent promotion
- capability and scope enforcement
- artifact and delivery coordination
- admin views and control surfaces

The broker should not behave like an autonomous planner. Its value is that it narrows behavior and makes it inspectable, repeatable, and governable.

### 2.3 Execution layer

This includes:

- governed agent runtime
- worker processes
- tool routes
- deployment execution
- browser execution groundwork

This layer should consume structured intent, not raw conversation.

## 3. Canonical Live Path

The current production-style path is:

`LINE webhook -> line-worker -> broker /api/v1/high-level/line/process -> HighLevelCoordinator`

Current local canonical sidecar ports:

- broker: `127.0.0.1:5361`
- line-worker webhook: `127.0.0.1:5357`

Public ingress is currently tunneled through ngrok.

Important clarification:

- `--line-listen` on the agent side is now legacy/development-only
- the real canonical LINE path is `line-worker -> broker high-level coordinator`

## 4. High-level Model Layer

The LINE high-level responder is now configured to use:

- provider: `openai-compatible`
- model: `gpt-5.4-mini`

This high-level model is used for:

- conversation
- clarification
- mediated query synthesis
- execution-model suggestion

It is intentionally separate from execution/runtime model selection.

Important principle:

- the entry model may propose execution model usage
- the broker validates and records it
- the broker should not freely improvise model selection on its own

## 5. High-level Parsing, Gating, and Memory

The high-level entry path already has explicit structure.

### 5.1 Command grammar

Supported explicit forms include:

- `?help` / `?h`
- `?search` / `?s`
- `?rail` / `?r`
- `?hsr` / `?thsr`
- `?bus` / `?b`
- `?flight` / `?f`
- `?profile` / `?p`
- `/name` / `/n`
- `/id` / `/i`
- `#projectName`
- `confirm`
- `cancel`

This grammar exists to narrow what can become executable intent.

### 5.2 Workflow state machine

The coordinator does not accept everything everywhere. Core workflow states are explicitly gated, especially around:

- production start
- project-name capture
- confirmation
- cancellation

### 5.3 Trust / taint boundary

The current high-level path distinguishes between:

- raw user input
- transformed/decoded input
- retrieved external content

Only trusted command-shaped user input should be allowed to affect workflow directly. This is still early, but the direction is correct.

### 5.4 Memory split

The system now distinguishes between:

- raw interaction log
- interpretation record
- memory projection
- execution intent

This is one of the most important structural improvements in the project.

The right mental model is:

- log stores raw truth
- memory stores reusable state
- execution intent stores approved, structured action

## 6. User Model and Managed Paths

Users in the LINE high-level layer are keyed by:

- `channel`
- `userId`

There is now support for:

- preferred display name
- preferred alphanumeric ID
- per-user permissions
- registration policy
- synthetic/test-user labeling

Managed workspace root is configured as an absolute path, not a relative path. The current model is:

- `{AccessRoot}/{channel}/{userId}/conversations`
- `{AccessRoot}/{channel}/{userId}/documents`
- `{AccessRoot}/{channel}/{userId}/projects/{projectName}`

This matters because capability path scope is meaningless until file placement is formalized.

## 7. Admin Console

There is now a working local admin console:

- `http://127.0.0.1:5361/line-admin.html`

Current behavior:

- localhost-only access
- local admin login
- initial password fallback if no password exists
- forced first password change
- logout support

The console already includes:

- LINE user list
- conversation views
- registration policy and review
- per-user permission toggles
- browser-related records
- deployment target views
- tool-spec views
- artifact and workflow visibility

This is already useful.

What it is not yet:

- a hardened multi-user production admin console
- a complete long-term admin product

## 8. Query and Tool Mediation

### 8.1 General mediated search

General search is no longer just raw search-result listing.

The current path:

- broker-mediated search tool
- then high-level synthesis over top results

This is the right direction. A high-level model that only forwards raw search results would not justify its existence.

### 8.2 Transport tools

Transport queries are split by mode:

- `?rail`
- `?hsr`
- `?bus`
- `?flight`

This is important. Rail and HSR were previously collapsed too loosely.

### 8.3 Relation-aware routing

A Wikipedia-first broker-mediated relation query path now exists for queries such as:

- administrative divisions
- nearby administrative areas
- core subject relation lookups

The current implementation is meaningfully better than raw search forwarding, but still not mature.

The system now does something more honest:

- uses Wikipedia-first evidence
- extracts candidate relation terms
- avoids confidently inventing neighboring-area answers when evidence is weak

This is better than confident nonsense, but still not equal to a real geographic relation engine.

## 9. Artifact Generation and Delivery

The system now supports document-style artifact generation in user-specific directories.

That includes:

- writing files into user documents paths
- artifact records
- admin visibility of artifacts

Google Drive delivery is also now wired in through delegated OAuth.

The current working delivery path is:

- generate file into user workspace
- upload to delegated Google Drive
- create share link
- queue notification for LINE delivery

This is already a real delivery chain, not a mock.

Current limitation:

- the final LINE send still depends on the target being a real LINE user ID
- test identities can complete upload and link generation, but not final real LINE delivery

## 10. Browser-Governed Capability Model

The browser-governance foundation is now significantly more mature than it was before.

The system already distinguishes browser identity modes:

- `anonymous`
- `system_account`
- `user_delegated`

It also has broker-side models for:

- site bindings
- user grants
- system bindings
- session leases
- browser execution requests/results

This is good architectural progress.

What is still missing:

- a full browser worker runtime for serious authenticated automation
- vault/credential lifecycle completion
- submission-grade action approval
- DOM/action policy enforcement at production quality

So the browser model is structurally real, but execution maturity is still partial.

## 11. Deployment

The broker now has Azure VM IIS deployment groundwork, including child-application deployment mode.

That means the system can move toward:

- publish
- package
- remote deploy
- IIS site or child-application update

This is strategically important because it proves the project is not just about generating artifacts, but also about governed delivery into real runtime environments.

What remains incomplete:

- stronger health-check automation
- better end-to-end deployment verification
- more polished operator flow

## 12. Hard Critique

This section is intentionally not flattering.

### 12.1 What is genuinely strong

- The project is no longer just feature sprawl. A real control-plane shape is emerging.
- The split between conversation, governance, execution, and delivery is increasingly coherent.
- The project already contains several working end-to-end paths, not just design notes.
- The system is becoming unusually strong in one area most prototypes neglect: governed transition from language to executable structure.

### 12.2 What is still weak

- Maturity is uneven. Some parts are real; some parts are still half-policy, half-implementation.
- Search and relation reasoning are improving, but still not reliable enough to claim deep knowledge competence.
- The admin console is useful, but it is still fundamentally a localhost-side operator console, not a hardened admin product.
- Browser governance is more complete on paper and broker records than in live authenticated execution.
- The system still carries historical layering residue. Old paths and newer canonical paths coexist more than they should.

### 12.3 What would be dishonest to claim

It would be dishonest to claim that the project is already:

- production-stable
- fully containerized end-to-end
- fully hardened against prompt injection
- browser-automation complete
- search-quality complete
- deployment-operations complete

It is not there yet.

### 12.4 What would also be dishonest to deny

It would also be dishonest to call this “just another toy agent repo”.

At this point, the repo already contains:

- live ingress
- governance
- structured state promotion
- delivery
- admin operations
- execution constraints
- deployment direction

That combination is materially stronger than a typical single-agent demo.

## 13. Recommended Near-Term Priorities

The highest-value remaining work is:

1. Continue improving mediated query quality
   - especially relation queries and source-aware synthesis
2. Harden sidecar/operator reliability
   - restart, publish, state visibility, failure reporting
3. Finish the delegated artifact delivery UX
   - especially real-user LINE delivery and admin affordances
4. Strengthen browser runtime execution
   - not just registry and records
5. Improve deployment verification
   - health checks, rollback posture, operator clarity

## 14. Bottom Line

The current project is best described as:

**a broker-centered governed AI operations prototype moving toward a real control plane**

That is the honest macro-level description.

It is already beyond a toy. It is not yet a complete platform. The most important thing now is not to pretend it is finished, but also not to underestimate how much real structure is already there.
