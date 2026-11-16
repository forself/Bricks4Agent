#!/usr/bin/env node
'use strict';

// OpenAI-compatible stack end-to-end (mock-openai): agent -> broker LLM proxy -> mock-openai.
// CONTAINER_ENGINE=docker runs it with docker / docker compose; the default is podman.

const assert = require('assert');
const path = require('path');
const { pathToFileURL } = require('url');

const {
    FINAL_COMPLETED,
    ROOT,
    assertStackHardened,
    buildImages: buildStackImages,
    compose,
    containerEngine,
    governedResponseLines,
} = require('./lib/container-stack');

const engine = containerEngine();
const composeFile = path.join(ROOT, 'tools', 'agent', 'container', 'compose.openai-compatible.yml');
const images = [
    ['bricks4agent-mock-openai:latest', 'tools/agent/container/mock-openai.Containerfile'],
    ['bricks4agent-broker:latest', 'packages/csharp/broker/Containerfile'],
    ['bricks4agent-agent:latest', 'tools/agent/Containerfile'],
];

// compose 檔不附預設金鑰（${VAR:?...}）。每次執行在記憶體中產生一組新的金鑰，
// 只放進子行程的 env，不寫檔；up 與 down 必須用同一個 env（down 也會展開 ${VAR:?...}）。
async function generateStackSecretsEnv() {
    const generator = path.join(ROOT, 'tools', 'agent', 'container', 'gen-stack-secrets.mjs');
    const { generateStackSecrets } = await import(pathToFileURL(generator).href);
    return generateStackSecrets();
}

async function main() {
    const env = {
        ...process.env,
        ...(await generateStackSecretsEnv()),
        PYTHONIOENCODING: 'utf-8',
        PYTHONUTF8: '1',
        OPENAI_API_FORMAT: 'responses',
        AGENT_RUN: 'This is a stack smoke test. Do not call a tool. Finish the run.',
    };

    try {
        if (!process.env.SKIP_IMAGE_BUILD) {
            await buildStackImages(engine, images, env);
        }

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
        // The mock upstream answers with the fixed governed reply; the agent prints it as its only stdout line.
        assert.deepStrictEqual(
            governedResponseLines(combinedOutput, 'agent'),
            [FINAL_COMPLETED],
            `Expected the agent to print exactly ${FINAL_COMPLETED}.\n${combinedOutput}`
        );

        await assertStackHardened(engine, composeFile, [], env, {
            services: ['mock-openai', 'broker', 'agent'],
            pidsLimits: { broker: 1024 },
        });

        console.log(`OpenAI-compatible stack integration test passed (${engine}).`);
    } finally {
        await compose(engine, composeFile, ['down', '-v'], { env });
    }
}

main().catch((error) => {
    console.error(error);
    process.exit(1);
});
