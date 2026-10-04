#!/usr/bin/env node
'use strict';

// AGENT.md lookup for the system prompt:
// - a manual near the project root always wins (local mode is unchanged);
// - only when none is found does AGENT_MANUAL_PATH apply (the container image sets it,
//   because no repository is mounted into the agent);
// - there is no implicit fallback to this package's own AGENT.md.

const assert = require('assert');
const fs = require('fs');
const os = require('os');
const path = require('path');

const { buildSystemPrompt, resolveAgentManualPath } = require('../lib/system-prompt');

const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'b4a-prompt-manual-'));
const originalManualPath = process.env.AGENT_MANUAL_PATH;

function prompt(projectRoot) {
    return buildSystemPrompt({ projectRoot, useReact: false, verbose: false });
}

try {
    // Deep enough that the four-level upward walk never leaves the temp directory.
    const bareProject = path.join(tempRoot, 'a', 'b', 'c', 'd', 'bare-project');
    const localProject = path.join(tempRoot, 'a', 'b', 'c', 'd', 'local-project');
    const bakedDir = path.join(tempRoot, 'image-app');
    fs.mkdirSync(bareProject, { recursive: true });
    fs.mkdirSync(localProject, { recursive: true });
    fs.mkdirSync(bakedDir, { recursive: true });

    const localManual = path.join(localProject, 'AGENT.md');
    const bakedManual = path.join(bakedDir, 'AGENT.md');
    fs.writeFileSync(localManual, '# Local\n\nLOCAL_MANUAL_MARKER\n');
    fs.writeFileSync(bakedManual, '# Baked\n\nBAKED_MANUAL_MARKER\n');

    // resolveAgentManualPath with an explicit env
    assert.strictEqual(resolveAgentManualPath(localProject, {}), localManual);
    assert.strictEqual(resolveAgentManualPath(localProject, { AGENT_MANUAL_PATH: bakedManual }), localManual,
        'a manual near the project root must win over AGENT_MANUAL_PATH');
    assert.strictEqual(resolveAgentManualPath(bareProject, {}), null,
        'without AGENT_MANUAL_PATH there is no fallback');
    assert.strictEqual(resolveAgentManualPath(bareProject, { AGENT_MANUAL_PATH: bakedManual }), bakedManual);
    assert.strictEqual(resolveAgentManualPath(bareProject, { AGENT_MANUAL_PATH: `  ${bakedManual}  ` }), bakedManual);
    assert.strictEqual(resolveAgentManualPath(bareProject, { AGENT_MANUAL_PATH: 'AGENT.md' }), null,
        'a relative AGENT_MANUAL_PATH is ignored');
    assert.strictEqual(resolveAgentManualPath(bareProject, { AGENT_MANUAL_PATH: path.join(bakedDir, 'missing.md') }), null,
        'a missing AGENT_MANUAL_PATH is ignored');

    // buildSystemPrompt reads process.env
    delete process.env.AGENT_MANUAL_PATH;
    assert(!prompt(bareProject).includes('## Project Manual'), 'no manual section without a manual');
    assert(prompt(localProject).includes('LOCAL_MANUAL_MARKER'));

    process.env.AGENT_MANUAL_PATH = bakedManual;
    const bakedPrompt = prompt(bareProject);
    assert(bakedPrompt.includes('## Project Manual'), 'AGENT_MANUAL_PATH fills the manual section');
    assert(bakedPrompt.includes('BAKED_MANUAL_MARKER'));
    const localPrompt = prompt(localProject);
    assert(localPrompt.includes('LOCAL_MANUAL_MARKER'));
    assert(!localPrompt.includes('BAKED_MANUAL_MARKER'), 'AGENT_MANUAL_PATH must not override a local manual');

    console.log('System prompt manual lookup tests passed.');
} finally {
    if (originalManualPath === undefined) {
        delete process.env.AGENT_MANUAL_PATH;
    } else {
        process.env.AGENT_MANUAL_PATH = originalManualPath;
    }
    fs.rmSync(tempRoot, { recursive: true, force: true });
}
