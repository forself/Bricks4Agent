#!/usr/bin/env node
'use strict';

// Host-Ollama stack end-to-end: needs a real model served by Ollama on the host.
// CONTAINER_ENGINE=docker runs it with docker / docker compose; the default is podman.

const assert = require('assert');
const http = require('http');
const net = require('net');
const path = require('path');
const { pathToFileURL } = require('url');

const {
    ROOT,
    assertStackHardened,
    buildImages: buildStackImages,
    compose,
    containerEngine,
    run,
} = require('./lib/container-stack');

const engine = containerEngine();
const composeFile = path.join(ROOT, 'tools', 'agent', 'container', 'compose.ollama-host.yml');
const images = [
    ['bricks4agent-broker:latest', 'packages/csharp/broker/Containerfile'],
    ['bricks4agent-agent:latest', 'tools/agent/Containerfile'],
];


function stripAnsi(text) {
    return text.replace(/\x1B\[[0-?]*[ -/]*[@-~]/g, '');
}

function extractAgentReplies(output) {
    return stripAnsi(output)
        .split(/\r?\n/)
        .map((line) => {
            // podman-compose prefixes lines with "[agent] |", docker compose with "agent-1 |"
            const match = line.match(/(?:\[agent\]|\bagent[-_]\d+)\s+\|\s+>\s*(.+)$/);
            return match ? match[1].trim() : '';
        })
        .filter(Boolean);
}

// compose 檔不附預設金鑰（${VAR:?...}）。每次執行在記憶體中產生一組新的金鑰，
// 只放進子行程的 env，不寫檔；up 與 down 必須用同一個 env（down 也會展開 ${VAR:?...}）。
async function generateStackSecretsEnv() {
    const generator = path.join(ROOT, 'tools', 'agent', 'container', 'gen-stack-secrets.mjs');
    const { generateStackSecrets } = await import(pathToFileURL(generator).href);
    return generateStackSecrets();
}

async function buildImages(env) {
    if (process.env.SKIP_IMAGE_BUILD) {
        return;
    }
    await buildStackImages(engine, images, env);
}

async function getFreePort() {
    return await new Promise((resolve, reject) => {
        const server = net.createServer();
        server.on('error', reject);
        server.listen(0, '127.0.0.1', () => {
            const address = server.address();
            server.close(() => resolve(address.port));
        });
    });
}

async function startHostProxy(targetBaseUrl) {
    const port = await getFreePort();
    const server = http.createServer(async (req, res) => {
        try {
            const body = await new Promise((resolve, reject) => {
                const chunks = [];
                req.on('data', (chunk) => chunks.push(chunk));
                req.on('end', () => resolve(Buffer.concat(chunks)));
                req.on('error', reject);
            });

            const response = await fetch(new URL(req.url, targetBaseUrl), {
                method: req.method,
                headers: req.headers,
                body: body.length > 0 ? body : undefined,
            });

            const headers = {};
            for (const [key, value] of response.headers.entries()) {
                headers[key] = value;
            }
            res.writeHead(response.status, headers);
            const responseBody = Buffer.from(await response.arrayBuffer());
            res.end(responseBody);
        } catch (error) {
            res.writeHead(502, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({
                error: error instanceof Error ? error.message : String(error),
            }));
        }
    });

    await new Promise((resolve, reject) => {
        server.on('error', reject);
        server.listen(port, '0.0.0.0', resolve);
    });

    return {
        port,
        async close() {
            await new Promise((resolve) => server.close(resolve));
        },
    };
}

async function getDefaultOllamaModel() {
    const response = await fetch('http://127.0.0.1:11434/api/tags');
    if (!response.ok) {
        throw new Error(`Ollama tags request failed with HTTP ${response.status}`);
    }

    const data = await response.json();
    const models = Array.isArray(data.models) ? data.models : [];
    const modelNames = models
        .filter((model) => model && model.name)
        .map((model) => model.name);
    const preferredModels = ['qwen3.6:latest', 'qwen3.6'];
    const preferredModel = preferredModels.find((name) => modelNames.includes(name));
    const selectedModel = preferredModel ?? modelNames[0] ?? '';

    if (!selectedModel) {
        throw new Error('No local Ollama models available for host stack test.');
    }

    return selectedModel;
}

// The broker container reaches the host proxy through this address.
// podman: the podman machine's default gateway; docker: host.containers.internal,
// which compose.ollama-host.yml maps to the host gateway (extra_hosts: host-gateway).
async function getHostGatewayAddress() {
    if (engine === 'docker') {
        return 'host.containers.internal';
    }
    const result = await run('podman', [
        'machine',
        'ssh',
        'ip route',
    ]);

    if (result.code !== 0) {
        throw new Error(`Unable to inspect podman machine routes.\n${result.stdout}\n${result.stderr}`);
    }

    const defaultLine = result.stdout
        .split(/\r?\n/)
        .find((line) => line.startsWith('default via '));

    if (!defaultLine) {
        throw new Error(`Unable to determine podman gateway IP.\n${result.stdout}`);
    }

    return defaultLine.split(/\s+/)[2];
}

async function main() {
    const modelName = process.env.STACK_MODEL || await getDefaultOllamaModel();
    const proxy = await startHostProxy('http://127.0.0.1:11434');
    const gatewayIp = await getHostGatewayAddress();
    const env = {
        ...process.env,
        ...(await generateStackSecretsEnv()),
        PYTHONIOENCODING: 'utf-8',
        PYTHONUTF8: '1',
        STACK_MODEL: modelName,
        OLLAMA_BASE_URL: `http://${gatewayIp}:${proxy.port}`,
        AGENT_RUN: 'Reply briefly that the broker-mediated Ollama stack completed.',
    };

    try {
        await buildImages(env);

        const upResult = await compose(engine, composeFile, [
            'up',
            '--abort-on-container-exit',
            '--exit-code-from',
            'agent',
        ], { env, stream: true });

        assert.strictEqual(
            upResult.code,
            0,
            `${engine} compose up failed.\nSTDOUT:\n${upResult.stdout}\nSTDERR:\n${upResult.stderr}`
        );

        const combinedOutput = `${upResult.stdout}\n${upResult.stderr}`;
        const plainOutput = stripAnsi(combinedOutput);
        const agentReplies = extractAgentReplies(combinedOutput);
        assert(
            plainOutput.includes(modelName),
            `Expected stack output to include chosen Ollama model ${modelName}.\n${combinedOutput}`
        );
        assert(
            plainOutput.includes('Completed in'),
            `Expected agent to complete a governed run.\n${combinedOutput}`
        );
        assert(
            plainOutput.includes('[Governed] session closed'),
            `Expected governed session to close cleanly.\n${combinedOutput}`
        );
        assert(
            agentReplies.some((reply) => reply.length > 0),
            `Expected agent to produce a non-empty model response.\n${combinedOutput}`
        );
        assert(
            !plainOutput.includes('Broker error') && !plainOutput.includes('API error'),
            `Expected Ollama host stack to complete without broker/API errors.\n${combinedOutput}`
        );

        await assertStackHardened(engine, composeFile, [], env, {
            services: ['broker', 'agent'],
            pidsLimits: { broker: 1024 },
        });

        console.log(`Ollama host stack integration test passed (${engine}).`);
    } finally {
        await compose(engine, composeFile, ['down', '-v'], { env });
        await proxy.close();
    }
}

main().catch((error) => {
    console.error(error);
    process.exit(1);
});
