#!/usr/bin/env node
'use strict';

const assert = require('assert');
const fs = require('fs');
const path = require('path');
const { assessCaseSuccess, main, outputPath, parseArgs, readLiveConfig, toolObservations } = require('../probe-runner');

const AGENT_ROOT = path.resolve(__dirname, '..');
const OUTPUT_ROOT = path.join(AGENT_ROOT, 'probe-results');

function cleanStaleTestDirs() {
    if (!fs.existsSync(OUTPUT_ROOT)) return;
    for (const entry of fs.readdirSync(OUTPUT_ROOT, { withFileTypes: true })) {
        if (!entry.isDirectory() || !entry.name.startsWith('.probe-runner-test-')) continue;
        const target = path.resolve(OUTPUT_ROOT, entry.name);
        const relative = path.relative(path.resolve(OUTPUT_ROOT), target);
        if (relative.startsWith('..') || path.isAbsolute(relative)) throw new Error('Unsafe probe test cleanup path');
        fs.rmSync(target, { recursive: true, force: true });
    }
}

async function mainTest() {
    const differingTemplates = { messages: [
        { role: 'assistant', tool_calls: [
            { id: 'validation', function: { name: 'validate_definition', arguments: { template: { name: 'Validated' } } } },
            { id: 'generation', function: { name: 'generate_scaffold', arguments: { template: { name: 'Different' } } } },
        ] },
        { role: 'tool', tool_call_id: 'validation', content: '{"ok":true}' },
        { role: 'tool', tool_call_id: 'generation', content: JSON.stringify({ ok: true, zip: { sha256: 'a'.repeat(64) } }) },
    ] };
    assert.equal(toolObservations(differingTemplates).generationSucceeded, false,
        'validating one definition must not count as validating a different generated definition');
    assert.deepStrictEqual(parseArgs(['--mode', 'mock', '--repeats', '2']), { mode: 'mock', repeats: 2, output: null, model: null, caseId: null });
    assert.equal(parseArgs(['--mode', 'live', '--model', 'override-model']).model, 'override-model');
    assert.equal(parseArgs(['--mode', 'live', '--case', 'component-generation-simple']).caseId, 'component-generation-simple');
    assert.throws(() => parseArgs(['--case', 'unknown']), /Unknown case id/);
    assert.throws(() => parseArgs(['--mode', 'live', '--repeats', '0']), /--repeats/);
    assert.throws(() => outputPath('mock', path.join(AGENT_ROOT, '..', 'outside.json')), /must stay under/);
    assert.deepStrictEqual(assessCaseSuccess(
        { id: 'component-generation-simple' },
        { type: 'final', code: 'completed' },
        { validationOk: true, validatedDefinitionSha256: 'a'.repeat(64), generationSucceeded: true },
        false,
        null,
    ), { success: false, reason: 'task_type_mismatch' });
    assert.deepStrictEqual(assessCaseSuccess(
        { id: 'simple-instruction' },
        { type: 'final', code: 'completed' },
        { trace: [{ route: 'query_component_catalog' }] },
        true,
        null,
    ), { success: false, reason: 'unexpected_tool_call' });
    assert.deepStrictEqual(assessCaseSuccess(
        { id: 'component-catalog' },
        { type: 'final', code: 'completed' },
        { trace: [{ route: 'query_component_catalog' }], catalogSucceeded: false },
        true,
        null,
    ), { success: false, reason: 'catalog_query_missing_or_failed' });

    const env = {
        BROKER_URL: 'http://broker.example',
        BROKER_PUB_KEY: 'public-key',
        BROKER_PRINCIPAL_ID: 'principal-test',
        BROKER_TASK_ID: 'task-test',
        BROKER_REGISTRATION_SECRET: 'one-time-secret',
    };
    const config = readLiveConfig(env);
    assert.strictEqual(config.taskId, 'task-test');
    assert.strictEqual(config.registrationSecret, 'one-time-secret');
    assert(!Object.hasOwn(env, 'BROKER_REGISTRATION_SECRET'));

    fs.mkdirSync(OUTPUT_ROOT, { recursive: true });
    cleanStaleTestDirs();
    const testDir = fs.mkdtempSync(path.join(OUTPUT_ROOT, '.probe-runner-test-'));
    const output = path.join(testDir, 'mock-report.json');
    const originalWrite = process.stdout.write;
    const stdout = [];
    process.stdout.write = function capture(chunk) { stdout.push(String(chunk)); return true; };
    try {
        assert.strictEqual(await main(['--mode', 'mock', '--repeats', '2', '--out', output]), 0);
    } finally {
        process.stdout.write = originalWrite;
    }
    assert.strictEqual(stdout.length, 1, 'runner emits one concise status line, not model transcripts');
    const report = JSON.parse(fs.readFileSync(output, 'utf8'));
    assert.strictEqual(report.mode, 'mock');
    assert.strictEqual(report.source, 'deterministic-mock');
    assert.strictEqual(report.results.length, 10);
    assert(report.results.every((row) => row.mode === 'mock' && row.sessionId && row.caseTaskTypeMatchesRuntime && row.caseSuccess));
    assert(report.results.every((row) => row.resolvedModel === 'probe-mock-v1' && row.runtime.taskId));
    assert.strictEqual(new Set(report.results.map((row) => row.sessionId)).size, 10, 'every attempt registers a fresh session');
    assert(report.consistency.every((row) => row.consistencyRate === 1 && row.successRate === 1));
    assert.deepStrictEqual(report.isolation, { freshAgentPerAttempt: true, freshSessionPerAttempt: true, taskGrantQuotasShared: true });
    assert.deepStrictEqual(report.results.filter((row) => row.caseId === 'simple-instruction')[0].toolTrace, []);
    const componentResult = report.results.find((row) => row.caseId === 'component-catalog');
    assert.deepStrictEqual(componentResult.toolTrace.map((item) => item.route), ['query_component_catalog', 'query_component_catalog']);
    assert(componentResult.toolTrace.every((item) => /^[a-f0-9]{64}$/.test(item.argumentsSha256)));
    const pageResult = report.results.find((row) => row.caseId === 'multi-page-generation');
    assert.deepStrictEqual(pageResult.toolTrace.map((item) => item.route),
        ['query_component_catalog', 'validate_definition', 'generate_scaffold']);
    assert.strictEqual(pageResult.validationOk, true);
    assert.strictEqual(pageResult.generationSucceeded, true);
    assert.strictEqual(pageResult.attemptedDefinitionSha256, pageResult.validatedDefinitionSha256);
    assert(/^[a-f0-9]{64}$/.test(pageResult.validatedDefinitionSha256));

    const resolvedTestDir = path.resolve(testDir);
    const relative = path.relative(OUTPUT_ROOT, resolvedTestDir);
    assert(relative.startsWith('.probe-runner-test-'), 'test cleanup target stays inside its own temp directory');
    fs.rmSync(resolvedTestDir, { recursive: true, force: true });
    process.stdout.write('Probe runner tests passed.\n');
}

mainTest().catch((error) => {
    process.stderr.write(`${error.stack || error}\n`);
    process.exitCode = 1;
});
