# AI Agent CLI

`tools/agent` is the local agent runtime and generation entrypoint for Bricks4Agent.

It currently supports three distinct modes:

- local provider mode: the agent talks directly to an upstream LLM provider

- generation / pipeline mode: the agent drives CRUD and `project.json` generation helpers

- governed mode: the agent talks to a broker over JSON contracts and does not directly own execution authority

This README describes the CLI itself. It does not describe the canonical LINE ingress path. The production-style LINE route is now:

`LINE webhook -> ngrok public URL -> line-worker -> broker high-level coordinator`

`--line-listen` is kept only as a legacy development path.

## Requirements

- Node.js 22+ (the agent container image is pinned to `node:22-bookworm-slim`)

- Ollama if you want fully local provider mode

- an API key for OpenAI-compatible mode

- a broker if you want governed mode

## Provider Aliases

| Provider | Notes |
|---|---|
| `ollama` | Local Ollama, default host `http://localhost:11434` |
| `openai` | OpenAI / OpenAI-compatible Responses API |
| `gemini` | Gemini OpenAI-compatible endpoint |
| `deepseek` | DeepSeek OpenAI-compatible endpoint |
| `groq` | Groq OpenAI-compatible endpoint |
| `mistral` | Mistral OpenAI-compatible endpoint |

If `--provider` is omitted, the CLI prefers an API-key-backed provider when a key is present; otherwise it falls back to `ollama`.

## Quick Start

### Local Ollama

```bash
ollama serve
ollama pull llama3.1
node tools/agent/agent.js
```

### OpenAI-compatible / cloud mode

```bash
node tools/agent/agent.js --provider openai --api-key sk-xxx --model gpt-5.4-mini
node tools/agent/agent.js --provider gemini --api-key AIza... --model gemini-2.0-flash
```

### One-shot run

```bash
node tools/agent/agent.js --run "Read AGENT.md and summarize the constraints"
```

### List models

```bash
node tools/agent/agent.js --list-models
```

## Generation / Pipeline Mode

### Generate `project.json`

```bash
node tools/agent/agent.js --generate --project-path projects/PhotoDiary
node tools/agent/agent.js --generate --project-path projects/PhotoDiary --dry-run
node tools/agent/agent.js --generate --project-path projects/PhotoDiary --validate
```

### CRUD pipeline

```bash
node tools/agent/agent.js --pipeline crud --entity Product --fields "[{\"name\":\"Name\",\"type\":\"string\"},{\"name\":\"Price\",\"type\":\"decimal\"}]"
```

## Governed Mode

Governed mode means more than "tools go through the broker".

In governed mode:

- tool requests go through the broker

- LLM health / model listing / chat also go through the broker

- the agent should not treat direct provider API keys as the canonical execution path

- the broker decides whether a request is allowed by session, role, grant, capability, scope, and policy

### Important note about broker ports

The CLI code still defaults generic governed examples to `http://localhost:5000` when `BROKER_URL` is omitted. That is the generic broker default used by the agent runtime itself.

If you are targeting the current Windows LINE sidecar broker, use:

- `http://127.0.0.1:5361`

Do not assume the sidecar path and the generic agent default are the same thing.

### Start governed mode

Registering a session needs the task's registration secret. The agent reads it only from the `BROKER_REGISTRATION_SECRET` environment variable (there is no command-line option, because arguments show up in process listings), sends it inside the encrypted register handshake, and removes it from its own environment. Set it before any of the commands below.

Generic broker example:

```bash
node tools/agent/agent.js \
  --governed \
  --broker-url http://localhost:5000 \
  --broker-pub-key <base64> \
  --principal-id prn_xxx \
  --task-id task_xxx \
  --role-id role_reader \
  --run "Read README.md"
```

Current Windows sidecar broker example:

```bash
node tools/agent/agent.js \
  --governed \
  --broker-url http://127.0.0.1:5361 \
  --broker-pub-key <base64> \
  --principal-id prn_xxx \
  --task-id task_xxx \
  --role-id role_reader \
  --run "Inspect the repo"
```

Environment-variable form:

```powershell
$env:BROKER_URL='http://127.0.0.1:5361'
$env:BROKER_PUB_KEY='MFkwEwYH...'
$env:BROKER_PRINCIPAL_ID='prn_xxx'
$env:BROKER_TASK_ID='task_xxx'
$env:BROKER_ROLE_ID='role_reader'
$env:BROKER_REGISTRATION_SECRET='<the task registration secret>'
node tools/agent/agent.js --governed
```

### Broker contract

Governed agent requests are broker-mediated JSON POST calls such as:

- `POST /api/v1/sessions/register`

- `POST /api/v1/execution-requests/submit`

- `POST /api/v1/sessions/heartbeat`

- `POST /api/v1/sessions/close`

- `POST /api/v1/capabilities/list`

- `POST /api/v1/grants/list`

- `POST /api/v1/runtime/spec`

- `POST /api/v1/llm/health`

- `POST /api/v1/llm/models`

- `POST /api/v1/llm/chat`

Governed prompt context includes:

- current session information

- granted capabilities

- `scope.paths` / `scope.routes`

- broker URL and POST route contracts

- runtime spec such as default model, override policy, and tool-calling allowance

### Capability layer and scope layer

- capability layer: `capability_id`

- scope layer: `scope.routes`, `scope.paths`

This means the question is not only "may this agent read a file", but "may this capability act on these routes and paths".

### Governed-mode behavior

- `--provider`, `--api-key`, and `--host` are ignored in governed mode

- `--list-models` goes through broker `/api/v1/llm/models`

- `AgentLoop` uses the governed executor as its effective provider

- LLM traffic is broker-mediated rather than agent-direct

### Governed generation tools

When the session holds the generation grants, the agent has three tools that map to the generation capabilities (executed by `generation-worker`):

| Tool (route) | Capability | Use |
|---|---|---|
| `query_component_catalog` | `generation.catalog.query` | Read the catalog summary: `overview`, `field_types`, `example`, or one `component` |
| `validate_definition` | `generation.definition.validate` | Validate a DefinitionTemplate; `ok: false` comes with structured errors to fix |
| `generate_scaffold` | `generation.scaffold.generate` | Generate and package the prototype from the validated template; the broker decides the output location |

For such a task (task type `system_scaffold`, or a generate grant whose scope carries `output_slot`; an agent holding only the catalog or validate grant keeps the general prompt) the system prompt starts from a short generation base prompt (no component list and no project manual, since the manual's CLI examples and field type tables conflict with the catalog and the agent has no file tool) and adds the workflow (catalog, write, validate and fix, generate, report) and the limits taken from the grants: remaining quota per tool, the page limit and the iteration limit. It also explains that a validate result may be truncated (`truncated`, `total_errors`) and that a call failing with `No available worker` can be repeated with the same arguments. It carries no secret, host path or output location. The payload stays `{ route, args, project_root }`.

The run is unattended, so a generation task that replies without a tool call before a package was generated gets one reminder. Some models, small local ones in particular, write the call as JSON in the reply text in both native and ReAct mode; until a generate succeeds, a generation task runs such calls (a JSON code block, also one without its closing fence, bare JSON, or a `<tool_call>` or `<tools>` wrapper holding an object with a string `name` and an object `arguments` nested at most 64 levels deep) as tool calls. In a generation task a name the session was not granted, written as text or called as a tool, is not run: the model gets an "unsupported tool" result that says there is no such tool and no tool writes the definition, and that it should write the DefinitionTemplate itself following the catalog example, pass it to `validate_definition` and then call `generate_scaffold`. A turn whose calls all use such names counts as a turn without progress and gets the same single reminder; when the same name comes back in the next such turn, the run ends with a summary that says why, before the iteration limit. Other agents never run reply text as a tool call.

Before a governed tool request is sent, an argument declared as an object or array that arrives as a string (a model server passes the raw text on when the model's JSON is broken) is parsed and sent as the parsed value. For `validate_definition` and `generate_scaffold`, a string that only lacks closing brackets is repaired (only brackets outside strings count, and only closing brackets are added: where the next closer does not match, before a comma inside an object that is directly followed by `{` or `[`, or at the end) and the tool result carries an `agent_note` that says how many were added. A string that still does not parse is answered locally with `ARGUMENT_JSON_INVALID` (the parameter, the error position and about 60 characters around it) and is not sent to the broker. The broker's schema check, the validator and the full validation in generate remain the final check. A denied request comes back as `[Governed] request denied: <policy reason>`; when a tool's quota is used up the reason is `Grant quota exhausted.`. Printing tool arguments never throws, and `--run` closes the broker session also when the run fails, so the generation watchdog ends the task at once instead of at its deadline.

## Legacy Direct LINE Listener

`--line-listen` still exists, but it is not the canonical production path.

Use it only for development experiments where you explicitly want the agent process to poll LINE directly. The repo's current production-style ingress path is the worker-side bridge under:

- [packages/csharp/workers/line-worker/README.md](../../packages/csharp/workers/line-worker/README.md)

## Podman Container

The minimal governed agent container is defined in:

- `tools/agent/Containerfile`

### Build

```bash
podman build -f tools/agent/Containerfile -t bricks4agent-agent:dev .
```

### Run

```bash
podman run --rm -it \
  --read-only --tmpfs /tmp --cap-drop ALL \
  --security-opt no-new-privileges:true --pids-limit 256 \
  -e BROKER_URL=http://host.containers.internal:5000 \
  -e BROKER_PUB_KEY=<base64> \
  -e BROKER_PRINCIPAL_ID=prn_xxx \
  -e BROKER_TASK_ID=task_xxx \
  -e BROKER_ROLE_ID=role_reader \
  -e BROKER_REGISTRATION_SECRET \
  -e AGENT_MODEL=llama3.1 \
  -e AGENT_RUN="Read README.md and summarize key points" \
  bricks4agent-agent:dev
```

The container entrypoint accepts only the governed path:

- `BROKER_URL`, `BROKER_PUB_KEY`, `BROKER_PRINCIPAL_ID`, `BROKER_TASK_ID`, and `BROKER_REGISTRATION_SECRET` are required; the registration secret stays in the environment and is never added to the agent's arguments (`-e BROKER_REGISTRATION_SECRET` without a value passes it from your shell)

- direct provider API keys are not the intended formal execution path

- nothing is mounted into the agent: `/workspace` is an empty logical root baked into the image, every file read or write goes through broker capabilities (the file worker or the execution adapter), and the project manual comes from `/app/AGENT.md` (`AGENT_MANUAL_PATH`)

- the image runs as uid 10001 and needs no writable path besides the `/tmp` tmpfs

- the entrypoint adds `--governed` and broker/session parameters automatically

- `AGENT_RUN` executes one task; otherwise the container enters REPL

See also:

- [tools/agent/container/README.md](container/README.md)

## Verification

```bash
npm run validate:agent-governed
npm run validate:broker-scope
npm run validate:broker-llm-proxy
```

These checks cover:

- governed prompt route and JSON-contract injection

- governed initialization avoiding direct provider use

- broker-mediated `health/models/chat`

- live broker + fake upstream LLM session flow

- grant-filtered tool visibility

- local rejection of unauthorized capability usage

- broker validation of both capability and `scope.paths/scope.routes`

## Main Parameters

| Parameter | Short | Meaning |
|---|---|---|
| `--run "<prompt>"` | `-r` | One-shot run |
| `--model <name>` | `-m` | Model name |
| `--provider <type>` | `-P` | Local-provider mode provider |
| `--api-key <key>` | `-k` | Local-provider mode API key |
| `--host <url>` | `-H` | Override provider base URL |
| `--list-models` |  | List models |
| `--no-stream` |  | Disable streaming |
| `--force-react` |  | Force ReAct XML |
| `--force-native` |  | Force native tool-calling |
| `--max-iterations <n>` |  | Max iterations, 1 to 100 (default: `AGENT_MAX_ITERATIONS`, else 20) |
| `--generate` | `-g` | Run `project.json` generation |
| `--pipeline <type>` |  | Run a named pipeline |
| `--project-path <path>` |  | Target project path |
| `--dry-run` |  | Validate without writing |
| `--validate` |  | Validate only |
| `--force` |  | Overwrite generated output |
| `--governed` |  | Enable broker-governed mode |
| `--broker-url <url>` |  | Broker base URL |
| `--broker-pub-key <base64>` |  | Broker public key |
| `--principal-id <id>` |  | Principal id |
| `--task-id <id>` |  | Task id |
| `--role-id <id>` |  | Role id |
| `--line-listen` |  | Legacy development-only LINE listener |

## REPL Commands

| Command | Meaning |
|---|---|
| `/help` | Show help |
| `/model <name>` | Switch model |
| `/models` | List models |
| `/clear` | Clear history |
| `/history` | Show history |
| `/tools` | Show available tools |
| `/exit` | Exit |

## Layout

```text
tools/agent/
├── agent.js
├── README.md
├── lib/
│   ├── agent-loop.js
│   ├── repl.js
│   ├── state-machine.js
│   ├── tool-registry.js
│   ├── governed-executor.js
│   ├── broker-client.js
│   ├── providers/
│   │   ├── provider-factory.js
│   │   ├── ollama-provider.js
│   │   └── openai-provider.js
│   ├── pipelines/
│   ├── react-parser.js
│   ├── streaming.js
│   ├── responses-parser.js
│   ├── safety.js
│   └── utils.js
└── tests/
    └── test-governed-mode.js
```
