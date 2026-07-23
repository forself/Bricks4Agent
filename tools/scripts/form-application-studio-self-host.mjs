import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { PageGenerator, validateToolPageDefinition } from '../../packages/javascript/browser/page-generator/index.js';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, '..', '..');
const toolDir = path.join(repoRoot, 'tools', 'form-application-studio');
const definitionText = readFileSync(path.join(toolDir, 'studio.page.json'), 'utf8');
const definition = JSON.parse(definitionText);
const studioSource = readFileSync(path.join(toolDir, 'studio.js'), 'utf8');
const controllerSource = readFileSync(path.join(toolDir, 'controller.js'), 'utf8');
const htmlSource = readFileSync(path.join(toolDir, 'index.html'), 'utf8');
const cliPath = path.join(toolDir, 'generate.mjs');

const checks = [];
function check(name, operation) {
    operation();
    checks.push(name);
}

function walk(node, visit) {
    visit(node);
    if (node.type === 'group') node.children.forEach((child) => walk(child, visit));
    if (node.type === 'tabs') node.tabs.forEach((tab) => walk(tab.content, visit));
}

check('ToolPageDefinition is valid', () => {
    const result = validateToolPageDefinition(definition);
    assert.equal(result.valid, true, result.errors.join('\n'));
});

check('FormDesigner is provided by the JSON page definition', () => {
    const components = [];
    walk(definition.root, (node) => {
        if (node.type === 'component') components.push(node.component);
    });
    assert.ok(components.includes('FormDesigner'));
    assert.ok(components.includes('TextInput'));
    assert.ok(components.includes('Dropdown'));
    assert.ok(components.includes('UploadButton'));
    assert.ok(components.includes('DownloadButton'));
});

check('Static tool generation accepts the same definition', () => {
    const result = new PageGenerator().generate(definition);
    assert.deepEqual(result.errors, []);
    assert.match(result.code, /DynamicToolRenderer/);
});

check('Runtime uses DynamicPageRenderer tool mode without iframe', () => {
    assert.match(studioSource, /new DynamicPageRenderer/);
    assert.match(studioSource, /mode:\s*'tool'/);
    assert.doesNotMatch(studioSource, /createElement\(['"]iframe/);
    assert.doesNotMatch(htmlSource, /<iframe/i);
});

check('Connection strings stay out of page state and persistent browser storage', () => {
    assert.doesNotMatch(definitionText, /Data Source=|Server=|Host=|Password=|Pwd=/i);
    assert.doesNotMatch(controllerSource, /localStorage|sessionStorage/);
    assert.doesNotMatch(controllerSource, /console\.(?:log|info|warn|error)\([^\n]*connection/i);
});

check('CLI help is a zero-exit flag and documents SQLite fallback', () => {
    const result = spawnSync(process.execPath, [cliPath, '--help'], {
        cwd: repoRoot,
        encoding: 'utf8',
    });
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, /Usage:/);
    assert.match(result.stdout, /local SQLite/);
});

check('CLI rejects unknown options', () => {
    const result = spawnSync(process.execPath, [cliPath, '--unknown', 'value'], {
        cwd: repoRoot,
        encoding: 'utf8',
    });
    assert.notEqual(result.status, 0);
    assert.match(result.stderr, /Unknown option/);
});

check('CSP and SVG hard-zero declarations are present', () => {
    assert.match(htmlSource, /Content-Security-Policy/);
    assert.doesNotMatch(definitionText, /<svg|data:image\/svg|createElementNS/i);
    assert.doesNotMatch(studioSource, /<svg|data:image\/svg|createElementNS/i);
    assert.doesNotMatch(controllerSource, /<svg|data:image\/svg|createElementNS/i);
});

for (const name of checks) console.log(`ok  ${name}`);
console.log(`\nForm Application Studio self-host: ${checks.length}/${checks.length} passed.`);
