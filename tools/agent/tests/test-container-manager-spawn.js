#!/usr/bin/env node
'use strict';

// Host-run broker + ContainerManager end-to-end: the broker runs on this machine with
// FunctionPool:ContainerManager enabled and drives the docker/podman CLI to spawn a real agent
// container through POST /api/v1/agents/spawn. Checks:
//   - the run arguments' §13.2 hardening is in effect (read-only rootfs, noexec /tmp tmpfs,
//     cap-drop ALL, no-new-privileges, pids and memory limits, no mounts, dedicated network);
//   - the agent registers with the registration credential the broker issued for the spawn (handed over
//     as a secret environment variable: present in the container, absent from its arguments and from
//     the broker's responses and logs), loads the baked-in manual and completes one run;
//   - spawn input is constrained (broker_url, max_iterations, /workers/spawn refusals);
//   - /agents/stop removes the container (rm -f -v) and revokes the agent's registration credential.
// CONTAINER_ENGINE=docker uses docker; the default is podman. Needs the agent image
// (built here unless SKIP_IMAGE_BUILD is set).

const assert = require('assert');
const crypto = require('crypto');
const fs = require('fs');
const http = require('http');
const net = require('net');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');

const { BrokerClient } = require('../lib/broker-client');
const { ROOT, buildImages, containerEngine, run } = require('./lib/container-stack');

const engine = containerEngine();
const hostAlias = engine === 'docker' ? 'host.docker.internal' : 'host.containers.internal';
const ADMIN_PRINCIPAL_ID = 'prn_spawn_admin';
const ADMIN_TASK_ID = 'task_spawn_admin';
const ADMIN_REGISTRATION_SECRET = crypto.randomBytes(32).toString('base64url');
const TEST_MODEL = 'spawn-test-model';
const READY_TEXT = 'AGENT_READY';

function sleep(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
}

async function getFreePort() {
    return await new Promise((resolve, reject) => {
        const server = net.createServer();
        server.on('error', reject);
        server.listen(0, '127.0.0.1', () => {
            const { port } = server.address();
            server.close(() => resolve(port));
        });
    });
}

async function startFakeOllama() {
    const port = await getFreePort();
    const bodies = [];
    const server = http.createServer((req, res) => {
        let body = '';
        req.on('data', (chunk) => { body += chunk; });
        req.on('end', () => {
            bodies.push(body);
            res.setHeader('Content-Type', 'application/json');
            if (req.method === 'GET' && req.url === '/api/tags') {
                res.end(JSON.stringify({ models: [{ name: TEST_MODEL, size: 1 }] }));
            } else if (req.method === 'POST' && req.url === '/api/chat') {
                const parsed = body ? JSON.parse(body) : {};
                res.end(JSON.stringify({
                    model: parsed.model || TEST_MODEL,
                    message: { content: READY_TEXT, tool_calls: [], thinking: '' },
                    total_duration: 1,
                    eval_count: 1,
                }));
            } else if (req.method === 'GET' && req.url === '/') {
                res.end('"ok"');
            } else {
                res.statusCode = 404;
                res.end('{}');
            }
        });
    });
    await new Promise((resolve, reject) => {
        server.on('error', reject);
        server.listen(port, '127.0.0.1', resolve);
    });
    return { port, seen: () => bodies.join(' '), close: () => new Promise((resolve) => server.close(resolve)) };
}

async function buildBroker() {
    if (process.env.SKIP_BROKER_BUILD) return;
    const result = await run('dotnet', ['build', '--nologo', '-v', 'q', '-m:1', '/nodeReuse:false', 'packages/csharp/broker/Broker.csproj']);
    assert.strictEqual(result.code, 0, `Broker build failed.\n${result.stdout}\n${result.stderr}`);
}

async function startBroker({ brokerPort, poolPort, upstreamPort, privateKeyBase64, networkName, tempDir }) {
    const logs = { stdout: '', stderr: '' };
    const child = spawn('dotnet', ['run', '--no-build', '--no-launch-profile', '--project', 'packages/csharp/broker/Broker.csproj'], {
        cwd: ROOT,
        env: {
            ...process.env,
            ASPNETCORE_URLS: `http://127.0.0.1:${brokerPort}`,
            Database__Path: path.join(tempDir, 'broker.db'),
            HighLevelCoordinator__AccessRoot: path.join(tempDir, 'workspaces'),
            Broker__ScopedToken__Secret: crypto.randomBytes(48).toString('base64'),
            Broker__Encryption__MasterKeyBase64: crypto.randomBytes(32).toString('base64'),
            Broker__Encryption__EcdhPrivateKeyBase64: privateKeyBase64,
            Embedding__Enabled: 'false',
            RagSeed__Enabled: 'false',
            LineChatGateway__RagEnabled: 'false',
            LlmProxy__Enabled: 'true',
            LlmProxy__Provider: 'ollama',
            LlmProxy__BaseUrl: `http://127.0.0.1:${upstreamPort}`,
            LlmProxy__DefaultModel: TEST_MODEL,
            LlmProxy__AllowModelOverride: 'false',
            LlmProxy__SupportsToolCalling: 'true',
            LlmProxy__StreamingEnabled: 'false',
            HighLevelLlm__BaseUrl: `http://127.0.0.1:${upstreamPort}`,
            HighLevelLlm__DefaultModel: TEST_MODEL,
            DevelopmentSeed__Enabled: 'true',
            DevelopmentSeed__PrincipalId: ADMIN_PRINCIPAL_ID,
            DevelopmentSeed__DisplayName: 'Spawn Test Admin',
            DevelopmentSeed__TaskId: ADMIN_TASK_ID,
            DevelopmentSeed__TaskType: 'analysis',
            DevelopmentSeed__AssignedRoleId: 'role_admin',
            DevelopmentSeed__RegistrationSecret: ADMIN_REGISTRATION_SECRET,
            FunctionPool__Enabled: 'true',
            FunctionPool__ListenPort: String(poolPort),
            FunctionPool__BindAddress: '127.0.0.1',
            FunctionPool__StrictMode: 'false',
            FunctionPool__ContainerManager__Enabled: 'true',
            FunctionPool__ContainerManager__Runtime: engine,
            FunctionPool__ContainerManager__NetworkName: '',
            FunctionPool__ContainerManager__AgentBrokerUrl: `http://${hostAlias}:${brokerPort}`,
            FunctionPool__ContainerManager__WorkerImages__agent__Image: 'bricks4agent-agent:latest',
            FunctionPool__ContainerManager__WorkerImages__agent__MemoryLimit: '512m',
            FunctionPool__ContainerManager__WorkerImages__agent__NetworkName: networkName,
        },
        stdio: ['ignore', 'pipe', 'pipe'],
    });
    child.stdout.on('data', (chunk) => { logs.stdout += chunk.toString(); });
    child.stderr.on('data', (chunk) => { logs.stderr += chunk.toString(); });

    const startedAt = Date.now();
    while (Date.now() - startedAt < 90000) {
        try {
            const response = await fetch(`http://127.0.0.1:${brokerPort}/api/v1/health`);
            if (response.ok) {
                return {
                    logs,
                    async stop() {
                        if (!child.killed) child.kill();
                        await Promise.race([new Promise((resolve) => child.once('exit', resolve)), sleep(5000)]);
                    },
                };
            }
        } catch (_) {
            // keep polling
        }
        if (child.exitCode !== null) break;
        await sleep(500);
    }
    child.kill();
    throw new Error(`Broker did not become healthy.\n${logs.stdout}\n${logs.stderr}`);
}

async function adminPost(client, route, body) {
    return await client._encryptedPost(route, { scoped_token: client.scopedToken, ...body });
}

async function waitForExit(containerId, timeoutMs) {
    const startedAt = Date.now();
    while (Date.now() - startedAt < timeoutMs) {
        const state = await run(engine, ['inspect', '--format', '{{.State.Status}}', containerId]);
        if (state.code === 0 && state.stdout.trim() === 'exited') return;
        await sleep(1000);
    }
    throw new Error(`Agent container ${containerId} did not exit within ${timeoutMs} ms`);
}

async function main() {
    const id = crypto.randomBytes(3).toString('hex');
    const networkName = `b4a-spawn-test-${id}`;
    const agentId = `s${id}`;
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'b4a-spawn-test-'));
    const { privateKey, publicKey } = crypto.generateKeyPairSync('ec', {
        namedCurve: 'P-256',
        privateKeyEncoding: { type: 'pkcs8', format: 'der' },
        publicKeyEncoding: { type: 'spki', format: 'der' },
    });

    let upstream = null;
    let broker = null;
    let containerId = null;

    try {
        if (!process.env.SKIP_IMAGE_BUILD) {
            await buildImages(engine, [['bricks4agent-agent:latest', 'tools/agent/Containerfile']], process.env);
        }
        await buildBroker();

        const network = await run(engine, ['network', 'create', networkName]);
        assert.strictEqual(network.code, 0, `network create failed: ${network.stderr}`);

        upstream = await startFakeOllama();
        const brokerPort = await getFreePort();
        broker = await startBroker({
            brokerPort,
            poolPort: await getFreePort(),
            upstreamPort: upstream.port,
            privateKeyBase64: Buffer.from(privateKey).toString('base64'),
            networkName,
            tempDir,
        });

        const admin = new BrokerClient(`http://127.0.0.1:${brokerPort}`, Buffer.from(publicKey).toString('base64'));
        await admin.registerSession(ADMIN_PRINCIPAL_ID, ADMIN_TASK_ID, 'role_admin', ADMIN_REGISTRATION_SECRET);

        // /workers/spawn: agents and caller-supplied environments are refused.
        await assert.rejects(() => adminPost(admin, '/api/v1/workers/spawn', { worker_type: 'agent' }), /Broker error 400/);
        await assert.rejects(() => adminPost(admin, '/api/v1/workers/spawn', {
            worker_type: 'file-worker', environment: { NODE_OPTIONS: '--require /tmp/x.js' },
        }), /Broker error 400/);

        const created = await adminPost(admin, '/api/v1/agents/create', {
            agent_id: agentId,
            display_name: 'Spawn hardening test',
            task_type: 'analysis',
        });
        assert.strictEqual(created.success, true, `agents/create failed: ${JSON.stringify(created)}`);
        const canonicalAgentId = created.data.agent_id;

        // broker_url may only repeat the configured AgentBrokerUrl.
        await assert.rejects(() => adminPost(admin, '/api/v1/agents/spawn', {
            agent_id: canonicalAgentId, broker_url: 'http://elsewhere.invalid:5000',
        }), /Broker error 400/);

        const spawned = await adminPost(admin, '/api/v1/agents/spawn', {
            agent_id: canonicalAgentId,
            run: `Reply with the exact text ${READY_TEXT}.`,
            max_iterations: 100000,
        });
        assert.strictEqual(spawned.success, true, `agents/spawn failed: ${JSON.stringify(spawned)}\n${broker.logs.stdout.slice(-4000)}`);
        containerId = spawned.data.container_id;
        assert(containerId, 'agents/spawn returned no container id');
        assert(spawned.data.registration_credential_id, 'agents/spawn reports the id of the credential it issued');
        assert(!('registration_secret' in spawned.data), 'agents/spawn must not return the secret');

        await waitForExit(containerId, 180000);

        const inspected = await run(engine, ['inspect', containerId]);
        assert.strictEqual(inspected.code, 0, `inspect failed: ${inspected.stderr}`);
        const [container] = JSON.parse(inspected.stdout);
        const host = container.HostConfig || {};
        const env = (container.Config && container.Config.Env) || [];
        const tmpfs = (host.Tmpfs && host.Tmpfs['/tmp']) || '';
        const logs = await run(engine, ['logs', containerId]);
        const output = `${logs.stdout}\n${logs.stderr}`;

        assert.strictEqual(container.State.ExitCode, 0, `agent exited with ${container.State.ExitCode}\n${output}`);
        assert.strictEqual(host.ReadonlyRootfs, true, 'ReadonlyRootfs');
        assert((host.CapDrop || []).some((cap) => /^(CAP_)?ALL$/i.test(cap)) || (Array.isArray(container.EffectiveCaps) && container.EffectiveCaps.length === 0),
            `CapDrop ${JSON.stringify(host.CapDrop)}`);
        assert((host.SecurityOpt || []).some((opt) => /^no-new-privileges(:true)?$/.test(opt)), `SecurityOpt ${JSON.stringify(host.SecurityOpt)}`);
        assert.strictEqual(Number(host.PidsLimit), 256, 'PidsLimit');
        assert.strictEqual(Number(host.Memory), 512 * 1024 * 1024, 'Memory');
        for (const option of ['noexec', 'nosuid', 'nodev', 'size=64m']) {
            assert(tmpfs.includes(option), `/tmp tmpfs options ${JSON.stringify(tmpfs)} lack ${option}`);
        }
        assert.strictEqual((container.Mounts || []).filter((m) => m.Type === 'bind' || m.Type === 'volume').length, 0,
            `agent must have no mounts: ${JSON.stringify(container.Mounts)}`);
        assert.strictEqual(host.NetworkMode, networkName, 'agent runs on its dedicated network');
        assert.notStrictEqual(host.Privileged, true, 'Privileged');
        assert(/^10001(:|$)/.test(String(container.Config.User || '')), `User ${container.Config.User}`);
        assert(env.includes(`BROKER_URL=http://${hostAlias}:${brokerPort}`), 'BROKER_URL is the configured AgentBrokerUrl');
        assert(env.includes('AGENT_MAX_ITERATIONS=50'), 'max_iterations is capped');

        // The registration secret reached the container through the CLI environment (-e NAME); the runtime's
        // inspect output shows it, but it is not in the container's arguments, the spawn response or any log.
        const secretEntry = env.find((entry) => entry.startsWith('BROKER_REGISTRATION_SECRET='));
        assert(secretEntry, 'the agent container has BROKER_REGISTRATION_SECRET');
        const agentSecret = secretEntry.slice('BROKER_REGISTRATION_SECRET='.length);
        assert(agentSecret.length >= 32, 'the issued registration secret is long enough');
        assert(agentSecret !== ADMIN_REGISTRATION_SECRET, 'the agent gets its own credential');
        const containerArgs = [container.Path, ...(container.Args || []), ...((container.Config && container.Config.Cmd) || []), ...((container.Config && container.Config.Entrypoint) || [])];
        assert(!containerArgs.some((arg) => String(arg).includes(agentSecret)), 'the secret is not in the container arguments');
        assert(!JSON.stringify(spawned).includes(agentSecret), 'the secret is not in the spawn response');
        assert(!output.includes(agentSecret), 'the agent does not log the secret');
        assert(!broker.logs.stdout.includes(agentSecret) && !broker.logs.stderr.includes(agentSecret), 'the broker does not log the secret');
        assert(!upstream.seen().includes(agentSecret), 'the model provider never receives the secret');

        assert(output.includes('Loading project manual: /app/AGENT.md'), `agent should load the baked-in manual\n${output}`);
        assert(output.includes(READY_TEXT), `agent should complete one run\n${output}`);
        assert(output.includes('[Governed] session closed'), `agent should close its session\n${output}`);
        console.log(`[spawn] ${engine} agent container ${containerId}: hardened, registered and completed a run.`);

        // /agents/stop removes the container (rm -f -v).
        const stopped = await adminPost(admin, '/api/v1/agents/stop', { agent_id: canonicalAgentId });
        assert.strictEqual(stopped.success, true, `agents/stop failed: ${JSON.stringify(stopped)}`);
        const gone = await run(engine, ['inspect', containerId]);
        assert.notStrictEqual(gone.code, 0, 'agents/stop should remove the container');
        containerId = null;

        // Stopping the agent revoked its credential: the same secret can no longer register.
        const stoppedAgent = new BrokerClient(`http://127.0.0.1:${brokerPort}`, Buffer.from(publicKey).toString('base64'));
        await assert.rejects(
            () => stoppedAgent.registerSession(created.data.principal_id, created.data.task_id, created.data.role_id, agentSecret),
            (error) => error.status === 401 && error.brokerMessage === 'Registration rejected.'
        );

        console.log(`Container manager spawn test passed (${engine}).`);
    } finally {
        if (containerId) await run(engine, ['rm', '-f', '-v', containerId]);
        if (broker) await broker.stop();
        if (upstream) await upstream.close();
        await run(engine, ['network', 'rm', networkName]);
        for (let attempt = 0; attempt < 20; attempt += 1) {
            try {
                fs.rmSync(tempDir, { recursive: true, force: true });
                break;
            } catch (_) {
                await sleep(250);
            }
        }
    }
}

main().catch((error) => {
    console.error(error);
    process.exit(1);
});
