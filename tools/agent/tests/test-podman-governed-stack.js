#!/usr/bin/env node
'use strict';

const assert = require('assert');
const path = require('path');
const { spawn } = require('child_process');

const ROOT = path.resolve(__dirname, '..', '..', '..');
const composeFile = path.join(ROOT, 'tools', 'agent', 'container', 'compose.yml');
const images = [
    ['bricks4agent-mock-ollama:latest', 'tools/agent/container/mock-ollama.Containerfile'],
    ['bricks4agent-broker:latest', 'packages/csharp/broker/Containerfile'],
    ['bricks4agent-file-worker:latest', 'packages/csharp/workers/file-worker/Containerfile'],
    ['bricks4agent-line-worker:latest', 'packages/csharp/workers/line-worker/Containerfile'],
    ['bricks4agent-agent:latest', 'tools/agent/Containerfile'],
];

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
            const text = chunk.toString();
            stdout += text;
            if (options.stream) {
                process.stdout.write(text);
            }
        });

        child.stderr.on('data', (chunk) => {
            const text = chunk.toString();
            stderr += text;
            if (options.stream) {
                process.stderr.write(text);
            }
        });

        child.on('error', reject);
        child.on('close', (code) => {
            resolve({ code, stdout, stderr });
        });
    });
}

async function buildImages(env) {
    for (const [image, dockerfile] of images) {
        const buildResult = await run('podman', [
            'build',
            '-t',
            image,
            '-f',
            dockerfile,
            '.',
        ], { env, stream: true });

        assert.strictEqual(
            buildResult.code,
            0,
            `podman build failed for ${image}.\nSTDOUT:\n${buildResult.stdout}\nSTDERR:\n${buildResult.stderr}`
        );
    }
}

async function main() {
    const env = {
        ...process.env,
        PYTHONIOENCODING: 'utf-8',
        PYTHONUTF8: '1',
        AGENT_RUN: 'Read README.md through the available tool, then reply with the exact text STACK_OK.',
        STACK_RESPONSE_TEXT: 'STACK_OK',
        STACK_TOOL_CALL: 'read_file',
        STACK_TOOL_PATH: 'README.md',
        BROKER_ROLE_ID: process.env.BROKER_ROLE_ID || 'role_reader',
        BROKER_TASK_TYPE: process.env.BROKER_TASK_TYPE || 'analysis',
        LINE_CHANNEL_ACCESS_TOKEN: process.env.LINE_CHANNEL_ACCESS_TOKEN || 'stack-test-token',
        LINE_CHANNEL_SECRET: process.env.LINE_CHANNEL_SECRET || 'stack-test-secret',
        LINE_DEFAULT_RECIPIENT_ID: process.env.LINE_DEFAULT_RECIPIENT_ID || 'Ustacktestrecipient',
        LINE_ALLOWED_USER_IDS: process.env.LINE_ALLOWED_USER_IDS || 'Ustacktestrecipient',
    };

    let upResult = null;

    try {
        await buildImages(env);

        upResult = await run('podman', [
            'compose',
            '-f',
            composeFile,
            'up',
            '--abort-on-container-exit',
            '--exit-code-from',
            'agent',
        ], { env, stream: true });

        assert.strictEqual(
            upResult.code,
            0,
            `podman compose up failed.\nSTDOUT:\n${upResult.stdout}\nSTDERR:\n${upResult.stderr}`
        );

        const combinedOutput = `${upResult.stdout}\n${upResult.stderr}`;
        assert(
            combinedOutput.includes('STACK_OK'),
            `Expected agent output to include STACK_OK.\n${combinedOutput}`
        );
        assert(
            combinedOutput.includes('[governed] read_file'),
            `Expected agent output to include governed read_file tool execution.\n${combinedOutput}`
        );

        console.log('Podman governed stack integration test passed.');
    } finally {
        const downResult = await run('podman', [
            'compose',
            '-f',
            composeFile,
            'down',
            '-v',
        ], { env });

        if (downResult.code !== 0) {
            console.error(`podman compose down failed.\n${downResult.stdout}\n${downResult.stderr}`);
        }
    }
}

main().catch((error) => {
    console.error(error);
    process.exit(1);
});
