#!/usr/bin/env node
'use strict';

const assert = require('assert');
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..', '..', '..');

function read(relativePath) {
    return fs.readFileSync(path.join(ROOT, relativePath), 'utf8');
}

function assertIncludes(name, text, expected) {
    assert(
        text.includes(expected),
        `${name}: expected to include ${JSON.stringify(expected)}`
    );
}

function assertNotIncludes(name, text, unexpected) {
    assert(
        !text.includes(unexpected),
        `${name}: expected not to include ${JSON.stringify(unexpected)}`
    );
}

const compose = read('tools/agent/container/compose.yml');
assertIncludes('compose agent worker image', compose, 'FunctionPool__ContainerManager__WorkerImages__agent__Image: "bricks4agent-agent:latest"');
assertIncludes('compose agent worker memory', compose, 'FunctionPool__ContainerManager__WorkerImages__agent__MemoryLimit: "512m"');
assertIncludes('compose agent worker network override', compose, 'FunctionPool__ContainerManager__WorkerImages__agent__NetworkName: "bricks4agent_control-net"');
assertIncludes('compose agent broker url', compose, 'FunctionPool__ContainerManager__AgentBrokerUrl: "${AGENT_BROKER_URL:-http://broker:5000}"');
assertIncludes('compose disables embeddings for smoke stack', compose, 'Embedding__Enabled: "false"');
assertIncludes('compose disables rag seed for smoke stack', compose, 'RagSeed__Enabled: "false"');
assertIncludes('compose mock tool call can exercise broker dispatch', compose, 'MOCK_TOOL_CALL: "${STACK_TOOL_CALL:-}"');
assertIncludes('compose uses container access root', compose, 'HighLevelCoordinator__AccessRoot: "/data/workspaces"');
assertIncludes('compose line worker broker api uses service name', compose, 'WORKER_Broker__ApiUrl: "http://broker:5000"');
assertIncludes('compose line worker has auth key', compose, 'WORKER_Worker__Auth__KeyId: "${LINE_WORKER_AUTH_KEY_ID:-line-worker-dev-key}"');
assertIncludes('compose mock ollama image tag', compose, 'image: bricks4agent-mock-ollama:latest');
assertIncludes('compose broker image tag', compose, 'image: bricks4agent-broker:latest');
assertIncludes('compose agent image tag', compose, 'image: bricks4agent-agent:latest');
assertIncludes('compose file worker image tag', compose, 'image: bricks4agent-file-worker:latest');
assertIncludes('compose line worker image tag', compose, 'image: bricks4agent-line-worker:latest');
assertIncludes('compose control network name', compose, 'name: bricks4agent_control-net');
assertIncludes('compose worker network name', compose, 'name: bricks4agent_worker-net');

const dockerignore = read('.dockerignore');
assertIncludes('container build ignores node modules', dockerignore, 'node_modules');
assertIncludes('container build ignores dotnet artifacts', dockerignore, '**/bin');
assertIncludes('container build ignores test harnesses', dockerignore, 'tools/agent/tests');
assertIncludes('container build ignores compose files', dockerignore, 'tools/agent/container/compose*.yml');
assertIncludes('container build ignores git metadata', dockerignore, '.git');

const agentContainerfile = read('tools/agent/Containerfile');
assertIncludes('agent image workspace directory', agentContainerfile, 'mkdir -p /workspace');
assertIncludes('agent image workspace ownership', agentContainerfile, 'chown -R agent:agent /app /workspace');

const agentSystemPrompt = read('tools/agent/lib/system-prompt.js');
assertIncludes('agent prompt prioritizes custom components', agentSystemPrompt, 'use the custom component library first');
assertIncludes('agent prompt names ui component runtime', agentSystemPrompt, './runtime/ui_components/index.js');
assertIncludes('agent prompt names key components', agentSystemPrompt, 'BasicButton, ButtonGroup, FeatureCard');

const agentEndpoints = read('packages/csharp/broker/Endpoints/AgentEndpoints.cs');
assertIncludes('spawn defaults to non-legacy mode', agentEndpoints, '["AGENT_LINE_LISTEN"] = "0"');
assertIncludes('spawn has legacy opt-in flag', agentEndpoints, '["AGENT_ENABLE_LEGACY_LINE_LISTEN"] = "1"');
assertIncludes('spawn one-shot run fallback', agentEndpoints, 'Reply with the exact text AGENT_READY.');
assertIncludes('spawn normalizes requested agent id', agentEndpoints, 'agentId = AgentSpawnService.NormalizeAgentId(agentId);');
assertIncludes('spawn resolves configurable agent broker url', agentEndpoints, 'ResolveAgentBrokerUrl(body, configuration)');
assertIncludes('spawn uses resolved broker url', agentEndpoints, '["BROKER_URL"] = agentBrokerUrl');
assertIncludes('spawn uses high-level model by default', agentEndpoints, '["AGENT_MODEL"] = highLevelLlmOptions.DefaultModel');
assertNotIncludes('spawn does not pass direct provider', agentEndpoints, 'envOverrides["AGENT_PROVIDER"]');
assertNotIncludes('spawn does not pass direct api key', agentEndpoints, 'envOverrides["OPENAI_API_KEY"]');
assertNotIncludes('create lets task type choose default caps', agentEndpoints, 'capabilityIds = spawnService.GetDefaultCapabilities();');
assertNotIncludes('stop only matches requested agent container', agentEndpoints, 'c.WorkerId == agentId || c.WorkerType == "agent"');
assertIncludes('stop requires agent worker type and id', agentEndpoints, 'c.WorkerType == "agent" && c.WorkerId == agentId');

const containerConfig = read('packages/csharp/function-pool/Container/ContainerConfig.cs');
assertIncludes('per-image network config exists', containerConfig, 'public string? NetworkName { get; set; }');
assertIncludes('agent broker url config exists', containerConfig, 'public string AgentBrokerUrl { get; set; } = "http://broker:5000";');

const program = read('packages/csharp/broker/Program.cs');
assertIncludes('program loads per-image network config', program, 'NetworkName = child.GetValue<string>("NetworkName")');
assertIncludes('program loads agent broker url config', program, 'AgentBrokerUrl = builder.Configuration.GetValue("FunctionPool:ContainerManager:AgentBrokerUrl"');

const podmanStackTest = read('tools/agent/tests/test-podman-governed-stack.js');
assertIncludes('podman stack prebuilds images', podmanStackTest, 'await buildImages(env);');
assertIncludes('podman stack validates broker tool dispatch', podmanStackTest, "STACK_TOOL_CALL: 'read_file'");
assertIncludes('podman stack forces utf8 compose output', podmanStackTest, "PYTHONIOENCODING: 'utf-8'");
assertNotIncludes('podman stack avoids compose build flag', podmanStackTest, "'--build'");

const containerManager = read('packages/csharp/function-pool/Container/ContainerManager.cs');
assertNotIncludes('container manager no StringBuilder', containerManager, 'new StringBuilder');
assertNotIncludes('container manager no raw Arguments assignment', containerManager, 'Arguments = arguments');
assertIncludes('container manager uses argument list', containerManager, 'process.StartInfo.ArgumentList.Add(argument)');
assertIncludes('container manager exposes testable run args', containerManager, 'BuildRunArguments');
assertIncludes('container manager uses per-image network override', containerManager, 'imageConfig.NetworkName');
assertIncludes('container manager keeps env values atomic', containerManager, 'args.Add($"{key}={value ?? string.Empty}")');

const spawnService = read('packages/csharp/broker-core/Services/AgentSpawnService.cs');
assertIncludes('agent id normalization exists', spawnService, 'public static string NormalizeAgentId');
assertIncludes('agent runtime descriptor carries default model', spawnService, 'default_model = request.LlmDefaultModel');
assertIncludes('agent runtime descriptor carries tool setting', spawnService, 'supports_tool_calling = request.LlmSupportsToolCalling');
assertIncludes('custom agent id gains canonical prefix', spawnService, 'raw = "agent_" + raw');
assertIncludes('symbol-only agent id gets safe fallback', spawnService, 'string.Equals(normalized, "agent", StringComparison.OrdinalIgnoreCase)');
assertIncludes('post-sanitize agent id keeps canonical prefix', spawnService, 'normalized = "agent_" + normalized');
assertIncludes('list agents follows task/principal pair', spawnService, 'string.Equals(t.AssignedPrincipalId, $"prn_{t.TaskId[5..]}", StringComparison.Ordinal)');
assertIncludes('deactivate normalizes requested agent id', spawnService, 'agentId = NormalizeAgentId(agentId);');

const codeArtifactService = read('packages/csharp/broker/Services/HighLevelCodeArtifactService.cs');
assertIncludes('code prompt prioritizes custom components', codeArtifactService, '任何網頁程式都必須優先使用專案自訂元件庫');
assertIncludes('code generator copies custom component runtime', codeArtifactService, 'CopyCustomComponentRuntimeIfAvailable');
assertIncludes('tic tac toe uses component runtime import', codeArtifactService, "import('./runtime/ui_components/index.js')");

console.log('Agent container config validation passed.');
