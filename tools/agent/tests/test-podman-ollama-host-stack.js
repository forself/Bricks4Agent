#!/usr/bin/env node
'use strict';

const assert = require('assert');
const http = require('http');
const net = require('net');
const path = require('path');
const { spawn } = require('child_process');

const ROOT = path.resolve(__dirname, '..', '..', '..');
const composeFile = path.join(ROOT, 'tools', 'agent', 'container', 'compose.ollama-host.yml');

function run(command, args, options = {}) {
    return new Promise((resolve, reject) => {
        const child = spawn(command, args, {
            cwd: ROOT,
            env: options.env || process.env,
            stdio: ['ignore', 'pipe', 'pipe'],
        });

        let stdout = '';
        let stderr = '';

        child.stdout.on('data', (chunk) => {
            stdout += chunk.toString();
        });

        child.stderr.on('data', (chunk) => {
            stderr += chunk.toString();
        });

        child.on('error', reject);
        child.on('close', (code) => {
            resolve({ code, stdout, stderr });
        });
    });
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

async function getFirstOllamaModel() {
    const response = await fetch('http://127.0.0.1:11434/api/tags');
    if (!response.ok) {
        throw new Error(`Ollama tags request failed with HTTP ${response.status}`);
    }

    const data = await response.json();
    const firstModel = Array.isArray(data.models) && data.models.length > 0
        ? data.models[0].name
        : '';

    if (!firstModel) {
        throw new Error('No local Ollama models available for host stack test.');
    }

    return firstModel;
}

async function getPodmanGatewayIp() {
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
    const modelName = await getFirstOllamaModel();
    const proxy = await startHostProxy('http://127.0.0.1:11434');
    const gatewayIp = await getPodmanGatewayIp();
    const env = {
        ...process.env,
        STACK_MODEL: modelName,
        OLLAMA_BASE_URL: `http://${gatewayIp}:${proxy.port}`,
        AGENT_RUN: 'Reply with a short sentence that contains OLLAMA_STACK_OK.',
    };

    try {
        const upResult = await run('podman', [
            'compose',
            '-f',
            composeFile,
            'up',
            '--build',
            '--abort-on-container-exit',
            '--exit-code-from',
            'agent',
        ], { env });

        assert.strictEqual(
            upResult.code,
            0,
            `podman compose up failed.\nSTDOUT:\n${upResult.stdout}\nSTDERR:\n${upResult.stderr}`
        );

        const combinedOutput = `${upResult.stdout}\n${upResult.stderr}`;
        assert(
            combinedOutput.includes(modelName),
            `Expected stack output to include chosen Ollama model ${modelName}.\n${combinedOutput}`
        );

        console.log('Podman Ollama host stack integration test passed.');
    } finally {
        await run('podman', [
            'compose',
            '-f',
            composeFile,
            'down',
            '-v',
        ], { env });
        await proxy.close();
    }
}

main().catch((error) => {
    console.error(error);
    process.exit(1);
});
