#!/usr/bin/env node
'use strict';

const assert = require('assert');
const http = require('http');
const net = require('net');
const os = require('os');
const path = require('path');
const fs = require('fs');
const crypto = require('crypto');
const { spawn } = require('child_process');

const { AgentLoop } = require('../lib/agent-loop');

const ROOT = path.resolve(__dirname, '..', '..', '..');
const TEST_PRINCIPAL_ID = 'prn_dev_test';
const TEST_TASK_ID = 'task_dev_test';
const TEST_ROLE_ID = 'role_reader';
const TEST_MODEL = 'proxy-test-model';

function createForbiddenDirectProvider() {
    return {
        name: 'forbidden-direct-provider',
        baseUrl: 'http://direct-provider.invalid',
        async healthCheck() {
            throw new Error('direct provider must not be used in broker-mediated governed mode');
        },
        supportsToolCalling() {
            throw new Error('direct provider must not be used in broker-mediated governed mode');
        },
        async chat() {
            throw new Error('direct provider must not be used in broker-mediated governed mode');
        },
        async listModels() {
            throw new Error('direct provider must not be used in broker-mediated governed mode');
        },
    };
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

function readJson(req) {
    return new Promise((resolve, reject) => {
        let data = '';
        req.on('data', (chunk) => { data += chunk; });
        req.on('end', () => {
            try {
                resolve(data ? JSON.parse(data) : {});
            } catch (error) {
                reject(error);
            }
        });
        req.on('error', reject);
    });
}

async function startFakeOllamaServer() {
    const port = await getFreePort();
    const captured = {
        chatBodies: [],
    };

    const server = http.createServer(async (req, res) => {
        try {
            if (req.method === 'GET' && req.url === '/') {
                res.writeHead(200, { 'Content-Type': 'text/plain' });
                res.end('ok');
                return;
            }

            if (req.method === 'GET' && req.url === '/api/tags') {
                res.writeHead(200, { 'Content-Type': 'application/json' });
                res.end(JSON.stringify({
                    models: [
                        { name: TEST_MODEL, size: 123456 },
                    ],
                }));
                return;
            }

            if (req.method === 'POST' && req.url === '/api/chat') {
                const body = await readJson(req);
                captured.chatBodies.push(body);
                res.writeHead(200, { 'Content-Type': 'application/json' });
                res.end(JSON.stringify({
                    model: body.model,
                    message: {
                        content: 'proxy upstream ok',
                        tool_calls: [],
                        thinking: '',
                    },
                    total_duration: 1,
                    eval_count: 2,
                }));
                return;
            }

            res.writeHead(404, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({ error: 'not found' }));
        } catch (error) {
            res.writeHead(500, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({ error: error.message }));
        }
    });

    await new Promise((resolve, reject) => {
        server.on('error', reject);
        server.listen(port, '127.0.0.1', resolve);
    });

    return {
        port,
        captured,
        async close() {
            await new Promise((resolve) => server.close(resolve));
        },
    };
}

function sleep(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
}

async function waitForHealth(url, timeoutMs, logs) {
    const startedAt = Date.now();
    while (Date.now() - startedAt < timeoutMs) {
        try {
            const response = await fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({}),
            });
            if (response.ok) {
                return;
            }
        } catch (_) {
            // keep polling
        }
        await sleep(500);
    }

    throw new Error(`Broker did not become healthy in time.\n${logs.stdout}\n${logs.stderr}`);
}

async function startBroker(brokerPort, upstreamPort, brokerPrivateKeyBase64) {
    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'broker-llm-proxy-'));
    const dbPath = path.join(tempDir, 'broker.db');
    const logs = { stdout: '', stderr: '' };

    const child = spawn('dotnet', [
        'run',
        '--no-launch-profile',
        '--project',
        'packages/csharp/broker/Broker.csproj',
    ], {
        cwd: ROOT,
        env: {
            ...process.env,
            ASPNETCORE_URLS: `http://127.0.0.1:${brokerPort}`,
            Database__Path: dbPath,
            Broker__ScopedToken__Secret: 'THIS_IS_A_TEST_SECRET_WITH_32_BYTES_MIN',
            Broker__Encryption__MasterKeyBase64: crypto.randomBytes(32).toString('base64'),
            Broker__Encryption__EcdhPrivateKeyBase64: brokerPrivateKeyBase64,
            LlmProxy__Enabled: 'true',
            LlmProxy__Provider: 'ollama',
            LlmProxy__BaseUrl: `http://127.0.0.1:${upstreamPort}`,
            LlmProxy__DefaultModel: TEST_MODEL,
            LlmProxy__AllowModelOverride: 'false',
            LlmProxy__SupportsToolCalling: 'true',
            LlmProxy__StreamingEnabled: 'false',
            DevelopmentSeed__Enabled: 'true',
            DevelopmentSeed__PrincipalId: TEST_PRINCIPAL_ID,
            DevelopmentSeed__DisplayName: 'Development Test Principal',
            DevelopmentSeed__TaskId: TEST_TASK_ID,
            DevelopmentSeed__TaskType: 'analysis',
            DevelopmentSeed__AssignedRoleId: TEST_ROLE_ID,
        },
        stdio: ['ignore', 'pipe', 'pipe'],
    });

    child.stdout.on('data', (chunk) => {
        logs.stdout += chunk.toString();
    });
    child.stderr.on('data', (chunk) => {
        logs.stderr += chunk.toString();
    });

    await waitForHealth(`http://127.0.0.1:${brokerPort}/api/v1/health`, 60000, logs);

    return {
        logs,
        tempDir,
        child,
        async stop() {
            if (!child.killed) {
                child.kill();
            }
            await Promise.race([
                new Promise((resolve) => child.once('exit', resolve)),
                sleep(5000),
            ]);
            fs.rmSync(tempDir, { recursive: true, force: true });
        },
    };
}

async function main() {
    const upstream = await startFakeOllamaServer();
    const brokerPort = await getFreePort();
    const { privateKey, publicKey } = crypto.generateKeyPairSync('ec', {
        namedCurve: 'P-256',
        privateKeyEncoding: { type: 'pkcs8', format: 'der' },
        publicKeyEncoding: { type: 'spki', format: 'der' },
    });

    const broker = await startBroker(
        brokerPort,
        upstream.port,
        Buffer.from(privateKey).toString('base64')
    );

    try {
        const agent = new AgentLoop({
            model: 'user-requested-model',
            provider: createForbiddenDirectProvider(),
            projectRoot: ROOT,
            stream: false,
            noConfirm: true,
            verbose: false,
            governed: {
                brokerUrl: `http://127.0.0.1:${brokerPort}`,
                brokerPubKey: Buffer.from(publicKey).toString('base64'),
                principalId: TEST_PRINCIPAL_ID,
                taskId: TEST_TASK_ID,
                roleId: TEST_ROLE_ID,
            },
        });

        await agent.init();

        assert.strictEqual(agent.provider.name, 'broker-governed');
        assert.strictEqual(agent.model, TEST_MODEL);

        const prompt = agent.messages[0].content;
        assert(prompt.includes(`/api/v1/runtime/spec`));
        assert(prompt.includes(`/api/v1/llm/chat`));
        assert(prompt.includes(`"model": "${TEST_MODEL}"`));

        const models = await agent.provider.listModels();
        assert.deepStrictEqual(models.map((item) => item.name), [TEST_MODEL]);

        const reply = await agent.send('Say hello in one short sentence.');
        assert.strictEqual(reply, 'proxy upstream ok');

        assert.strictEqual(upstream.captured.chatBodies.length, 1);
        assert.strictEqual(upstream.captured.chatBodies[0].model, TEST_MODEL);
        assert.strictEqual(upstream.captured.chatBodies[0].stream, false);

        await agent.close();
        console.log('Broker LLM proxy integration test passed.');
    } finally {
        await broker.stop();
        await upstream.close();
    }
}

main().catch((error) => {
    console.error(error);
    process.exit(1);
});
