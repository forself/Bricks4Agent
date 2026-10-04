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

- the default smoke test can force a broker-mediated `read_file` tool call through the file worker

What it does not prove:

- the canonical LINE ingress path

- the local admin console at `line-admin.html`

- Google Drive delivery

- Azure VM IIS deployment

- browser-governed execution

- the full high-level conversation/query/production routing flow

Those parts currently live in the Windows sidecar path under [`packages/csharp/workers/line-worker/README.md`](../../../packages/csharp/workers/line-worker/README.md) and the broker runtime itself.

## Stack Variants

- `compose.yml`: mock Ollama protocol stack with broker, governed agent, file worker, and LINE worker

- `compose.openai-compatible.yml`: mock OpenAI-compatible stack

- `compose.ollama-host.yml`: broker + governed agent against a host-side Ollama server

## Canonical Local Use

From the repo root:

```bash
npm run validate:podman-governed-stack
```

The validation script prebuilds each image with an explicit `Containerfile`, then runs `podman compose up` without the compose build flag. This is the preferred smoke test on Windows Podman because some `podman-compose` versions do not reliably honor per-service `dockerfile:` entries during `up --build`.

The validation scripts generate a fresh set of broker keys and worker credentials in memory for every run (see [Secrets](#secrets)) and pass them to both `up` and `down` through the environment. Nothing is written to disk.

For manual runs, generate a secrets file outside the repository once, then pass it with `--env-file` to every `up` and `down`.

The manual command blocks in this README are PowerShell, like the runbook and the environment setup guide. In bash, replace `$env:NAME = 'value'` with `export NAME='value'`; keep the double quotes around the env file path in either shell, because the home directory may contain spaces.

```powershell
node tools/agent/container/gen-stack-secrets.mjs
podman compose --env-file "$HOME/.bricks4agent/agent-stack.env" -f tools/agent/container/compose.yml up --build --abort-on-container-exit --exit-code-from agent
```

The generator prints the exact path it wrote; it differs from the default when `BRICKS4AGENT_SECRETS_DIR` is set.

The default stack uses the bundled mock upstream and should end with the agent printing `STACK_OK`.

The smoke stack disables broker RAG seeding and embeddings so it only verifies the governed agent, broker LLM proxy, and worker attachment path. This avoids accidental dependence on a host-side Ollama embedding server.

The default `npm run validate:podman-governed-stack` run also configures the mock upstream to request `read_file` once, so the test covers the agent submitting a broker-governed capability request and the broker dispatching it to `file-worker`.

OpenAI-compatible stack:

```powershell
podman compose --env-file "$HOME/.bricks4agent/agent-stack.env" -f tools/agent/container/compose.openai-compatible.yml up --build --abort-on-container-exit --exit-code-from agent
```

Host Ollama stack:

```powershell
$env:STACK_MODEL = 'qwen3.6:latest'
podman compose --env-file "$HOME/.bricks4agent/agent-stack.env" -f tools/agent/container/compose.ollama-host.yml up --build --abort-on-container-exit --exit-code-from agent
```

To stop and remove a stack (`down` also needs the env file, because compose expands the required variables for every command):

```powershell
podman compose --env-file "$HOME/.bricks4agent/agent-stack.env" -f tools/agent/container/compose.yml down -v
```

## Port Notes

Default exposed broker ports:

- `compose.yml`: `5000`

- `compose.openai-compatible.yml`: `5361`

- `compose.ollama-host.yml`: `5002`

Every published port (broker, mock LLM, LINE worker webhook) is bound to `127.0.0.1` only. Containers still reach each other over the compose networks; only access from other machines is closed.

The `5361` default in `compose.openai-compatible.yml` collides with the Windows LINE sidecar broker default. If the sidecar is running, override the compose broker port before starting:

```powershell
$env:BROKER_PORT = '5601'
podman compose --env-file "$HOME/.bricks4agent/agent-stack.env" -f tools/agent/container/compose.openai-compatible.yml up --build --abort-on-container-exit --exit-code-from agent
```

The default LINE worker container webhook port in `compose.yml` is `19090`. That is a worker-container development port, not the canonical Windows sidecar ingress port `5357`. Because it is bound to `127.0.0.1`, expose it to LINE through a tunnel running on the host.

## Default Development Identity

The compose stacks seed development principals and tasks into the broker. The default stack uses:

- `principal_id`: `prn_podman_dev`

- `task_id`: `task_podman_dev`

- `role_id`: `role_reader`

The other compose files seed their own development principal/task pairs.

The compose files no longer embed any keys; see [Secrets](#secrets).

Each compose file also seeds a `runtime_descriptor` onto the task. That descriptor is the task architecture hook used by the broker to issue:

- task-bound default model

- `allow_model_override` policy

- explicit capability grants

- per-grant scope overrides

## Secrets

The compose files have no default keys. Each secret variable uses the required form `${VAR:?...}`, so `podman compose` stops with a message that points at the generator when a value is missing.

Required variables:

- all three compose files: `BROKER_SCOPED_TOKEN_SECRET`, `BROKER_MASTER_KEY_BASE64`, `BROKER_ECDH_PRIVATE_KEY_BASE64`, and `BROKER_ECDH_PUBLIC_KEY_BASE64` (the agent pins this public key; it must be the pair of the private key)

- `compose.yml` also: `LINE_WORKER_AUTH_KEY_ID`, `LINE_WORKER_AUTH_SHARED_SECRET`, `FILE_WORKER_AUTH_KEY_ID`, `FILE_WORKER_AUTH_SHARED_SECRET`, `EXEC_ADAPTER_AUTH_KEY_ID`, and `EXEC_ADAPTER_AUTH_SHARED_SECRET` (the broker side and the worker side read the same variables)

`node tools/agent/container/gen-stack-secrets.mjs` generates all of them with `node:crypto` and writes them to `$BRICKS4AGENT_SECRETS_DIR/agent-stack.env`, or `~/.bricks4agent/agent-stack.env` when that variable is not set. It never prints the values, refuses any path inside the repository, and refuses to overwrite an existing file unless you pass `--force` (which rotates every key; recreate the stack with `down -v` afterwards). `--self-test` checks the generator without writing anything. [`agent-stack.env.example`](agent-stack.env.example) lists the variable names only.

Keep the file outside the repository. The agent container mounts the repository at `/workspace` with read access, and compose also auto-loads a `.env` next to the compose files, so a key file inside the repository would be readable by the untrusted agent.

The broker runs in the Production environment in these stacks and validates its keys at startup: it refuses placeholder values (empty, `CHANGE_ME*`, `REPLACE_WITH_*`) and any key that was ever published as a compose default, and it checks key formats. Do not set `ASPNETCORE_ENVIRONMENT=Development` in the compose files to get around this.

`WORKER_AUTH_ENFORCE` defaults to `true`: the broker verifies worker credentials on function pool registration and on the LINE worker HTTP routes. The broker also trusts the execution adapter credential (credential index 2), which replaces the template credential at that index in `appsettings.json`.

## Overrides

You can override the defaults with environment variables before starting the stack:

```powershell
$env:BROKER_PORT = '5500'
$env:STACK_MODEL = 'llama3.1'
$env:AGENT_RUN = 'Read README.md and summarize it in one sentence.'
podman compose --env-file "$HOME/.bricks4agent/agent-stack.env" -f tools/agent/container/compose.yml up --build --abort-on-container-exit --exit-code-from agent
```

Required secrets have no defaults and are not overrides; see [Secrets](#secrets) for the four broker key variables and, for `compose.yml`, the six worker credential variables.

Supported overrides:

- `BROKER_PORT`

- `STACK_LLM_PORT`

- `STACK_MODEL`

- `STACK_RESPONSE_TEXT`

- `STACK_TOOL_CALL`

- `STACK_TOOL_PATH`

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

- `WORKER_AUTH_ENFORCE` (default `true`)

- `AGENT_BROKER_URL`

- `AGENT_RUN`

- `AGENT_VERBOSE`

- `AGENT_LINE_LISTEN`

- `AGENT_LINE_POLL_INTERVAL`

Limits on these overrides:

- `BROKER_ROLE_ID` must not name an administrator-level role (`role_admin`, or any role whose allowed task types include `*`). The broker registers administrator-level sessions only for callers on its own loopback interface; the agent registers over the compose network, so the broker refuses the registration with HTTP 403.

- `WORKER_AUTH_ENFORCE=false` turns off worker credential verification. The three LINE worker routes (`POST /api/v1/high-level/line/process`, `GET /api/v1/high-level/line/notifications/pending`, `POST /api/v1/high-level/line/notifications/complete`) accept only a verified worker signature, so with verification off they answer HTTP 401 to every request and the LINE worker stops working. Keep the default `true` whenever the LINE worker runs.

`AGENT_BROKER_URL` controls the HTTP broker URL injected into dynamically spawned agent containers through `/api/v1/agents/spawn`. In the compose stack the default is `http://broker:5000`; for a host-side broker, point it at the broker address reachable from the container, such as `http://host.containers.internal:5361` on Podman Desktop.

`/api/v1/agents/spawn` requires an administrator scoped token (`role_admin`). The broker issues an administrator-level session only when the registration arrives over the broker host's own loopback interface and the task assigns an administrator role. The agent containers in these stacks reach the broker over the compose network, so the spawn flow is not available inside them.

## Switching To A Real Upstream

The broker is already wired to use the upstream through `LlmProxy__BaseUrl`.

For a real provider:

- replace `mock-ollama` or `mock-openai`

- point `LlmProxy__BaseUrl` to the actual upstream

- provide the required provider key through environment variables
- use `Provider=anthropic`, `LlmProxy__ApiFormat=messages`, and `claude-sonnet-4-6` for Claude Messages API paths

- use `Provider=anthropic`, `LlmProxy__ApiFormat=messages`, and `claude-sonnet-4-6` for Claude Messages API paths

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
