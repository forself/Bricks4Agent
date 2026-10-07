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
// the adapter block runs until the next service-level "  # ──" comment after it
// (the generation worker follows it, then the agent)
const afterAdapter = compose.slice(adapterIdx);
const nextServiceMarker = afterAdapter.search(/\n {2}# ── /);
assert(nextServiceMarker > 0, 'compose has a service marker after the adapter block');
const block = afterAdapter.slice(0, nextServiceMarker);

has('adapter-profile-gated', block, 'profiles: ["adapters"]');
has('adapter-read-only', block, 'read_only: true');
has('adapter-cap-drop', block, 'cap_drop:');
has('adapter-cap-drop-all', block, '- ALL');
has('adapter-no-new-privs', block, 'no-new-privileges:true');
has('adapter-pids-limit', block, 'pids_limit: 256');
has('adapter-sandbox-root', block, 'SandboxRoot: "/workspace"');
has('adapter-worker-net', block, '- worker-net');
has('adapter-tmpfs', block, 'tmpfs:\n      - /tmp');
has('adapter-home-on-tmpfs', block, 'HOME: "/tmp"');
has('adapter-evidence-on-tmpfs', block, 'EvidenceRoot: "/tmp/b4a-evidence"');
// §13.2: no docker socket mounted into the adapter
hasNot('adapter-no-docker-socket', block, 'docker.sock');
// default workspace must not be the real repo bind mount
hasNot('adapter-not-real-repo', block, '../../..:/workspace');
// the adapter is not part of governed generation: no generation network, no generated packages
hasNot('adapter-not-on-generation-net', block, 'generation-net');
hasNot('adapter-no-generation-volume', block, 'generation-out');
hasNot('adapter-block-ends-before-generation-worker', block, 'generation-worker');
// the adapter keeps credential index 2; the generation worker takes index 3
has('adapter-credential-index', compose, 'WorkerAuth__Credentials__2__WorkerType: "execution-adapter-worker"');
has('generation-credential-index', compose, 'WorkerAuth__Credentials__3__WorkerType: "generation-worker"');

// 1b) adapter image: .NET 10 SDK runtime pinned by digest, git from the SDK image, no VOLUME
const containerfile = read('packages/csharp/workers/execution-adapter-worker/Containerfile');
const runtimeStage = containerfile.slice(containerfile.lastIndexOf('\nFROM '));
assert(/\nFROM mcr\.microsoft\.com\/dotnet\/sdk:10\.0@sha256:[0-9a-f]{64}\s*\n/.test(runtimeStage),
    'adapter runtime stage must be sdk:10.0 pinned by digest');
hasNot('adapter-no-apt-git', containerfile, 'apt-get');
assert(!/^\s*VOLUME\b/m.test(runtimeStage), 'adapter-no-volume: the runtime stage must not declare VOLUME');
has('adapter-non-root-user', runtimeStage, 'USER 10004:10004');
has('adapter-home-default', runtimeStage, 'HOME=/tmp');

// 2) broker capability seed
const seed = read('packages/csharp/broker-core/Data/BrokerDbInitializer.cs');
has('seed-repo-cap', seed, 'CapabilityId = "repo.patch.apply"');
has('seed-repo-route', seed, 'Route = "apply_patch"');
has('seed-build-cap', seed, 'CapabilityId = "build.test.run"');
has('seed-build-route', seed, 'Route = "run_build_test"');

// 3) agent tool → capability mapping
const registry = read('tools/agent/lib/tool-registry.js');
has('tool-apply-patch', registry, "apply_patch: 'repo.patch.apply'");
has('tool-run-build-test', registry, "run_build_test: 'build.test.run'");

// 4) worker registers both handlers
const program = read('packages/csharp/workers/execution-adapter-worker/Program.cs');
has('worker-repo-handler', program, 'RepoApplyPatchHandler');
has('worker-build-handler', program, 'BuildTestRunHandler');

console.log('Execution adapter configuration validation passed.');
