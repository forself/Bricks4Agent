#!/usr/bin/env node
'use strict';

// §18.1 end-to-end: a (mock) model drives the agent through the governed chain
// agent -> broker (grant/quota/scope/policy) -> execution-adapter, all under §13.1/§13.2
// isolation, in two steps:
//   1. apply_patch     (repo.patch.apply) -> git apply in the fixture workspace
//   2. run_build_test  (build.test.run)   -> a real `dotnet build` of the fixture project,
//      which exercises the adapter's read-only rootfs, noexec /tmp and pids limit.
// Uses the `adapters` compose profile and a throwaway fixture git repo (never the real source tree).
// CONTAINER_ENGINE=docker runs it with docker / docker compose; the default is podman.

const assert = require('assert');
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');
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
const profileArgs = ['--profile', 'adapters'];
const images = [
    ['bricks4agent-mock-ollama:latest', 'tools/agent/container/mock-ollama.Containerfile'],
    ['bricks4agent-broker:latest', 'packages/csharp/broker/Containerfile'],
    ['bricks4agent-file-worker:latest', 'packages/csharp/workers/file-worker/Containerfile'],
    ['bricks4agent-line-worker:latest', 'packages/csharp/workers/line-worker/Containerfile'],
    ['bricks4agent-execution-adapter-worker:latest', 'packages/csharp/workers/execution-adapter-worker/Containerfile'],
    ['bricks4agent-agent:latest', 'tools/agent/Containerfile'],
];

// compose 檔不附預設金鑰（${VAR:?...}）。每次執行在記憶體中產生一組新的金鑰，
// 只放進子行程的 env，不寫檔；up 與 down 必須用同一個 env（down 也會展開 ${VAR:?...}）。
async function generateStackSecretsEnv() {
    const generator = path.join(ROOT, 'tools', 'agent', 'container', 'gen-stack-secrets.mjs');
    const { generateStackSecrets } = await import(pathToFileURL(generator).href);
    return generateStackSecrets();
}

function git(dir, ...args) {
    return execFileSync('git', args, { cwd: dir, encoding: 'utf8' });
}

// Create a throwaway git repo with a minimal console project, return { dir, relForCompose, patch }
function makeFixture() {
    const id = `${process.pid}-${Math.floor(process.hrtime()[1])}`;
    const dir = path.join(ROOT, '.test-output', `adapter-fixture-${id}`);
    fs.mkdirSync(dir, { recursive: true });
    git(dir, 'init', '-q');
    git(dir, 'config', 'user.email', 'test@bricks4agent.local');
    git(dir, 'config', 'user.name', 'test');
    git(dir, 'config', 'core.autocrlf', 'false');
    fs.writeFileSync(path.join(dir, 'fileA.txt'), 'v1\n');
    fs.writeFileSync(path.join(dir, '.gitignore'), 'bin/\nobj/\n');
    fs.writeFileSync(path.join(dir, 'Fixture.csproj'), [
        '<Project Sdk="Microsoft.NET.Sdk">',
        '  <PropertyGroup>',
        '    <OutputType>Exe</OutputType>',
        '    <TargetFramework>net10.0</TargetFramework>',
        '    <ImplicitUsings>enable</ImplicitUsings>',
        '    <Nullable>enable</Nullable>',
        '  </PropertyGroup>',
        '</Project>',
        '',
    ].join('\n'));
    fs.writeFileSync(path.join(dir, 'Program.cs'), 'Console.WriteLine("adapter fixture");\n');
    git(dir, 'add', '-A');
    git(dir, 'commit', '-q', '-m', 'init');
    // produce a patch that appends v2, then revert so the repo sits at base
    fs.writeFileSync(path.join(dir, 'fileA.txt'), 'v1\nv2\n');
    const patch = git(dir, 'diff');
    git(dir, 'checkout', '--', '.');
    // path relative to the compose file dir (tools/agent/container)
    const relForCompose = path.relative(path.dirname(composeFile), dir).split(path.sep).join('/');
    return { dir, relForCompose, patch };
}

async function main() {
    const fixture = makeFixture();
    const env = {
        ...process.env,
        ...(await generateStackSecretsEnv()),
        PYTHONIOENCODING: 'utf-8',
        PYTHONUTF8: '1',
        ADAPTER_WORKSPACE: fixture.relForCompose,
        STACK_TOOL_SEQUENCE_JSON: JSON.stringify([
            { name: 'apply_patch', args: { patch: fixture.patch } },
            { name: 'run_build_test', args: { command: 'dotnet build' } },
        ]),
        // apply_patch must succeed; the build must exit 0 (proves noexec /tmp + pids limit are workable)
        STACK_EXPECT_TOOL_RESULTS_JSON: JSON.stringify([
            ['"capability":"repo.patch.apply"', '"success":true'],
            ['"capability":"build.test.run"', '"exit_code":0'],
        ]),
        // a cold `dotnet build` can take longer than the 30 s default dispatch timeout
        POOL_DISPATCH_TIMEOUT_SECONDS: process.env.POOL_DISPATCH_TIMEOUT_SECONDS || '240',
        AGENT_RUN: 'Apply the available patch with apply_patch, build with run_build_test, then reply with the exact text EXEC_ADAPTER_OK.',
        STACK_RESPONSE_TEXT: 'EXEC_ADAPTER_OK',
        BROKER_ROLE_ID: process.env.BROKER_ROLE_ID || 'role_reader',
        BROKER_TASK_TYPE: process.env.BROKER_TASK_TYPE || 'analysis',
        LINE_CHANNEL_ACCESS_TOKEN: process.env.LINE_CHANNEL_ACCESS_TOKEN || 'stack-test-token',
        LINE_CHANNEL_SECRET: process.env.LINE_CHANNEL_SECRET || 'stack-test-secret',
        LINE_DEFAULT_RECIPIENT_ID: process.env.LINE_DEFAULT_RECIPIENT_ID || 'Ustacktestrecipient',
        LINE_ALLOWED_USER_IDS: process.env.LINE_ALLOWED_USER_IDS || 'Ustacktestrecipient',
    };

    try {
        if (!process.env.SKIP_IMAGE_BUILD) {
            await buildStackImages(engine, images, env);
        }

        const up = await compose(engine, composeFile, [
            ...profileArgs, 'up', '--abort-on-container-exit', '--exit-code-from', 'agent',
        ], { env, stream: true });

        assert.strictEqual(up.code, 0, `${engine} compose up failed.\n${up.stderr}`);

        const out = `${up.stdout}\n${up.stderr}`;
        assert(out.includes('EXEC_ADAPTER_OK'), `Expected EXEC_ADAPTER_OK in agent output.\n${out}`);
        assert(out.includes('[governed] apply_patch'), `Expected governed apply_patch in output.\n${out}`);
        assert(out.includes('[governed] run_build_test'), `Expected governed run_build_test in output.\n${out}`);
        assert(out.includes('TOOL_RESULT_VERIFIED') && !out.includes('TOOL_RESULT_MISMATCH'),
            `Expected apply_patch to succeed and dotnet build to exit 0 inside the hardened adapter.\n${out}`);

        // the real proof: the fixture file was actually patched and built through the chain
        const applied = fs.readFileSync(path.join(fixture.dir, 'fileA.txt'), 'utf8');
        assert.strictEqual(applied, 'v1\nv2\n',
            `Expected fixture fileA.txt to be patched to "v1\\nv2\\n", got ${JSON.stringify(applied)}`);
        assert(fs.existsSync(path.join(fixture.dir, 'bin', 'Debug', 'net10.0', 'Fixture.dll')),
            'Expected dotnet build output in the fixture workspace');

        await assertStackHardened(engine, composeFile, profileArgs, env, {
            services: ['mock-ollama', 'broker', 'file-worker', 'line-worker', 'execution-adapter-worker', 'agent'],
            pidsLimits: { broker: 1024 },
            readOnlyBinds: ['file-worker'],
        });

        console.log(`Execution-adapter stack integration test passed (${engine}; patch applied and dotnet build ran through the governed chain).`);
    } finally {
        await compose(engine, composeFile, [...profileArgs, 'down', '-v'], { env });
        try { fs.rmSync(fixture.dir, { recursive: true, force: true }); } catch { /* best-effort */ }
    }
}

main().catch((error) => {
    console.error(error);
    process.exit(1);
});
