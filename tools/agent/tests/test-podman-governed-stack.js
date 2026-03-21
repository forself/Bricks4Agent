#!/usr/bin/env node
'use strict';

const assert = require('assert');
const path = require('path');
const { spawn } = require('child_process');

const ROOT = path.resolve(__dirname, '..', '..', '..');
const composeFile = path.join(ROOT, 'tools', 'agent', 'container', 'compose.yml');

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

async function main() {
    const env = {
        ...process.env,
        AGENT_RUN: 'Reply with the exact text STACK_OK.',
        STACK_RESPONSE_TEXT: 'STACK_OK',
        BROKER_ROLE_ID: process.env.BROKER_ROLE_ID || 'role_reader',
        BROKER_TASK_TYPE: process.env.BROKER_TASK_TYPE || 'analysis',
        LINE_CHANNEL_ACCESS_TOKEN: process.env.LINE_CHANNEL_ACCESS_TOKEN || 'stack-test-token',
        LINE_CHANNEL_SECRET: process.env.LINE_CHANNEL_SECRET || 'stack-test-secret',
        LINE_DEFAULT_RECIPIENT_ID: process.env.LINE_DEFAULT_RECIPIENT_ID || 'Ustacktestrecipient',
        LINE_ALLOWED_USER_IDS: process.env.LINE_ALLOWED_USER_IDS || 'Ustacktestrecipient',
    };

    let upResult = null;

    try {
        upResult = await run('podman', [
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
            combinedOutput.includes('STACK_OK'),
            `Expected agent output to include STACK_OK.\n${combinedOutput}`
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
