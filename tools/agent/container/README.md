# Governed Agent Podman Stack

This directory contains a narrow Podman stack for the governed agent execution path, not the whole Bricks4Agent system.

Included services:

- `mock-ollama`: deterministic Ollama-style upstream stub
- `mock-openai`: deterministic OpenAI-compatible upstream stub
- `broker`: scoped-session issuer, capability/scope policy engine, and LLM proxy
- `agent`: governed agent container that only talks to the broker
- `file-worker` and `line-worker`: optional worker containers in the default stack

## Scope

This stack proves the broker-mediated agent path. It does not prove the full production system.

What it proves:

- the agent container does not need a direct provider API key
- runtime spec and model traffic are fetched through the broker
- the broker forwards model traffic to an upstream service
- the agent only operates with the broker-issued session, role, capability, and scope
- the broker can issue task-specific runtime defaults and capability grants from `runtime_descriptor`
- worker containers can connect to the broker function pool in local container development

What it does not prove:

- the canonical LINE ingress path
- the local admin console at `line-admin.html`
- Google Drive delivery
- Azure VM IIS deployment
- browser-governed execution
- the full high-level conversation/query/production routing flow

Those parts currently live in the Windows sidecar path under [`packages/csharp/workers/line-worker/README.md`](/d:/Bricks4Agent/packages/csharp/workers/line-worker/README.md) and the broker runtime itself.

## Stack Variants

- `compose.yml`: mock Ollama protocol stack with broker, governed agent, file worker, and LINE worker
- `compose.openai-compatible.yml`: mock OpenAI-compatible stack
- `compose.ollama-host.yml`: broker + governed agent against a host-side Ollama server

## Canonical Local Use

From the repo root:

```bash
podman compose -f tools/agent/container/compose.yml up --build --abort-on-container-exit --exit-code-from agent
```

The default stack uses the bundled mock upstream and should end with the agent printing `STACK_OK`.

OpenAI-compatible stack:

```bash
podman compose -f tools/agent/container/compose.openai-compatible.yml up --build --abort-on-container-exit --exit-code-from agent
```

Host Ollama stack:

```bash
set STACK_MODEL=qwen3-coder:30b
podman compose -f tools/agent/container/compose.ollama-host.yml up --build --abort-on-container-exit --exit-code-from agent
```

To stop and remove a stack:

```bash
podman compose -f tools/agent/container/compose.yml down -v
```

## Port Notes

Default exposed broker ports:

- `compose.yml`: `5000`
- `compose.openai-compatible.yml`: `5361`
- `compose.ollama-host.yml`: `5002`

The `5361` default in `compose.openai-compatible.yml` collides with the Windows LINE sidecar broker default. If the sidecar is running, override the compose broker port before starting:

```bash
set BROKER_PORT=5601
podman compose -f tools/agent/container/compose.openai-compatible.yml up --build --abort-on-container-exit --exit-code-from agent
```

The default LINE worker container webhook port in `compose.yml` is `19090`. That is a worker-container development port, not the canonical Windows sidecar ingress port `5357`.

## Default Development Identity

The compose stacks seed development principals and tasks into the broker. The default stack uses:

- `principal_id`: `prn_podman_dev`
- `task_id`: `task_podman_dev`
- `role_id`: `role_reader`

The other compose files seed their own development principal/task pairs.

The ECDH keypair, broker token secret, and master key embedded in the compose files are development-only values. Do not reuse them outside local testing.

Each compose file also seeds a `runtime_descriptor` onto the task. That descriptor is the task architecture hook used by the broker to issue:

- task-bound default model
- `allow_model_override` policy
- explicit capability grants
- per-grant scope overrides

## Overrides

You can override the defaults with environment variables before starting the stack:

```bash
set BROKER_PORT=5500
set STACK_MODEL=llama3.1
set AGENT_RUN=Read README.md and summarize it in one sentence.
podman compose -f tools/agent/container/compose.yml up --build --abort-on-container-exit --exit-code-from agent
```

Supported overrides:

- `BROKER_PORT`
- `STACK_LLM_PORT`
- `STACK_MODEL`
- `STACK_RESPONSE_TEXT`
- `BROKER_PRINCIPAL_ID`
- `BROKER_TASK_ID`
- `BROKER_ROLE_ID`
- `BROKER_TASK_TYPE`
- `OPENAI_API_KEY`
- `OPENAI_API_FORMAT`
- `OLLAMA_BASE_URL`
- `LINE_CHANNEL_ACCESS_TOKEN`
- `LINE_CHANNEL_SECRET`
- `LINE_DEFAULT_RECIPIENT_ID`
- `LINE_ALLOWED_USER_IDS`
- `LINE_WEBHOOK_PORT`
- `AGENT_RUN`
- `AGENT_VERBOSE`
- `AGENT_LINE_LISTEN`
- `AGENT_LINE_POLL_INTERVAL`

## Switching To A Real Upstream

The broker is already wired to use the upstream through `LlmProxy__BaseUrl`.

For a real provider:

- replace `mock-ollama` or `mock-openai`
- point `LlmProxy__BaseUrl` to the actual upstream
- provide the required provider key through environment variables

The governed agent container does not change. It still only knows about:

- `BROKER_URL`
- `BROKER_PUB_KEY`
- `BROKER_PRINCIPAL_ID`
- `BROKER_TASK_ID`
- `BROKER_ROLE_ID`

## Relationship To The Current LINE Architecture

The canonical LINE production path is:

- `line-worker` receives webhook traffic
- `line-worker` forwards user messages to the broker high-level coordinator
- the broker decides `conversation`, `query`, or `production`
- only confirmed production work becomes task/plan/handoff state

That production path is currently exercised through the Windows sidecar scripts, not through this Podman stack. The agent's `--line-listen` mode is kept only as a legacy development path. It is not the primary production integration model.

## Practical Reading

Use this Podman stack when you want to verify:

- governed agent bootstrap
- broker-issued runtime descriptors
- capability/scope-gated execution
- upstream model proxying
- worker container attachment

Do not read a passing Podman run here as proof that:

- the admin console is healthy
- LINE is reachable from the public internet
- deployment targets are configured
- Google Drive delegated delivery is working
- browser-governed tools are production-ready
