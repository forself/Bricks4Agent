#!/usr/bin/env node
'use strict';

// Governed stack end-to-end (mock-ollama): agent -> broker -> file-worker read_file.
// CONTAINER_ENGINE=docker runs it with docker / docker compose; the default is podman.
//
// Proof that the file was really read: the mock model only answers with TOOL_RESULT_VERIFIED
// when the tool result it receives through the broker contains text from README.html
// (the `[governed] read_file` log line is printed before the tool runs, so it proves nothing).

const assert = require('assert');
const fs = require('fs');
const path = require('path');
const { pathToFileURL } = require('url');

const {
    ROOT,
    assertStackHardened,
    buildImages: buildStackImages,
    compose,
    containerEngine,
} = require('./lib/container-stack');

const engine = containerEngine();
const composeFile = path.join(ROOT, 'tools', 'agent', 'container', 'compose.yml');
const images = [
    ['bricks4agent-mock-ollama:latest', 'tools/agent/container/mock-ollama.Containerfile'],
    ['bricks4agent-broker:latest', 'packages/csharp/broker/Containerfile'],
    ['bricks4agent-file-worker:latest', 'packages/csharp/workers/file-worker/Containerfile'],
    ['bricks4agent-line-worker:latest', 'packages/csharp/workers/line-worker/Containerfile'],
    ['bricks4agent-agent:latest', 'tools/agent/Containerfile'],
];

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

// Two phrases that only appear in README.html itself (not in a path or an error message).
const README_MARKERS = ['Bricks4Agent', 'What this is'];

async function main() {
    const readme = fs.readFileSync(path.join(ROOT, 'README.html'), 'utf8');
    for (const marker of README_MARKERS) {
        assert(readme.includes(marker), `README.html no longer contains the marker ${JSON.stringify(marker)}; update the test`);
    }

    const env = {
        ...process.env,
        ...(await generateStackSecretsEnv()),
        PYTHONIOENCODING: 'utf-8',
        PYTHONUTF8: '1',
        AGENT_RUN: 'Read README.md through the available tool, then reply with the exact text STACK_OK.',
        STACK_RESPONSE_TEXT: 'STACK_OK',
        STACK_TOOL_CALL: 'read_file',
        STACK_TOOL_PATH: 'README.html',
        STACK_EXPECT_TOOL_RESULTS_JSON: JSON.stringify([README_MARKERS]),
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

        upResult = await compose(engine, composeFile, [
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
        assert(
            combinedOutput.includes('STACK_OK'),
            `Expected agent output to include STACK_OK.\n${combinedOutput}`
        );
        assert(
            combinedOutput.includes('[governed] read_file'),
            `Expected agent output to include governed read_file tool execution.\n${combinedOutput}`
        );
        assert(
            combinedOutput.includes('TOOL_RESULT_VERIFIED') && !combinedOutput.includes('TOOL_RESULT_MISMATCH'),
            `Expected the read_file result to carry README.html content read by the file-worker.\n${combinedOutput}`
        );

        await assertStackHardened(engine, composeFile, [], env, {
            services: ['mock-ollama', 'broker', 'file-worker', 'line-worker', 'agent'],
            pidsLimits: { broker: 1024 },
            readOnlyBinds: ['file-worker'],
        });

        console.log(`Governed stack integration test passed (${engine}).`);
    } finally {
        const downResult = await compose(engine, composeFile, ['down', '-v'], { env });

        if (downResult.code !== 0) {
            console.error(`${engine} compose down failed.\n${downResult.stdout}\n${downResult.stderr}`);
        }
    }
}

main().catch((error) => {
    console.error(error);
    process.exit(1);
});
