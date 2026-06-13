#!/usr/bin/env node
'use strict';

// §18.1 execution-adapter configuration validation (no containers).
// Verifies the compose wiring + §13.2 hardening, the broker capability seed,
// and the agent tool→capability mapping are all in place and consistent.

const assert = require('assert');
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..', '..', '..');

function read(rel) {
    return fs.readFileSync(path.join(ROOT, rel), 'utf8');
}
function has(name, text, needle) {
    assert(text.includes(needle), `${name}: expected to include ${JSON.stringify(needle)}`);
}
function hasNot(name, text, needle) {
    assert(!text.includes(needle), `${name}: expected NOT to include ${JSON.stringify(needle)}`);
}

// 1) compose wiring + hardening — scope to the execution-adapter-worker block
const compose = read('tools/agent/container/compose.yml');
const adapterIdx = compose.indexOf('execution-adapter-worker:');
assert(adapterIdx >= 0, 'compose declares execution-adapter-worker service');
// the adapter block runs until the next top-level "  # ──" comment after it
const afterAdapter = compose.slice(adapterIdx);
const block = afterAdapter.slice(0, afterAdapter.indexOf('# ── Agent ──'));

has('adapter-profile-gated', block, 'profiles: ["adapters"]');
has('adapter-read-only', block, 'read_only: true');
has('adapter-cap-drop', block, 'cap_drop:');
has('adapter-cap-drop-all', block, '- ALL');
has('adapter-no-new-privs', block, 'no-new-privileges:true');
has('adapter-pids-limit', block, 'pids_limit: 256');
has('adapter-sandbox-root', block, 'SandboxRoot: "/workspace"');
has('adapter-worker-net', block, '- worker-net');
// §13.2: no docker socket mounted into the adapter
hasNot('adapter-no-docker-socket', block, 'docker.sock');
// default workspace must not be the real repo bind mount
hasNot('adapter-not-real-repo', block, '../../..:/workspace');

// 2) broker capability seed
const seed = read('packages/csharp/broker-core/Data/BrokerDbInitializer.cs');
has('seed-repo-cap', seed, 'CapabilityId = "repo.patch.apply"');
has('seed-repo-route', seed, 'execution.repo.apply_patch');
has('seed-build-cap', seed, 'CapabilityId = "build.test.run"');
has('seed-build-route', seed, 'execution.build_test.run');

// 3) agent tool → capability mapping
const registry = read('tools/agent/lib/tool-registry.js');
has('tool-apply-patch', registry, "apply_patch: 'repo.patch.apply'");
has('tool-run-build-test', registry, "run_build_test: 'build.test.run'");

// 4) worker registers both handlers
const program = read('packages/csharp/workers/execution-adapter-worker/Program.cs');
has('worker-repo-handler', program, 'RepoApplyPatchHandler');
has('worker-build-handler', program, 'BuildTestRunHandler');

console.log('Execution adapter configuration validation passed.');
