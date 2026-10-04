#!/usr/bin/env node
'use strict';

const assert = require('assert');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');

const ROOT = path.resolve(__dirname, '..', '..', '..');

// compose 檔曾以 ${VAR:-預設值} 提交的開發金鑰，已在 git 歷史中公開。
// 只保存 SHA-256（十六進位），不在 repo 中寫出原值。
// 與 packages/csharp/broker/Configuration/BrokerSecretsValidator.cs 的
// LeakedSecretFingerprints.Default（ValueSha256）必須一致，下方會比對。
const LEAKED_SECRET_SHA256 = [
    'd4735fc78bf0409eb9a424099eca948453529e39e8b541eaedf70fa341aa81fe', // broker ScopedToken secret
    '5bfc312a1c45de453aa9fa235af0650488a52faf21ea66dd231af31b0269a2f1', // broker MasterKeyBase64
    '7a5dd0ecd40874c4f914c0a49a5edbfde8136989d72e84a113025313f517f932', // broker ECDH private key
    '3662bfe012750afdccb756357ecbf04e49f047ab409f214d3ec5cfeb52472c0f', // line-worker shared secret
    '56d92e694fa1eec508fd8ef41ebe01aacc980e2140575797d3900b7763635283', // file-worker shared secret
    'bbac8d268d5976b6dc3bdfd123fc86e0d1ee539210efd507c2aabead1c5c74f9', // execution-adapter shared secret
];
// 與上面 ECDH 私鑰成對的 broker 公鑰：本身不是密鑰，但出現代表 stack 仍在用那把私鑰。
const LEAKED_PAIRED_PUBLIC_KEY_SHA256 = '7caae6b87a07c8f0b8edd969b473391671f1d1944d1619778818cf7397f9e3b8';

const COMPOSE_FILES = [
    'tools/agent/container/compose.yml',
    'tools/agent/container/compose.openai-compatible.yml',
    'tools/agent/container/compose.ollama-host.yml',
];
const BROKER_SECRET_VARIABLES = [
    'BROKER_SCOPED_TOKEN_SECRET',
    'BROKER_MASTER_KEY_BASE64',
    'BROKER_ECDH_PRIVATE_KEY_BASE64',
    'BROKER_ECDH_PUBLIC_KEY_BASE64',
    'BROKER_REGISTRATION_SECRET',
];
const WORKER_SECRET_VARIABLES = [
    'LINE_WORKER_AUTH_KEY_ID',
    'LINE_WORKER_AUTH_SHARED_SECRET',
    'FILE_WORKER_AUTH_KEY_ID',
    'FILE_WORKER_AUTH_SHARED_SECRET',
    'EXEC_ADAPTER_AUTH_KEY_ID',
    'EXEC_ADAPTER_AUTH_SHARED_SECRET',
];

function sha256Hex(value) {
    return crypto.createHash('sha256').update(value, 'utf8').digest('hex');
}

// 把文字切成類 base64／識別字的片段，逐一比對雜湊；回傳命中的雜湊前綴（不回傳原值）。
function findLeakedFragments(text) {
    const leaked = new Set([...LEAKED_SECRET_SHA256, LEAKED_PAIRED_PUBLIC_KEY_SHA256]);
    const hits = new Set();
    for (const match of text.matchAll(/[A-Za-z0-9+/=_-]{12,}/g)) {
        const token = match[0];
        const candidates = new Set([token, token.replace(/^-+/, '')]);
        for (let index = token.indexOf('='); index !== -1; index = token.indexOf('=', index + 1)) {
            candidates.add(token.slice(index + 1));
        }
        for (const candidate of candidates) {
            const hash = sha256Hex(candidate);
            if (leaked.has(hash)) {
                hits.add(hash.slice(0, 12));
            }
        }
    }
    return [...hits];
}

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
assertIncludes('compose agent worker network override', compose, 'FunctionPool__ContainerManager__WorkerImages__agent__NetworkName: "bricks4agent_agent-net"');
assertIncludes('compose agent broker url', compose, 'FunctionPool__ContainerManager__AgentBrokerUrl: "${AGENT_BROKER_URL:-http://broker:5000}"');
assertIncludes('compose disables embeddings for smoke stack', compose, 'Embedding__Enabled: "false"');
assertIncludes('compose disables rag seed for smoke stack', compose, 'RagSeed__Enabled: "false"');
assertIncludes('compose mock tool call can exercise broker dispatch', compose, 'MOCK_TOOL_CALL: "${STACK_TOOL_CALL:-}"');
assertIncludes('compose uses container access root', compose, 'HighLevelCoordinator__AccessRoot: "/data/workspaces"');
assertIncludes('compose line worker broker api uses service name', compose, 'WORKER_Broker__ApiUrl: "http://broker:5000"');
assertIncludes('compose line worker has auth key', compose, 'WORKER_Worker__Auth__KeyId: "${LINE_WORKER_AUTH_KEY_ID:?');
assertIncludes('compose mock ollama image tag', compose, 'image: bricks4agent-mock-ollama:latest');
assertIncludes('compose broker image tag', compose, 'image: bricks4agent-broker:latest');
assertIncludes('compose agent image tag', compose, 'image: bricks4agent-agent:latest');
assertIncludes('compose file worker image tag', compose, 'image: bricks4agent-file-worker:latest');
assertIncludes('compose line worker image tag', compose, 'image: bricks4agent-line-worker:latest');
assertIncludes('compose control network name', compose, 'name: bricks4agent_control-net');
assertIncludes('compose worker network name', compose, 'name: bricks4agent_worker-net');

// ── compose 密鑰：不附預設值、不含已外洩值、worker 驗證預設開啟、對外埠只綁 loopback ──
const expectedLoopbackPorts = {
    'tools/agent/container/compose.yml': 3,
    'tools/agent/container/compose.openai-compatible.yml': 2,
    'tools/agent/container/compose.ollama-host.yml': 1,
};
for (const composePath of COMPOSE_FILES) {
    const text = read(composePath);
    const requiredVariables = composePath.endsWith('/compose.yml')
        ? [...BROKER_SECRET_VARIABLES, ...WORKER_SECRET_VARIABLES]
        : BROKER_SECRET_VARIABLES;

    for (const variable of requiredVariables) {
        const references = [...text.matchAll(new RegExp(`[$][{]${variable}([^A-Z0-9_])`, 'g'))];
        assert(references.length > 0, `${composePath}: expected a reference to ${variable}`);
        for (const reference of references) {
            assert.strictEqual(
                reference[1] + text.charAt(reference.index + reference[0].length),
                ':?',
                `${composePath}: ${variable} must use the required form \${${variable}:?...} with no default`
            );
        }
    }

    const requiredMessages = [...text.matchAll(/[$][{]([A-Z0-9_]+):[?]([^}]*)[}]/g)];
    assert(requiredMessages.length >= requiredVariables.length, `${composePath}: expected \${VAR:?...} entries`);
    for (const [, variable, message] of requiredMessages) {
        assert(
            message.includes('gen-stack-secrets.mjs'),
            `${composePath}: the error message for ${variable} should point at gen-stack-secrets.mjs`
        );
    }

    assert.deepStrictEqual(findLeakedFragments(text), [], `${composePath}: contains a previously published development key`);
    assertNotIncludes(`${composePath} keeps the broker out of Development`, text, 'ASPNETCORE_ENVIRONMENT');

    const publishedPorts = [...text.matchAll(/^\s*ports:\s*\r?\n((?:\s*-\s*.*\r?\n?)+)/gm)]
        .flatMap((block) => block[1].split(/\r?\n/).map((line) => line.trim()).filter((line) => line.startsWith('-')));
    assert.strictEqual(
        publishedPorts.length,
        expectedLoopbackPorts[composePath],
        `${composePath}: unexpected number of published ports`
    );
    for (const port of publishedPorts) {
        assert(port.startsWith('- "127.0.0.1:'), `${composePath}: published port must bind 127.0.0.1 (${port})`);
    }
}
assertIncludes('compose enforces worker auth by default', compose, 'WorkerAuth__Enforce: "${WORKER_AUTH_ENFORCE:-true}"');
assertIncludes('compose broker trusts execution adapter credential', compose, 'WorkerAuth__Credentials__2__WorkerType: "execution-adapter-worker"');
assertIncludes('compose broker execution adapter key id', compose, 'WorkerAuth__Credentials__2__KeyId: "${EXEC_ADAPTER_AUTH_KEY_ID:?');
assertIncludes('compose broker execution adapter secret', compose, 'WorkerAuth__Credentials__2__SharedSecret: "${EXEC_ADAPTER_AUTH_SHARED_SECRET:?');
assertIncludes('compose execution adapter worker uses the same key id', compose, 'WORKER_Worker__Auth__KeyId: "${EXEC_ADAPTER_AUTH_KEY_ID:?');

const brokerSecretsValidator = read('packages/csharp/broker/Configuration/BrokerSecretsValidator.cs');
for (const hash of LEAKED_SECRET_SHA256) {
    assertIncludes('broker validator rejects every published compose secret', brokerSecretsValidator, `"${hash}"`);
}
assertNotIncludes('broker validator keeps only fingerprints', brokerSecretsValidator, 'BEGIN PRIVATE KEY');
assertIncludes('broker startup calls the secrets validator', read('packages/csharp/broker/Program.cs'), 'BrokerSecretsValidator.Validate(');

const envExample = read('tools/agent/container/agent-stack.env.example');
for (const variable of [...BROKER_SECRET_VARIABLES, ...WORKER_SECRET_VARIABLES]) {
    assert(
        new RegExp(`^${variable}=$`, 'm').test(envExample),
        `agent-stack.env.example: expected an empty ${variable}= line`
    );
}
for (const line of envExample.split(/\r?\n/)) {
    if (line.trim() === '' || line.trimStart().startsWith('#')) {
        continue;
    }
    assert(/^[A-Z0-9_]+=$/.test(line), `agent-stack.env.example: values must stay empty (${line.split('=')[0]})`);
}

const podmanStackTests = [
    'tools/agent/tests/test-podman-governed-stack.js',
    'tools/agent/tests/test-podman-execution-adapter-stack.js',
    'tools/agent/tests/test-podman-openai-compatible-stack.js',
    'tools/agent/tests/test-podman-ollama-host-stack.js',
];
for (const testPath of podmanStackTests) {
    const testSource = read(testPath);
    assertIncludes(`${testPath} imports the secrets generator`, testSource, "'gen-stack-secrets.mjs'");
    assertIncludes(`${testPath} passes generated secrets through env`, testSource, '...(await generateStackSecretsEnv()),');
    assertNotIncludes(`${testPath} does not write an env file`, testSource, '--env-file');
}

const sidecarScript = read('packages/csharp/workers/line-worker/start-sidecar-stack.ps1');
assertIncludes('sidecar opts in to ephemeral broker keys explicitly', sidecarScript, 'AllowEphemeralKeys = $true');

const dockerignore = read('.dockerignore');
assertIncludes('container build ignores node modules', dockerignore, 'node_modules');
assertIncludes('container build ignores dotnet artifacts', dockerignore, '**/bin');
assertIncludes('container build ignores test harnesses', dockerignore, 'tools/agent/tests');
assertIncludes('container build ignores compose files', dockerignore, 'tools/agent/container/compose*.yml');
assertIncludes('container build ignores git metadata', dockerignore, '.git');

const agentContainerfile = read('tools/agent/Containerfile');
assertIncludes('agent image workspace directory', agentContainerfile, 'mkdir -p /workspace');
assertIncludes('agent image bakes in the project manual', agentContainerfile, 'COPY AGENT.md /app/AGENT.md');
assertIncludes('agent image points the prompt at the baked manual', agentContainerfile, 'ENV AGENT_MANUAL_PATH=/app/AGENT.md');
assert(!/^\s*(?:RUN|&&).*\bchown\b.*\/app\b/m.test(agentContainerfile), 'agent image: /app must stay owned by root (no chown of /app)');
assertNotIncludes('agent image does not chown the code', agentContainerfile, 'chown -R agent:agent /app');
const entrypoint = read('tools/agent/container/entrypoint.sh');
assertIncludes('agent image keeps the default project root', entrypoint, 'WORKSPACE_DIR="${AGENT_PROJECT_ROOT:-/workspace}"');
// 註冊密鑰：entrypoint 要求它存在，但只留在環境變數，不放進 node 的參數（argv 會出現在程序清單）。
assertIncludes('entrypoint requires the registration secret', entrypoint, 'require_env BROKER_REGISTRATION_SECRET');
const entrypointArgvStart = entrypoint.indexOf('set -- ');
assert(entrypointArgvStart > entrypoint.indexOf('require_env BROKER_REGISTRATION_SECRET'), 'entrypoint: set -- block not found after the env checks');
const entrypointArgv = entrypoint.slice(entrypointArgvStart, entrypoint.lastIndexOf('exec "$@"'));
assertNotIncludes('entrypoint keeps the registration secret out of argv', entrypointArgv, 'REGISTRATION_SECRET');
const agentCli = read('tools/agent/agent.js');
assertIncludes('agent reads the registration secret from the environment', agentCli, 'process.env.BROKER_REGISTRATION_SECRET');
assertIncludes('agent drops the registration secret from its environment', agentCli, 'delete process.env.BROKER_REGISTRATION_SECRET');
assert(!/--registration-secret/.test(agentCli), 'agent.js: the registration secret must not have a command-line option');
const governedExecutorSource = read('tools/agent/lib/governed-executor.js');
assertIncludes('prompt context shows only a placeholder for the registration secret', governedExecutorSource, "registration_secret: '<registration secret>'");

// ── compose 每個服務的 §13.2 加固（依縮排切出服務區塊逐一檢查） ──
function composeServices(text) {
    const lines = text.split(/\r?\n/);
    const start = lines.findIndex((line) => line === 'services:');
    assert(start >= 0, 'compose file without a services: section');
    const services = {};
    let current = null;
    for (const line of lines.slice(start + 1)) {
        if (/^\S/.test(line)) {
            break; // next top-level key (networks:, volumes:)
        }
        const header = /^ {2}([A-Za-z0-9_-]+):\s*$/.exec(line);
        if (header) {
            current = header[1];
            services[current] = [];
        } else if (current) {
            services[current].push(line);
        }
    }
    return Object.fromEntries(Object.entries(services).map(([name, body]) => [name, body.join('\n')]));
}

function serviceListValues(block, key) {
    const match = new RegExp(`^ {4}${key}:\\s*\\r?\\n((?: {6}- .*\\r?\\n?)+)`, 'm').exec(block);
    return match ? match[1].split(/\r?\n/).map((line) => line.trim().replace(/^- /, '').replace(/\s+#.*$/, '')).filter(Boolean) : [];
}

const expectedServices = {
    'tools/agent/container/compose.yml': ['mock-ollama', 'broker', 'file-worker', 'line-worker', 'execution-adapter-worker', 'agent'],
    'tools/agent/container/compose.openai-compatible.yml': ['mock-openai', 'broker', 'agent'],
    'tools/agent/container/compose.ollama-host.yml': ['broker', 'agent'],
};
for (const composePath of COMPOSE_FILES) {
    const text = read(composePath);
    assertNotIncludes(`${composePath} mounts no runtime socket`, text, 'docker.sock');
    assertNotIncludes(`${composePath} mounts no podman socket`, text, 'podman.sock');
    assert(!/^\s*privileged:/m.test(text), `${composePath}: no service may be privileged`);
    assert(!/^\s*network_mode:/m.test(text), `${composePath}: no service may share the host network`);

    const services = composeServices(text);
    assert.deepStrictEqual(Object.keys(services).sort(), [...expectedServices[composePath]].sort(), `${composePath}: unexpected service list`);
    for (const [name, block] of Object.entries(services)) {
        const where = `${composePath} ${name}`;
        assert(/^ {4}read_only: true\b/m.test(block), `${where}: expected read_only: true`);
        assert(serviceListValues(block, 'tmpfs').includes('/tmp'), `${where}: expected a /tmp tmpfs`);
        assert.deepStrictEqual(serviceListValues(block, 'cap_drop'), ['ALL'], `${where}: expected cap_drop: [ALL]`);
        assert(serviceListValues(block, 'security_opt').includes('no-new-privileges:true'), `${where}: expected no-new-privileges:true`);
        const pids = /^ {4}pids_limit: (\d+)\b/m.exec(block);
        assert(pids, `${where}: expected pids_limit`);
        assert.strictEqual(Number(pids[1]), name === 'broker' ? 1024 : 256, `${where}: unexpected pids_limit`);
        assert(!/^ {4}user:\s*["']?(?:0|root)\b/m.test(block), `${where}: must not run as root`);
    }

    const agent = services.agent;
    // 註冊密鑰：broker 的種子與 agent 取用同一個必填變數（${VAR:?...}，訊息指向產生器，見上方檢查）。
    assertIncludes(`${composePath} broker seeds the registration secret`, services.broker,
        'DevelopmentSeed__RegistrationSecret: "${BROKER_REGISTRATION_SECRET:?');
    assertIncludes(`${composePath} agent receives the registration secret`, agent,
        'BROKER_REGISTRATION_SECRET: "${BROKER_REGISTRATION_SECRET:?');
    assert(!/^ {4}volumes:/m.test(agent), `${composePath} agent: the agent must not mount anything`);
    assert(!/:\/workspace\b/.test(agent), `${composePath} agent: no :/workspace bind mount`);
    assertIncludes(`${composePath} agent keeps the logical project root`, agent, 'AGENT_PROJECT_ROOT: "/workspace"');
    assertIncludes(`${composePath} agent stays on the internal network`, agent, '- agent-net');
    assertNotIncludes(`${composePath} drops the stale file.search seed`, text, '"capability_id":"file.search"');
}

const composeServicesMain = composeServices(compose);
assertIncludes('compose line worker writes audio to tmpfs', composeServicesMain['line-worker'], 'WORKER_Line__AudioTempPath: "/tmp/audio_temp"');
assertIncludes('compose file worker reads the repository read-only', composeServicesMain['file-worker'], '- ../../..:/workspace:ro');
assertIncludes('compose broker keeps its data volume', composeServicesMain.broker, '- broker-data:/data');
assertIncludes('compose seeds file.search_name', compose, '"capability_id":"file.search_name","scope":{"paths":["/workspace"],"routes":["search_files"]}');
assertIncludes('compose seeds file.search_content', compose, '"capability_id":"file.search_content","scope":{"paths":["/workspace"],"routes":["search_content"]}');
assertIncludes('compose keeps container manager disabled by default', compose, 'FunctionPool__ContainerManager__Enabled: "${CONTAINER_MANAGER_ENABLED:-false}"');
assertIncludes('compose keeps the agent block marker', compose, '# ── Agent ──');

// sidecar：動態啟動的代理不掛任何主機目錄；預設網路只能明確 opt-in。
const sidecarContainerBlock = sidecarScript.slice(
    sidecarScript.indexOf('ContainerManager = @{'),
    sidecarScript.indexOf('$brokerWorkerAuthCredentials = @()')
);
assert(sidecarContainerBlock.length > 0, 'sidecar: ContainerManager block not found');
assert(!/^\s*Volumes\s*=/m.test(sidecarContainerBlock), 'sidecar: agent image must not configure Volumes');
assertNotIncludes('sidecar does not mount managed workspaces into agents', sidecarContainerBlock, '$managedWorkspaceRoot');
assertIncludes('sidecar opts in to the default network explicitly', sidecarContainerBlock, 'AllowAgentDefaultNetwork = $true');

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
assertIncludes('podman stack validates broker tool dispatch', podmanStackTest, "{ name: 'read_file', args: { path: 'README.html' } }");
assertIncludes('podman stack drives a tool sequence', podmanStackTest, 'STACK_TOOL_SEQUENCE_JSON: JSON.stringify(TOOL_SEQUENCE)');
assertIncludes('podman stack checks every tool result', podmanStackTest, 'STACK_EXPECT_TOOL_RESULTS_JSON: JSON.stringify(TOOL_EXPECTATIONS)');
assertIncludes('podman stack searches with the agent tool arguments', podmanStackTest, "name: 'search_content'");
assertIncludes('podman stack checks that a worker refusal is final', podmanStackTest, 'Worker refused search_files');
assertIncludes('podman stack forces utf8 compose output', podmanStackTest, "PYTHONIOENCODING: 'utf-8'");
assertNotIncludes('podman stack avoids compose build flag', podmanStackTest, "'--build'");

const ollamaHostCompose = read('tools/agent/container/compose.ollama-host.yml');
assertIncludes('ollama host compose defaults to qwen3.6', ollamaHostCompose, 'qwen3.6:latest');
assertNotIncludes('ollama host compose does not default qwen3-coder', ollamaHostCompose, 'qwen3-coder:30b');

const ollamaHostStackTest = read('tools/agent/tests/test-podman-ollama-host-stack.js');
assertIncludes('ollama host auto-selection prefers qwen3.6', ollamaHostStackTest, "const preferredModels = ['qwen3.6:latest', 'qwen3.6'];");

const sidecarStartScript = read('packages/csharp/workers/line-worker/start-sidecar-stack.ps1');
assertIncludes('sidecar can read Anthropic API key', sidecarStartScript, 'ANTHROPIC_API_KEY');
assertIncludes('sidecar can select Anthropic high-level provider', sidecarStartScript, 'Provider = "anthropic"');
assertIncludes('sidecar defaults Anthropic model to Claude Sonnet 4.6', sidecarStartScript, 'claude-sonnet-4-6');

const packageJson = read('package.json');
assertIncludes('package exposes Anthropic provider smoke validation', packageJson, '"validate:anthropic-provider-smoke"');
assertIncludes('package Anthropic smoke points at provider smoke script', packageJson, 'tools/agent/tests/test-anthropic-provider-smoke.js');

const containerManager = read('packages/csharp/function-pool/Container/ContainerManager.cs');
assertNotIncludes('container manager no StringBuilder', containerManager, 'new StringBuilder');
assertNotIncludes('container manager no raw Arguments assignment', containerManager, 'Arguments = arguments');
assertIncludes('container manager uses argument list', containerManager, 'process.StartInfo.ArgumentList.Add(argument)');
assertIncludes('container manager exposes testable run args', containerManager, 'BuildRunArguments');
assertIncludes('container manager uses per-image network override', containerManager, 'imageConfig.NetworkName');
assertIncludes('container manager keeps env values atomic', containerManager, 'args.Add($"{key}={value ?? string.Empty}")');
for (const flag of ['"--read-only"', '"--cap-drop", "ALL"', '"--security-opt", "no-new-privileges:true"', '"--pids-limit"', '"--tmpfs", TmpfsMount']) {
    assertIncludes(`container manager always adds ${flag}`, containerManager, flag);
}
assertIncludes('container manager tmpfs is noexec', containerManager, '"/tmp:rw,noexec,nosuid,nodev,size=64m"');
assertIncludes('container manager removes anonymous volumes', containerManager, '"rm", "-f", "-v"');
assertIncludes('container manager passes secrets by name only', containerManager, 'process.StartInfo.Environment[name] = value');
assertIncludes('container manager refuses agent mounts', containerManager, 'Agent containers must not mount volumes');

const containerManagerInterface = read('packages/csharp/function-pool/Container/IContainerManager.cs');
assertIncludes('spawn takes a request object', containerManagerInterface, 'Task<string> SpawnWorkerAsync(ContainerSpawnRequest request, CancellationToken ct = default);');
const spawnRequest = read('packages/csharp/function-pool/Container/ContainerSpawnRequest.cs');
assertIncludes('spawn request has trusted environment', spawnRequest, 'public IReadOnlyDictionary<string, string> TrustedEnvironment');
assertIncludes('spawn request has secret environment', spawnRequest, 'public IReadOnlyDictionary<string, string> SecretEnvironment');

const workerEndpoints = read('packages/csharp/broker/Endpoints/WorkerEndpoints.cs');
assertIncludes('workers/spawn refuses agents', workerEndpoints, 'ContainerManager.IsAgentWorkerType(workerType)');
assertNotIncludes('workers/spawn no longer copies a request environment', workerEndpoints, 'envOverrides[prop.Name]');
assertIncludes('workers/spawn reports a missing runtime CLI', workerEndpoints, 'catch (Win32Exception)');
assertIncludes('agents/spawn caps max_iterations', agentEndpoints, 'ClampMaxIterations(maxIterations)');
assertIncludes('agents/spawn only accepts the configured broker url', agentEndpoints, 'Agent broker_url must match the configured AgentBrokerUrl.');
// 註冊憑證只經 SecretEnvironment 交給容器（參數中只有 -e NAME），不放進 TrustedEnvironment。
assertIncludes('agents/spawn issues a registration credential', agentEndpoints, 'spawnService.IssueSpawnCredential(');
assertIncludes('agents/spawn hands the secret over as a secret environment', agentEndpoints, '[RegistrationSecretEnvironmentVariable] = credential.Secret');
assertIncludes('agents/spawn names the container variable', agentEndpoints, 'RegistrationSecretEnvironmentVariable = "BROKER_REGISTRATION_SECRET"');
assertNotIncludes('agents/spawn keeps the secret out of the trusted environment', agentEndpoints, 'envOverrides["BROKER_REGISTRATION_SECRET"]');
assertNotIncludes('agents/spawn keeps the secret out of the trusted environment (indexer form)', agentEndpoints, '["BROKER_REGISTRATION_SECRET"] =');
assertIncludes('agents/spawn revokes the credential when the spawn fails', agentEndpoints, 'spawnService.RevokeSpawnCredential(credential.CredentialId');

const spawnService = read('packages/csharp/broker-core/Services/AgentSpawnService.cs');
assertIncludes('agent id normalization exists', spawnService, 'public static string NormalizeAgentId');
assertIncludes('agent runtime descriptor carries default model', spawnService, 'default_model = request.LlmDefaultModel');
assertIncludes('agent runtime descriptor carries tool setting', spawnService, 'supports_tool_calling = request.LlmSupportsToolCalling');
assertIncludes('custom agent id gains canonical prefix', spawnService, 'raw = "agent_" + raw');
assertIncludes('symbol-only agent id gets safe fallback', spawnService, 'string.Equals(normalized, "agent", StringComparison.OrdinalIgnoreCase)');
assertIncludes('post-sanitize agent id keeps canonical prefix', spawnService, 'normalized = "agent_" + normalized');
assertIncludes('list agents follows task/principal pair', spawnService, 'string.Equals(t.AssignedPrincipalId, $"prn_{t.TaskId[5..]}", StringComparison.Ordinal)');
assertIncludes('deactivate normalizes requested agent id', spawnService, 'agentId = NormalizeAgentId(agentId);');
assertIncludes('deactivate revokes registration credentials', spawnService, '_credentials.RevokeFor(principalId, taskId, "Agent deactivated."');
assertIncludes('deactivate revokes sessions', spawnService, '_sessions.RevokeSessionsByTask(taskId, "Agent deactivated."');

const codeArtifactService = read('packages/csharp/broker/Services/HighLevelCodeArtifactService.cs');
assertIncludes('code prompt prioritizes custom components', codeArtifactService, '任何網頁程式都必須優先使用專案自訂元件庫');
assertIncludes('code generator copies custom component runtime', codeArtifactService, 'CopyCustomComponentRuntimeIfAvailable');
assertIncludes('tic tac toe uses component runtime import', codeArtifactService, "import('./runtime/ui_components/index.js')");

const dockerignoreLines = new Set(dockerignore.split(/\r?\n/).map((line) => line.trim()));
for (const pattern of [
    '**/.env',
    '**/.env.*',
    '**/agent-stack.env',
    'packages/csharp/broker/appsettings.Development.json',
    'packages/csharp/workers/line-worker/appsettings.json',
]) {
    assert(dockerignoreLines.has(pattern), `.dockerignore: expected a line ${JSON.stringify(pattern)}`);
}

// 產生器的輸出檔名也不可進 git（範例檔 agent-stack.env.example 則要留在 repo）。
const gitignoreLines = new Set(read('.gitignore').split(/\r?\n/).map((line) => line.trim()));
assert(gitignoreLines.has('**/agent-stack.env'), '.gitignore: expected a line "**/agent-stack.env"');

// 文件只能提到產生器與變數名稱，不得含已外洩值。
for (const docPath of [
    'tools/agent/container/README.md',
    'tools/agent/container/README.html',
    'docs/manuals/agent-container-runbook.md',
    'docs/manuals/agent-container-runbook.html',
    'docs/environment-setup.zh-TW.md',
    'docs/environment-setup.zh-TW.html',
    'tools/agent/container/agent-stack.env.example',
]) {
    const docText = read(docPath);
    assert.deepStrictEqual(findLeakedFragments(docText), [], `${docPath}: contains a previously published development key`);
    assertIncludes(`${docPath} points at the secrets generator`, docText, 'gen-stack-secrets.mjs');
}

// 文件中的手動 compose 指令：每一行（包括 down）都要帶同一份金鑰檔，因為 compose 對每個指令都會
// 展開必填變數；路徑要加引號（使用者目錄可能含空白）；指令區塊不得混用 cmd 的 `set NAME=value`。
const COMPOSE_COMMAND_LINE = /^(?:<pre><code[^>]*>)?\s*podman compose\b/;
const QUOTED_ENV_FILE = /--env-file (?:"|&quot;)\$HOME\/\.bricks4agent\/agent-stack\.env(?:"|&quot;) /;
const CMD_SET_LINE = /^(?:<pre><code[^>]*>)?\s*set [A-Za-z_][A-Za-z0-9_]*=/;
for (const docPath of [
    'tools/agent/container/README.md',
    'tools/agent/container/README.html',
    'docs/manuals/agent-container-runbook.md',
    'docs/manuals/agent-container-runbook.html',
    'docs/environment-setup.zh-TW.md',
    'docs/environment-setup.zh-TW.html',
    'docs/manuals/current-user-manual.zh-TW.md',
    'docs/manuals/current-user-manual.zh-TW.html',
    'docs/manuals/current-technical-manual.zh-TW.md',
    'docs/manuals/current-technical-manual.zh-TW.html',
]) {
    let composeCommands = 0;
    for (const line of read(docPath).split(/\r?\n/)) {
        if (COMPOSE_COMMAND_LINE.test(line)) {
            composeCommands += 1;
            assert(QUOTED_ENV_FILE.test(line), `${docPath}: compose command without the quoted --env-file: ${line}`);
        }
        assert(!CMD_SET_LINE.test(line), `${docPath}: cmd-style "set NAME=value" line: ${line}`);
    }
    assert(composeCommands > 0, `${docPath}: expected at least one manual compose command`);
}

async function validateSecretsGenerator() {
    const generatorPath = path.join(ROOT, 'tools', 'agent', 'container', 'gen-stack-secrets.mjs');
    const generator = await import(pathToFileURL(generatorPath).href);

    const secrets = generator.generateStackSecrets();
    generator.verifyStackSecrets(secrets);
    assert.deepStrictEqual(
        Object.keys(secrets).sort(),
        [...BROKER_SECRET_VARIABLES, ...WORKER_SECRET_VARIABLES].sort(),
        'generator must produce exactly the variables the compose files require'
    );
    assert.deepStrictEqual([...generator.STACK_SECRET_VARIABLES].sort(), Object.keys(secrets).sort());
    for (const [name, value] of Object.entries(secrets)) {
        assert(!LEAKED_SECRET_SHA256.includes(sha256Hex(value)), `generator produced a published value for ${name}`);
    }
    const second = generator.generateStackSecrets();
    assert.notStrictEqual(second.BROKER_SCOPED_TOKEN_SECRET, secrets.BROKER_SCOPED_TOKEN_SECRET);
    assert.notStrictEqual(second.BROKER_ECDH_PRIVATE_KEY_BASE64, secrets.BROKER_ECDH_PRIVATE_KEY_BASE64);

    // 輸出位置必須在 repo 以外：這裡只呼叫純函式，不寫任何檔案。
    for (const insideRepo of [
        path.join(ROOT, 'tools', 'agent', 'container', '.env'),
        path.join(ROOT, 'tools', 'agent', 'container', 'agent-stack.env'),
        path.join(ROOT, 'agent-stack.env'),
    ]) {
        assert.throws(() => generator.assertOutsideRepo(insideRepo), /outside/, `generator must refuse ${insideRepo}`);
    }
    assert.strictEqual(
        generator.isInsideDirectory(
            generator.defaultSecretsPath({ BRICKS4AGENT_SECRETS_DIR: path.join(ROOT, 'secrets') }),
            ROOT
        ),
        true,
        'BRICKS4AGENT_SECRETS_DIR inside the repository must be detected'
    );
    assert.strictEqual(path.basename(generator.defaultSecretsPath({})), 'agent-stack.env');
}

validateSecretsGenerator()
    .then(() => {
        console.log('Agent container config validation passed.');
    })
    .catch((error) => {
        console.error(error);
        process.exit(1);
    });
