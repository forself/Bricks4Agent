import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { GOLDEN_EXAMPLE_PATH } from './paths.mjs';

const cliPath = path.join(path.dirname(fileURLToPath(import.meta.url)), 'cli.mjs');
const golden = () => JSON.parse(readFileSync(GOLDEN_EXAMPLE_PATH, 'utf8'));
const tempRoot = mkdtempSync(path.join(os.tmpdir(), 'b4a-gen-test-'));
test.after(() => rmSync(tempRoot, { recursive: true, force: true }));

function run(args, input) {
    const child = spawnSync(process.execPath, [cliPath, ...args], {
        input: typeof input === 'string' ? input : JSON.stringify(input),
        encoding: 'utf8',
        maxBuffer: 16 * 1024 * 1024
    });
    const lines = child.stdout.split('\n').filter(Boolean);
    assert.equal(lines.length, 1, `stdout must be exactly one JSON line, got: ${child.stdout.slice(0, 200)}`);
    return { status: child.status, json: JSON.parse(lines[0]), stderr: child.stderr };
}

test('usage errors exit 64 with a JSON error on stdout', () => {
    for (const args of [[], ['nope'], ['validate', 'extra']]) {
        const { status, json } = run(args, {});
        assert.equal(status, 64, args.join(' '));
        assert.equal(json.ok, false);
        assert.equal(json.errors[0].code, 'USAGE');
    }
});

test('input that is not JSON exits 65', () => {
    const { status, json } = run(['validate'], '{ not json');
    assert.equal(status, 65);
    assert.equal(json.errors[0].code, 'INPUT_NOT_JSON');
});

test('oversized stdin exits 65 without echoing the input', () => {
    const { status, json } = run(['validate'], `"${'x'.repeat(1024 * 1024 + 10)}"`);
    assert.equal(status, 65);
    assert.equal(json.errors[0].code, 'INPUT_TOO_LARGE');
});

test('validate exits 0 for both passing and failing definitions', () => {
    const passing = run(['validate'], { template: golden() });
    assert.equal(passing.status, 0);
    assert.equal(passing.json.ok, true);
    assert.deepEqual(Object.keys(passing.json).sort(), ['errors', 'ok', 'pages', 'validation_digest', 'validator_version', 'warnings']);

    const template = golden();
    template.definitions.pages[2].definition.fields.push({ name: 'phone2', type: 'tel', label: '電話二' });
    const failing = run(['validate'], { template });
    assert.equal(failing.status, 0);
    assert.equal(failing.json.ok, false);
    assert.equal(failing.json.errors[0].code, 'FIELD_TYPE_UNSUPPORTED');
    assert.equal(failing.json.errors[0].path, 'definitions.pages[2].definition.fields[7].type');

    const wrongShape = run(['validate'], [1, 2]);
    assert.equal(wrongShape.status, 0);
    assert.equal(wrongShape.json.errors[0].code, 'INPUT_INVALID');
});

test('catalog exits 0 and reports COMPONENT_NOT_FOUND as ok:false', () => {
    const overview = run(['catalog'], {});
    assert.equal(overview.status, 0);
    assert.equal(overview.json.ok, true);
    assert.equal(overview.json.section, 'overview');
    for (const key of ['catalog_sha256', 'matrix_sha256', 'summary_version', 'content']) assert.ok(key in overview.json, key);

    const missing = run(['catalog'], { section: 'component', name: 'NoSuchComponent' });
    assert.equal(missing.status, 0);
    assert.equal(missing.json.ok, false);
    assert.equal(missing.json.errors[0].code, 'COMPONENT_NOT_FOUND');
});

test('build exits 0, writes only under out_dir and returns relative sorted paths', () => {
    const outDir = path.join(tempRoot, 'build-ok');
    const { status, json } = run(['build'], { template: golden(), out_dir: outDir, title: '聯絡人' });
    assert.equal(status, 0);
    assert.equal(json.ok, true);
    const paths = json.files.map(file => file.path);
    assert.deepEqual(paths, [...paths].sort((a, b) => (a < b ? -1 : a > b ? 1 : 0)));
    assert.ok(paths.every(file => file.startsWith('site/') || file.startsWith('report/')));
    assert.ok(!JSON.stringify(json).includes(tempRoot.replaceAll('\\', '\\\\')), 'result must not contain the absolute out_dir');
    for (const key of ['pages', 'files', 'generator_version', 'catalog_sha256', 'validation_digest']) assert.ok(key in json, key);

    const validation = run(['validate'], { template: golden() });
    assert.equal(json.validation_digest, validation.json.validation_digest, 'build and validate digest the same input identically');
});

test('a rejected build exits 0 and creates nothing', () => {
    const outDir = path.join(tempRoot, 'build-rejected');
    const template = golden();
    template.definitions.pages.push({ id: 'tool-page', definition: { name: 'ToolPage', type: 'tool', fields: [{ name: 'x', type: 'text', label: 'x' }] } });
    const { status, json } = run(['build'], { template, out_dir: outDir });
    assert.equal(status, 0);
    assert.equal(json.ok, false);
    assert.ok(json.errors.some(error => error.code === 'PAGE_TYPE_UNSUPPORTED'));
    assert.equal(existsSync(outDir), false);
});
