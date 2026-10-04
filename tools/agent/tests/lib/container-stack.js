'use strict';

// Shared helpers for the tools/agent/tests/test-podman-*-stack.js end-to-end tests.
//
// CONTAINER_ENGINE selects the CLI: "podman" (default) or "docker". Both are driven the same
// way: `<engine> build`, `<engine> compose -f ... up/down`, `<engine> inspect`.
// After `up` returns (the containers are stopped, not removed), assertStackHardened() reads
// `<engine> inspect` and checks the design §13.2 hardening of every container in the stack.

const assert = require('assert');
const path = require('path');
const { spawn } = require('child_process');

const { isRootUserSpec } = require('../../container/container-user');

const ROOT = path.resolve(__dirname, '..', '..', '..', '..');
const SUPPORTED_ENGINES = ['podman', 'docker'];

function containerEngine(env = process.env) {
    const engine = String(env.CONTAINER_ENGINE || 'podman').trim().toLowerCase();
    if (!SUPPORTED_ENGINES.includes(engine)) {
        throw new Error(`CONTAINER_ENGINE must be one of ${SUPPORTED_ENGINES.join(', ')} (got "${engine}").`);
    }
    return engine;
}

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
            if (options.stream) process.stdout.write(text);
        });
        child.stderr.on('data', (chunk) => {
            const text = chunk.toString();
            stderr += text;
            if (options.stream) process.stderr.write(text);
        });
        child.on('error', reject);
        child.on('close', (code) => resolve({ code, stdout, stderr }));
    });
}

async function buildImages(engine, images, env) {
    for (const [image, dockerfile] of images) {
        const result = await run(engine, ['build', '-t', image, '-f', dockerfile, '.'], { env, stream: true });
        assert.strictEqual(
            result.code,
            0,
            `${engine} build failed for ${image}.\nSTDOUT:\n${result.stdout}\nSTDERR:\n${result.stderr}`
        );
    }
}

function compose(engine, composeFile, args, options = {}) {
    return run(engine, ['compose', '-f', composeFile, ...args], options);
}

function serviceOf(container) {
    const labels = (container.Config && container.Config.Labels) || {};
    return labels['com.docker.compose.service']
        || labels['io.podman.compose.service']
        || String(container.Name || '').replace(/^\//, '');
}

async function inspectStack(engine, composeFile, profileArgs, env) {
    const ps = await compose(engine, composeFile, [...profileArgs, 'ps', '-a', '-q'], { env });
    assert.strictEqual(ps.code, 0, `${engine} compose ps failed.\n${ps.stderr}`);
    const ids = ps.stdout.split(/\s+/).filter(Boolean);
    assert(ids.length > 0, `${engine} compose ps returned no containers`);
    const inspected = await run(engine, ['inspect', ...ids], { env });
    assert.strictEqual(inspected.code, 0, `${engine} inspect failed.\n${inspected.stderr}`);
    return JSON.parse(inspected.stdout);
}

function hasCapDropAll(container) {
    const capDrop = (container.HostConfig && container.HostConfig.CapDrop) || [];
    if (capDrop.some((cap) => /^(CAP_)?ALL$/i.test(cap))) return true;
    // podman may expand ALL into the individual capabilities and report what is left.
    return Array.isArray(container.EffectiveCaps) && container.EffectiveCaps.length === 0;
}

/** A namespace mode that shares the host's or another container's namespace. */
function sharesNamespace(mode) {
    return /^(host|container:)/i.test(String(mode || ''));
}

/**
 * Asserts the §13.2 hardening of one container from `<engine> inspect` (no runtime needed, so the
 * offline tests can feed it synthetic inspect output). Returns a one-line summary.
 * options.pidsLimit:    expected pids limit (default 256)
 * options.readOnlyBinds: every bind mount must be read-only
 * options.noMounts:      no bind or volume mount at all (the agent)
 */
function assertContainerHardened(service, container, options = {}) {
    const where = `inspect ${service}`;
    const host = container.HostConfig || {};
    const mounts = container.Mounts || [];
    const user = String((container.Config && container.Config.User) || '');
    const securityOpt = host.SecurityOpt || [];
    const expectedPids = options.pidsLimit || 256;

    assert.strictEqual(host.ReadonlyRootfs, true, `${where}: ReadonlyRootfs`);
    assert(hasCapDropAll(container), `${where}: CapDrop must be ALL (${JSON.stringify(host.CapDrop)})`);
    assert.strictEqual((host.CapAdd || []).length, 0, `${where}: CapAdd must be empty (${JSON.stringify(host.CapAdd)})`);
    assert(securityOpt.some((opt) => /^no-new-privileges(:true)?$/.test(opt)), `${where}: SecurityOpt ${JSON.stringify(securityOpt)}`);
    assert(!securityOpt.some((opt) => /unconfined/i.test(opt)), `${where}: SecurityOpt must not be unconfined (${JSON.stringify(securityOpt)})`);
    assert.strictEqual(Number(host.PidsLimit), expectedPids, `${where}: PidsLimit`);
    assert.notStrictEqual(host.Privileged, true, `${where}: Privileged`);
    assert.strictEqual((host.Devices || []).length, 0, `${where}: Devices must be empty (${JSON.stringify(host.Devices)})`);
    assert(!sharesNamespace(host.NetworkMode), `${where}: NetworkMode ${host.NetworkMode}`);
    assert(!sharesNamespace(host.PidMode), `${where}: PidMode ${host.PidMode}`);
    assert(!sharesNamespace(host.IpcMode), `${where}: IpcMode ${host.IpcMode}`);
    // Same rule as ContainerManager.ValidateUser: any part named root or numerically 0 (00, +0, 10001:0) is root.
    assert(user !== '' && !isRootUserSpec(user), `${where}: runs as root (User="${user}")`);
    assert(Object.keys(host.Tmpfs || {}).includes('/tmp')
        || mounts.some((mount) => mount.Type === 'tmpfs' && mount.Destination === '/tmp'), `${where}: /tmp tmpfs`);
    for (const mount of mounts) {
        assert(!/(docker|podman|containerd)\.sock/.test(`${mount.Source || ''} ${mount.Destination || ''}`),
            `${where}: runtime socket mounted (${mount.Source})`);
    }
    if (options.noMounts) {
        const persistent = mounts.filter((mount) => mount.Type === 'bind' || mount.Type === 'volume');
        assert.strictEqual(persistent.length, 0, `${where}: the agent must not have bind or volume mounts (${JSON.stringify(persistent)})`);
    }
    if (options.readOnlyBinds) {
        for (const mount of mounts.filter((m) => m.Type === 'bind')) {
            assert.strictEqual(mount.RW, false, `${where}: bind mount ${mount.Destination} must be read-only`);
        }
    }
    return `${service}(uid ${user}, pids ${host.PidsLimit}, mounts ${mounts.map((m) => `${m.Type}:${m.Destination}${m.RW === false ? ':ro' : ''}`).join(',') || 'none'})`;
}

/**
 * Asserts the §13.2 hardening of every container in a compose stack.
 * options.services:      expected service names (all must be present)
 * options.pidsLimits:    per-service pids limit (default 256)
 * options.readOnlyBinds: services whose bind mounts must all be read-only
 */
async function assertStackHardened(engine, composeFile, profileArgs, env, options) {
    const containers = await inspectStack(engine, composeFile, profileArgs, env);
    const byService = new Map(containers.map((container) => [serviceOf(container), container]));
    for (const service of options.services) {
        assert(byService.has(service), `inspect: service ${service} not found (got ${[...byService.keys()].join(', ')})`);
    }

    const summary = [];
    for (const [service, container] of byService) {
        summary.push(assertContainerHardened(service, container, {
            pidsLimit: options.pidsLimits && options.pidsLimits[service],
            readOnlyBinds: (options.readOnlyBinds || []).includes(service),
            noMounts: service === 'agent',
        }));
    }
    console.log(`[hardening] ${engine} inspect OK: ${summary.join('; ')}`);
}

module.exports = {
    ROOT,
    assertContainerHardened,
    assertStackHardened,
    buildImages,
    compose,
    containerEngine,
    inspectStack,
    run,
    serviceOf,
};
