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

- Node.js 18+
- Ollama if you want fully local provider mode
- an API key for OpenAI-compatible mode
- a broker if you want governed mode

## Provider Aliases

| Provider | Notes |
| --- | --- |
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

## Legacy Direct LINE Listener

`--line-listen` still exists, but it is not the canonical production path.

Use it only for development experiments where you explicitly want the agent process to poll LINE directly. The repo's current production-style ingress path is the worker-side bridge under:

- [packages/csharp/workers/line-worker/README.md](/d:/Bricks4Agent/packages/csharp/workers/line-worker/README.md)

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
  -v %CD%:/workspace \
  -e BROKER_URL=http://host.containers.internal:5000 \
  -e BROKER_PUB_KEY=<base64> \
  -e BROKER_PRINCIPAL_ID=prn_xxx \
  -e BROKER_TASK_ID=task_xxx \
  -e BROKER_ROLE_ID=role_reader \
  -e AGENT_MODEL=llama3.1 \
  -e AGENT_RUN="Read README.md and summarize key points" \
  bricks4agent-agent:dev
```

The container entrypoint accepts only the governed path:

- `BROKER_URL`, `BROKER_PUB_KEY`, `BROKER_PRINCIPAL_ID`, and `BROKER_TASK_ID` are required
- direct provider API keys are not the intended formal execution path
- `/workspace` is the default mounted workspace
- the entrypoint adds `--governed` and broker/session parameters automatically
- `AGENT_RUN` executes one task; otherwise the container enters REPL

See also:

- [tools/agent/container/README.md](/d:/Bricks4Agent/tools/agent/container/README.md)

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
| --- | --- | --- |
| `--run "<prompt>"` | `-r` | One-shot run |
| `--model <name>` | `-m` | Model name |
| `--provider <type>` | `-P` | Local-provider mode provider |
| `--api-key <key>` | `-k` | Local-provider mode API key |
| `--host <url>` | `-H` | Override provider base URL |
| `--list-models` |  | List models |
| `--no-stream` |  | Disable streaming |
| `--force-react` |  | Force ReAct XML |
| `--force-native` |  | Force native tool-calling |
| `--max-iterations <n>` |  | Max iterations |
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
| --- | --- |
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
