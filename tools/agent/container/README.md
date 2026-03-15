# Governed Agent Podman Stack

This directory contains a self-contained development stack for the governed agent flow:

- `mock-ollama`: deterministic upstream LLM stub
- `broker`: control plane, scoped-session issuer, capability/scope policy, and LLM reverse proxy
- `agent`: governed agent container that can only talk to the broker

## What This Proves

The stack is intentionally narrow. It verifies that:

- the agent container does not need a direct provider API key
- runtime spec and model traffic are fetched through the broker
- the broker forwards LLM traffic to an upstream service
- the agent can only operate with the broker-issued session, role, capability, and scope

## Build And Run

From the repo root:

```bash
podman compose -f tools/agent/container/compose.yml up --build --abort-on-container-exit --exit-code-from agent
```

The default stack uses the bundled mock upstream and should end with the agent printing `STACK_OK`.

To stop and remove the stack:

```bash
podman compose -f tools/agent/container/compose.yml down -v
```

## Default Development Identity

The compose stack seeds a development principal and task into the broker:

- `principal_id`: `prn_podman_dev`
- `task_id`: `task_podman_dev`
- `role_id`: `role_reader`

The ECDH keypair, broker token secret, and master key embedded in the compose file are development-only values.
Do not reuse them outside local testing.

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
- `AGENT_RUN`
- `AGENT_VERBOSE`

## Switching To A Real Upstream

The broker is already wired to use the upstream through `LlmProxy__BaseUrl`.
For a real provider, replace the `mock-ollama` service and point `LlmProxy__BaseUrl` to the actual upstream.

The governed agent container does not change. It still only knows about:

- `BROKER_URL`
- `BROKER_PUB_KEY`
- `BROKER_PRINCIPAL_ID`
- `BROKER_TASK_ID`
- `BROKER_ROLE_ID`
