#!/usr/bin/env node
'use strict';

// §18.1 end-to-end: a (mock) model drives the agent to apply a patch through the
// governed chain. agent -> broker (grant/quota/scope/policy) -> execution-adapter
// -> git apply, all under §13.1/§13.2 isolation. Uses the `adapters` compose
// profile and a throwaway fixture git repo (never the real source tree).

const assert = require('assert');
const fs = require('fs');
const path = require('path');
const { spawn, execFileSync } = require('child_process');

const ROOT = path.resolve(__dirname, '..', '..', '..');
const composeFile = path.join(ROOT, 'tools', 'agent', 'container', 'compose.yml');
const images = [
    ['bricks4agent-mock-ollama:latest', 'tools/agent/container/mock-ollama.Containerfile'],
    ['bricks4agent-broker:latest', 'packages/csharp/broker/Containerfile'],
    ['bricks4agent-file-worker:latest', 'packages/csharp/workers/file-worker/Containerfile'],
    ['bricks4agent-line-worker:latest', 'packages/csharp/workers/line-worker/Containerfile'],
    ['bricks4agent-execution-adapter-worker:latest', 'packages/csharp/workers/execution-adapter-worker/Containerfile'],
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
        child.stdout.on('data', (c) => { const t = c.toString(); stdout += t; if (options.stream) process.stdout.write(t); });
        child.stderr.on('data', (c) => { const t = c.toString(); stderr += t; if (options.stream) process.stderr.write(t); });
        child.on('error', reject);
        child.on('close', (code) => resolve({ code, stdout, stderr }));
    });
}

async function buildImages(env) {
    for (const [image, dockerfile] of images) {
        const r = await run('podman', ['build', '-t', image, '-f', dockerfile, '.'], { env, stream: true });
        assert.strictEqual(r.code, 0, `podman build failed for ${image}.\n${r.stderr}`);
    }
}

function git(dir, ...args) {
    return execFileSync('git', args, { cwd: dir, encoding: 'utf8' });
}

// Create a throwaway git repo, return { dir, relForCompose, patch }
function makeFixture() {
    const id = `${process.pid}-${Math.floor(process.hrtime()[1])}`;
    const dir = path.join(ROOT, '.test-output', `adapter-fixture-${id}`);
    fs.mkdirSync(dir, { recursive: true });
    git(dir, 'init', '-q');
    git(dir, 'config', 'user.email', 'test@bricks4agent.local');
    git(dir, 'config', 'user.name', 'test');
    git(dir, 'config', 'core.autocrlf', 'false');
    fs.writeFileSync(path.join(dir, 'fileA.txt'), 'v1\n');
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
        PYTHONIOENCODING: 'utf-8',
        PYTHONUTF8: '1',
        ADAPTER_WORKSPACE: fixture.relForCompose,
        STACK_TOOL_CALL: 'apply_patch',
        STACK_TOOL_ARGS_JSON: JSON.stringify({ patch: fixture.patch }),
        AGENT_RUN: 'Apply the available patch using the apply_patch tool, then reply with the exact text EXEC_ADAPTER_OK.',
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
            await buildImages(env);
        }

        const up = await run('podman', [
            'compose', '-f', composeFile, '--profile', 'adapters',
            'up', '--abort-on-container-exit', '--exit-code-from', 'agent',
        ], { env, stream: true });

        assert.strictEqual(up.code, 0, `podman compose up failed.\n${up.stderr}`);

        const out = `${up.stdout}\n${up.stderr}`;
        assert(out.includes('EXEC_ADAPTER_OK'), `Expected EXEC_ADAPTER_OK in agent output.\n${out}`);
        assert(out.includes('[governed] apply_patch') || out.includes('apply_patch'),
            `Expected governed apply_patch in output.\n${out}`);

        // the real proof: the fixture file was actually patched through the chain
        const applied = fs.readFileSync(path.join(fixture.dir, 'fileA.txt'), 'utf8');
        assert.strictEqual(applied, 'v1\nv2\n',
            `Expected fixture fileA.txt to be patched to "v1\\nv2\\n", got ${JSON.stringify(applied)}`);

        console.log('Podman execution-adapter stack integration test passed (patch applied through governed chain).');
    } finally {
        await run('podman', ['compose', '-f', composeFile, '--profile', 'adapters', 'down', '-v'], { env });
        try { fs.rmSync(fixture.dir, { recursive: true, force: true }); } catch { /* best-effort */ }
    }
}

main().catch((error) => {
    console.error(error);
    process.exit(1);
});
