#!/usr/bin/env node
'use strict';

const assert = require('assert');
const path = require('path');
const { spawn } = require('child_process');

const ROOT = path.resolve(__dirname, '..', '..', '..');
const composeFile = path.join(ROOT, 'tools', 'agent', 'container', 'compose.openai-compatible.yml');
const images = [
    ['bricks4agent-mock-openai:latest', 'tools/agent/container/mock-openai.Containerfile'],
    ['bricks4agent-broker:latest', 'packages/csharp/broker/Containerfile'],
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
        OPENAI_API_FORMAT: 'responses',
        AGENT_RUN: 'Reply with the exact text STACK_OK.',
        STACK_RESPONSE_TEXT: 'STACK_OK',
    };

    try {
        await buildImages(env);

        const upResult = await run('podman', [
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

        console.log('Podman OpenAI-compatible stack integration test passed.');
    } finally {
        await run('podman', [
            'compose',
            '-f',
            composeFile,
            'down',
            '-v',
        ], { env });
    }
}

main().catch((error) => {
    console.error(error);
    process.exit(1);
});
