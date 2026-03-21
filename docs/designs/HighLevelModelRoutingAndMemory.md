# High-Level Model Routing And Memory

## Goal

Define the control-plane role of the high-level model that fronts LINE or similar social channels.

The high-level model is not a low-level agent. Its job is to:

- receive and reply to user messages
- decide whether the request is only conversation/query or a production task
- use broker-managed memory
- ask for confirmation before creating production work
- create a `BrokerTask` and a draft `Plan` handoff when production work is confirmed

## Layered Memory

The mechanism uses three memory layers.

### 1. Conversation Memory

- scope: per channel user thread
- current implementation source: `convlog:{userId}`
- owner: high-level conversation gateway
- purpose: recent dialogue continuity, RAG over conversation history

### 2. User Memory

- scope: per channel user profile
- document id: `hlm.profile.{channel}.{userId}`
- purpose:
  - last routing decision
  - aggregate counts of conversation/query/production decisions
  - last created task id / plan id
  - pending draft presence

### 3. Task Memory

- scope: per pending or confirmed production task
- draft document id: `hlm.draft.{channel}.{userId}`
- handoff document id: `hlm.handoff.{taskId}`
- purpose:
  - store a pending production proposal awaiting confirmation
  - store the task tree skeleton passed into the broker workflow layer

## Managed Workspace Root

The high-level layer must define a broker-owned filesystem root for agent-accessible work.

- config key: `HighLevelCoordinator:AccessRoot`
- current default: `./managed-workspaces`
- this is the canonical agent-access root for high-level production tasks

Within that root, the current layout is:

```text
{AccessRoot}/
  {channel}/
    {userId}/
      conversations/
      documents/
      projects/
        {projectName}/
```

Rules:

- each channel user gets a dedicated user root
- conversation, document, and project artifacts are separated
- generated project work must live under `projects/{projectName}`
- the broker/runtime descriptor carries these paths forward as managed paths and path scope

Conversation memory is still primarily stored in broker-managed context documents.
The `conversations/` folder exists as the canonical per-user conversation-area path for downstream artifact usage, exports, or future file-backed conversation assets.

## Routing Gate

Incoming messages are routed into one of three modes.

### Conversation

- normal dialogue
- no external production workflow
- handled directly by the high-level model reply path

### Query

- search / explanation / RAG-assisted answer
- still no task tree
- handled directly by the high-level model reply path

### Production

- any request expected to create or mutate an external artifact
- examples:
  - new or modified files
  - new records
  - generated prototype / draft deliverable
  - structured work that must be handed to agent containers

Production requests do not immediately create tasks.
They first become a pending draft and require user confirmation.

If the production request is project-like, the high-level layer must first collect a project name before confirmation.

## Confirmation Flow

1. User sends a production-like request.
2. High-level coordinator classifies it as `production`.
3. Coordinator creates a pending draft in task memory.
4. If the draft requires a project workspace:
   - ask the user for a project name
   - validate that the project name is not empty
   - map it to a filesystem-safe project folder name
   - confirm that the target project folder does not already exist under the user's `projects/` root
5. Coordinator replies with a summary and asks for confirmation.
6. User replies with confirm/cancel language.
7. On confirm:
   - create `BrokerTask`
   - create draft `Plan`
   - write handoff context document
   - create the managed workspace directories under the configured access root
   - clear pending draft
8. On cancel:
   - clear pending draft
   - no task is created

Project name uniqueness is enforced per user workspace.
Two different users may have the same project name.
The same user may not create two project folders with the same final folder name.

## Handoff Artifact

The coordinator does not directly create executable low-level plan nodes.
Instead it creates:

- a `BrokerTask`
- a draft `Plan`
- a `task tree skeleton` stored as handoff context

That handoff contains:

- channel
- user id
- original request
- classified task type
- summary
- draft phases
- runtime hints
- memory references
- managed workspace paths
- project name / project root when applicable

This keeps the high-level layer responsible for planning intent, while later planning/execution layers can refine executable nodes.

## Current Implementation Boundary

This mechanism is intentionally limited to what the broker can own:

- routing
- memory
- confirmation state
- task creation
- plan creation
- handoff persistence

It does not claim to solve:

- final low-level execution planning
- all future channel integrations
- all natural-language routing edge cases

Those remain downstream concerns.

## Broker Surface

The current broker entrypoints for the high-level LINE coordinator are:

- `POST /api/v1/high-level/line/process`
  - body: `{ "user_id": "...", "message": "..." }`
  - behavior:
    - conversation/query: reply directly through the high-level path
    - production: create pending draft and ask for confirmation
    - confirm/cancel while draft exists: either create task/plan/handoff or clear the draft
- `POST /api/v1/high-level/line/profile`
  - body: `{ "user_id": "..." }`
  - returns the current high-level user profile memory
- `POST /api/v1/high-level/line/draft`
  - body: `{ "user_id": "..." }`
  - returns the current pending production draft, if any

The line conversation gateway remains the direct conversation/query executor.
The high-level coordinator is the routing and confirmation layer above it.
