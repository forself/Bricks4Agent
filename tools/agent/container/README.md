# Governed Agent Podman Stack

This directory contains a narrow Podman stack for the governed agent execution path, not the whole Bricks4Agent system.

Included services:

- `mock-ollama`: deterministic Ollama-style upstream stub

- `mock-openai`: deterministic OpenAI-compatible upstream stub

- `broker`: scoped-session issuer, capability/scope policy engine, and LLM proxy

- `agent`: governed agent container that only talks to the broker; nothing is mounted into it

- `file-worker` and `line-worker`: optional worker containers in the default stack

- `execution-adapter-worker`: patch and build/test worker, started only with `--profile adapters`

Every service runs hardened (non-root, read-only root filesystem, no capabilities); see [Container Hardening](#container-hardening).

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

The stack tests also run with Docker: set `CONTAINER_ENGINE=docker` (PowerShell: `$env:CONTAINER_ENGINE = 'docker'`) and they use `docker build` and `docker compose` instead; the default stays `podman`. After `up`, each test inspects every container and fails unless it has a read-only root filesystem, a non-root user, `cap_drop: ALL`, `no-new-privileges`, the expected `pids_limit`, a `/tmp` tmpfs, no runtime socket and, for the agent, no bind or volume mount. If a host service already uses a published port (for example a local Ollama on `11434`), move the stack with `STACK_LLM_PORT` or `BROKER_PORT`.

Other end-to-end checks:

```bash
npm run validate:podman-execution-adapter-stack
npm run validate:container-spawn
```

The first applies a patch and runs a real `dotnet build` through the execution adapter (profile `adapters`). The second starts a broker on the host with the container manager enabled, spawns an agent container through `/api/v1/agents/spawn`, and checks its hardening, its run and its removal; see [Container Hardening](#container-hardening).

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

The default `npm run validate:podman-governed-stack` run also configures the mock upstream to request `read_file` once, so the test covers the agent submitting a broker-governed capability request and the broker dispatching it to `file-worker`. The mock answers `STACK_OK TOOL_RESULT_VERIFIED` only when the tool result it receives through the broker contains text from `README.html`, so a passing run proves the file worker really read the file (the `[governed] read_file` log line alone is printed before the tool runs).

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

Keep the file outside the repository. The agent no longer mounts anything, but the file worker serves the repository read-only to the agent through the broker; its deny list refuses `.env`, `.env.*` and `agent-stack.env`, yet any other name you give a key file inside the repository would be readable by the untrusted agent. Compose also auto-loads a `.env` next to the compose files.

The broker runs in the Production environment in these stacks and validates its keys at startup: it refuses placeholder values (empty, `CHANGE_ME*`, `REPLACE_WITH_*`) and any key that was ever published as a compose default, and it checks key formats. Do not set `ASPNETCORE_ENVIRONMENT=Development` in the compose files to get around this.

`WORKER_AUTH_ENFORCE` defaults to `true`: the broker verifies worker credentials on function pool registration and on the LINE worker HTTP routes. The broker also trusts the execution adapter credential (credential index 2), which replaces the template credential at that index in `appsettings.json`.

## Container Hardening

Every service in the three compose files runs with the design §13.2 settings:

| Setting | Value |
|---|---|
| user | non-root UID from the image: agent 10001, broker 10002, file-worker 10003, execution-adapter 10004, line-worker 10005, mock-ollama 10006, mock-openai 10007 |
| `read_only` | `true`; only the `/tmp` tmpfs and the mounts below are writable |
| `tmpfs` | `/tmp` (Docker and Podman mount it `noexec,nosuid,nodev`) |
| `cap_drop` | `ALL` |
| `security_opt` | `no-new-privileges:true` |
| `pids_limit` | `1024` for the broker (.NET thread pool and sockets), `256` for every other service |

Mounts:

- broker: the named volume at `/data` (SQLite database and workspaces). No container runtime socket is mounted, so dynamic spawn is not available inside these stacks; keep `CONTAINER_MANAGER_ENABLED` at `false`.

- agent: nothing. `/workspace` is an empty directory in the image that only serves as the logical root the broker grants are scoped to; the project manual is baked in at `/app/AGENT.md` (`AGENT_MANUAL_PATH`).

- file-worker: the repository at `/workspace`, read-only. This is what the agent can read through the broker. The worker resolves every path, symlinks included, refuses anything outside `/workspace`, and applies one deny list to read, list, search, write and delete: `.git`, `.claude`, `.codegraph-cache`, `.ssh`, `.env`, `.env.*`, `agent-stack.env`, `appsettings.Development.json`, `*.pem`, `*.key`, `*.pfx`, `*.p12`, SSH private key names such as `id_rsa*`, and `client_secret_*`.

- execution-adapter-worker: the throwaway git workspace from `ADAPTER_WORKSPACE`, writable, because it is the mediated write path. `dotnet build` and `dotnet test` (with NuGet packages on the `noexec` `/tmp`) work within `pids_limit: 256`.

- line-worker: nothing; audio scratch files go to `/tmp/audio_temp` (`WORKER_Line__AudioTempPath`).

### Dynamic Spawn

A broker running on the host can start containers itself when `FunctionPool:ContainerManager:Enabled` is `true` (the Windows sidecar turns it on when it finds podman or docker). Every container it starts gets `--read-only`, `--tmpfs /tmp:rw,noexec,nosuid,nodev,size=64m`, `--cap-drop ALL`, `--security-opt no-new-privileges:true`, `--pids-limit` (`DefaultPidsLimit`, 256 unless an image sets `PidsLimit`) and a memory limit (`DefaultMemoryLimit`, `512m` unless an image sets `MemoryLimit`). The broker refuses configuration that would weaken this:

- a root user (`User` of `0` or `root`), or a `host`, `container:` or `ns:` network

- any `Volumes` or `Ports` on the `agent` image

- an `agent` image without its own `NetworkName`; agents never fall back to the shared worker network. Only a broker that runs on the host without a dedicated agent network sets `AllowAgentDefaultNetwork=true` (the sidecar does)

- for other workers: runtime sockets, system paths, relative paths, ports not bound to `127.0.0.1`, and host paths outside `AllowedHostPathRoots`

`/api/v1/agents/spawn` always hands the agent the configured `AgentBrokerUrl`; a request may repeat that value in `broker_url` but cannot replace it, and `max_iterations` is capped at 50. `/api/v1/workers/spawn` requires `worker_type`, no longer starts agents and no longer accepts an `environment` field. Values that must stay out of process listings are passed to the runtime as `-e NAME` with the value in the CLI's own environment; they still show up in the runtime's `inspect` output.

### Images

All images build from `main`: the .NET services use `mcr.microsoft.com/dotnet/sdk:10.0` and `aspnet:10.0` (the execution adapter keeps the SDK at run time for `git` and `dotnet build/test`), and the Node services use `node:22-bookworm-slim`. Every `FROM` is pinned to the multi-arch index digest, so base image fixes arrive only when you refresh the digests and rebuild:

```bash
node tools/agent/container/resolve-base-image-digests.mjs
node tools/agent/container/resolve-base-image-digests.mjs --engine podman
node tools/agent/container/resolve-base-image-digests.mjs --check
```

The first form queries docker (or `CONTAINER_ENGINE`) and rewrites the `FROM` lines; `--check` only reports and exits 1 when a digest is out of date. `npm run validate:container-images` checks every Containerfile without a container runtime: digest pinning, .NET 10 and Node 22 bases, no `adduser`, a non-root `USER` with a UID no other image uses, and no `COPY . .` or `VOLUME` in the final stage.

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

- `STACK_TOOL_ARGS_JSON`, `STACK_TOOL_SEQUENCE_JSON` (several tool calls in order) and `STACK_EXPECT_TOOL_RESULTS_JSON` (text each tool result must contain before the mock answers `TOOL_RESULT_VERIFIED`)

- `POOL_DISPATCH_TIMEOUT_SECONDS` (default `30`)

- `ADAPTER_WORKSPACE`

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

`AGENT_BROKER_URL` controls the HTTP broker URL injected into dynamically spawned agent containers through `/api/v1/agents/spawn`. In the compose stack the default is `http://broker:5000`; for a host-side broker, point it at the broker address reachable from the container, such as `http://host.containers.internal:5361` on Podman Desktop (`http://host.docker.internal:<port>` on Docker Desktop).

`/api/v1/agents/spawn` requires an administrator scoped token (`role_admin`). The broker issues an administrator-level session only when the registration arrives over the broker host's own loopback interface and the task assigns an administrator role. The agent containers in these stacks reach the broker over the compose network, and the compose broker has neither a runtime CLI nor a runtime socket, so the spawn flow is not available inside them; see [Dynamic Spawn](#dynamic-spawn) for a broker on the host.

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
