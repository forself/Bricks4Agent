#!/usr/bin/env node
'use strict';

/**
 * Bricks4Agent Convergence Test Suite
 * Tests the pipeline convergence refactoring:
 * - generate-api.js functions
 * - crud-pipeline 3-state structure
 * - StateMachine handler support
 * - page-gen.js field types and pageName
 * - DefinitionTemplate integration
 * - Golden sample validation
 */

const { execSync } = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');

const ROOT = path.resolve(__dirname, '..', '..', '..');

let pass = 0, fail = 0;
function test(name, fn) {
    try { fn(); pass++; console.log('  \u2713 ' + name); }
    catch(e) { fail++; console.log('  \u2717 ' + name + ': ' + (e.message || e)); }
}

console.log('=== Bricks4Agent Convergence Test Suite ===\n');

// --- generate-api.js ---

test('generate-api.js: all functions exported', () => {
    const g = require(path.join(ROOT, 'templates/spa/scripts/generate-api.js'));
    ['buildServiceRegistration','generateModel','generateService','generateEndpoints','generateDbMethods','generateAll'].forEach(f => {
        if (typeof g[f] !== 'function') throw new Error('missing ' + f);
    });
});

test('generate-api.js: service registration uses AddScoped', () => {
    const g = require(path.join(ROOT, 'templates/spa/scripts/generate-api.js'));
    const registration = g.buildServiceRegistration('Order');
    if (!registration.includes('AddScoped<IOrderService, OrderService>()')) {
        throw new Error('expected AddScoped registration');
    }
});

test('generate-api.js: model generation', () => {
    const g = require(path.join(ROOT, 'templates/spa/scripts/generate-api.js'));
    const m = g.generateModel('Order', [{name:'Total',type:'decimal'}], 'TestNs');
    if (!m.content.includes('class Order')) throw new Error('missing class');
    if (!m.content.includes('CreateOrderRequest')) throw new Error('missing DTO');
});

test('generate-api.js: service generation', () => {
    const g = require(path.join(ROOT, 'templates/spa/scripts/generate-api.js'));
    const s = g.generateService('Order', [{name:'Total',type:'decimal'}], 'TestNs');
    if (!s.content.includes('OrderService : IOrderService')) throw new Error('missing class');
    if (!s.content.includes('AppDb _db')) throw new Error('missing db');
});

test('generate-api.js: db methods generation', () => {
    const g = require(path.join(ROOT, 'templates/spa/scripts/generate-api.js'));
    const d = g.generateDbMethods('Order', [{name:'Total',type:'decimal'}]);
    if (!d.tableSql.includes('CREATE TABLE IF NOT EXISTS Orders')) throw new Error('missing sql');
    if (!d.methods.includes('GetAllOrders')) throw new Error('missing method');
});

test('generate-api.js: endpoint generation', () => {
    const g = require(path.join(ROOT, 'templates/spa/scripts/generate-api.js'));
    const e = g.generateEndpoints('Order', []);
    if (!e.content.includes('/api/orders')) throw new Error('missing route');
    if (!e.content.includes('MapDelete')) throw new Error('missing delete');
});

// --- crud-pipeline ---

test('crud-pipeline: builds 3 states', () => {
    const { buildCrudPipeline } = require(path.join(ROOT, 'tools/agent/lib/pipelines/crud-pipeline.js'));
    const states = buildCrudPipeline({
        entityName: 'Task', fields: [{name:'Title',type:'string'}]
    });
    if (states.length !== 3) throw new Error('expected 3, got ' + states.length);
});

test('crud-pipeline: state 1 LLM, states 2-3 deterministic', () => {
    const { buildCrudPipeline } = require(path.join(ROOT, 'tools/agent/lib/pipelines/crud-pipeline.js'));
    const states = buildCrudPipeline({
        entityName: 'Task', fields: [{name:'Title',type:'string'}]
    });
    if (states[0].handler) throw new Error('state 1 should not have handler');
    if (!states[0].promptBuilder) throw new Error('state 1 needs promptBuilder');
    if (!states[1].handler) throw new Error('state 2 needs handler');
    if (!states[2].handler) throw new Error('state 3 needs handler');
});

test('crud-pipeline: frontend validation targets generated routes registry', () => {
    const { buildCrudPipeline } = require(path.join(ROOT, 'tools/agent/lib/pipelines/crud-pipeline.js'));
    const states = buildCrudPipeline({
        entityName: 'Task', fields: [{name:'Title',type:'string'}]
    });
    const routeCheck = states[2].contract.fileChecks.find(fc => fc.path.includes('routes.generated.js'));
    if (!routeCheck) throw new Error('missing generated routes validation');
});

// --- StateMachine ---

test('StateMachine: handler property on State', () => {
    const { State, CompletionContract } = require(path.join(ROOT, 'tools/agent/lib/state-machine.js'));
    const s = new State({
        id: 'x', name: 'x',
        contract: new CompletionContract({ id: 'x', description: 'x' }),
        handler: async () => 'ok'
    });
    if (typeof s.handler !== 'function') throw new Error('handler not set');
});

// --- page-gen.js ---

test('page-gen.js: validation works (single page definition)', () => {
    const tmpDef = path.join(os.tmpdir(), 'bricks-validate-test.json');
    fs.writeFileSync(tmpDef, JSON.stringify({
        page: { pageName: 'TestPage', entity: 'test', view: 'list' },
        fields: [{ fieldName: 'name', label: 'Name', fieldType: 'text' }],
        api: { baseUrl: '/api/tests' }
    }));
    const result = execSync(
        `node tools/page-gen.js --validate --def "${tmpDef}"`,
        { cwd: ROOT, encoding: 'utf8' }
    );
    fs.unlinkSync(tmpDef);
    const parsed = JSON.parse(result.trim());
    if (!parsed.success) throw new Error(JSON.stringify(parsed.errors));
});

test('page-gen.js: pageName field used for output filename', () => {
    const tmpDir = path.join(os.tmpdir(), 'bricks-test-' + Date.now());
    fs.mkdirSync(tmpDir);
    const def = {
        page: { pageName: 'MyCustomPage', entity: 'item', view: 'list' },
        fields: [{ fieldName: 'name', label: 'Name', fieldType: 'text' }],
        api: { baseUrl: '/api/items' }
    };
    const tmp = path.join(os.tmpdir(), 'test-def.json');
    fs.writeFileSync(tmp, JSON.stringify(def));
    execSync(`node tools/page-gen.js --def "${tmp}" --mode static --output "${tmpDir}"`, { cwd: ROOT });
    fs.unlinkSync(tmp);
    const files = fs.readdirSync(tmpDir);
    fs.rmSync(tmpDir, { recursive: true, force: true });
    if (!files.includes('MyCustomPage.js')) throw new Error('expected MyCustomPage.js, got: ' + files);
});

test('page-gen.js: rating and tags field types accepted', () => {
    const tmpDir = path.join(os.tmpdir(), 'bricks-test-' + Date.now());
    fs.mkdirSync(tmpDir);
    const def = {
        page: { pageName: 'ReviewPage', entity: 'review', view: 'form' },
        fields: [
            { fieldName: 'rating', label: 'Rating', fieldType: 'rating' },
            { fieldName: 'tags', label: 'Tags', fieldType: 'tags' }
        ],
        api: { baseUrl: '/api/reviews' }
    };
    const tmp = path.join(os.tmpdir(), 'test-rt.json');
    fs.writeFileSync(tmp, JSON.stringify(def));
    execSync(`node tools/page-gen.js --def "${tmp}" --mode static --output "${tmpDir}"`, { cwd: ROOT });
    fs.unlinkSync(tmp);
    fs.rmSync(tmpDir, { recursive: true, force: true });
});

test('page-gen.js: tel and url field types accepted', () => {
    const tmpDir = path.join(os.tmpdir(), 'bricks-test-' + Date.now());
    fs.mkdirSync(tmpDir);
    const def = {
        page: { pageName: 'ContactPage', entity: 'contact', view: 'form' },
        fields: [
            { fieldName: 'phone', label: 'Phone', fieldType: 'tel' },
            { fieldName: 'website', label: 'Website', fieldType: 'url' }
        ],
        api: { baseUrl: '/api/contacts' }
    };
    const tmp = path.join(os.tmpdir(), 'test-tu.json');
    fs.writeFileSync(tmp, JSON.stringify(def));
    execSync(`node tools/page-gen.js --def "${tmp}" --mode static --output "${tmpDir}"`, { cwd: ROOT });
    fs.unlinkSync(tmp);
    fs.rmSync(tmpDir, { recursive: true, force: true });
});

// --- Golden samples ---

test('golden samples: validation passes', () => {
    const result = execSync('node tools/spa-generator/schemas/golden/validate.js', {
        cwd: ROOT, encoding: 'utf8'
    });
    if (!result.includes('ALL PASS')) throw new Error('validation failed');
});

// --- DefinitionTemplate integration ---

test('page-gen.js: --page flag with DefinitionTemplate', () => {
    const tmpDir = path.join(os.tmpdir(), 'bricks-test-' + Date.now());
    fs.mkdirSync(tmpDir);
    const result = execSync(
        `node tools/page-gen.js --def tools/spa-generator/schemas/golden/spa-generator.definition-template.json --page home --mode static --output "${tmpDir}"`,
        { cwd: ROOT, encoding: 'utf8' }
    );
    const parsed = JSON.parse(result.trim());
    fs.rmSync(tmpDir, { recursive: true, force: true });
    if (!parsed.success) throw new Error(JSON.stringify(parsed.errors));
});

// --- Backend build ---

test('SPA Generator backend: builds with 0 errors', () => {
    const result = execSync('dotnet build --nologo -v q', {
        cwd: path.join(ROOT, 'tools/spa-generator/backend'),
        encoding: 'utf8',
        timeout: 120000
    });
    if (result.includes('錯誤') && !result.includes('0 個錯誤')) {
        throw new Error('build errors detected');
    }
});

// --- Summary ---

console.log(`\n=== Results: ${pass}/${pass+fail} passed ===`);
if (fail > 0) process.exit(1);
