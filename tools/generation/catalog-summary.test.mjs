import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import {
    buildCatalogSummary,
    loadCatalogSummary,
    MAX_RESPONSE_BYTES,
    queryCatalog,
    SECTIONS
} from './catalog-summary.mjs';
import { CATALOG_PATH, GOLDEN_EXAMPLE_PATH, MATRIX_PATH, SUMMARY_VERSION } from './paths.mjs';
import { fileSha256, prettyJson } from './json-util.mjs';

const summaryScript = path.join(path.dirname(fileURLToPath(import.meta.url)), 'catalog-summary.mjs');

/** 粗估 token：ASCII 每 4 字元一個，其他字元（含 CJK）每字一個 */
function estimateTokens(value) {
    let ascii = 0;
    let other = 0;
    for (const char of JSON.stringify(value)) {
        if (char.charCodeAt(0) < 128) ascii += 1;
        else other += 1;
    }
    return Math.ceil(ascii / 4) + other;
}

function withTempDir(fn) {
    const dir = mkdtempSync(path.join(os.tmpdir(), 'b4a-gen-test-'));
    try {
        return fn(dir);
    } finally {
        rmSync(dir, { recursive: true, force: true });
    }
}

test('summary hashes match the catalog and support matrix files', () => {
    const summary = buildCatalogSummary();
    assert.equal(summary.catalog_sha256, fileSha256(CATALOG_PATH));
    assert.equal(summary.matrix_sha256, fileSha256(MATRIX_PATH));
    assert.equal(summary.example_sha256, fileSha256(GOLDEN_EXAMPLE_PATH));
    assert.equal(summary.summary_version, SUMMARY_VERSION);
});

test('summary generation is deterministic', () => {
    assert.equal(prettyJson(buildCatalogSummary()), prettyJson(buildCatalogSummary()));
});

test('the default sections stay inside the token budget and every response under 32KB', () => {
    const { summary } = loadCatalogSummary({ pregeneratedPath: null });
    let total = 0;
    for (const section of ['overview', 'field_types', 'example']) {
        const response = queryCatalog({ section }, summary);
        assert.equal(response.ok, true);
        assert.ok(Buffer.byteLength(JSON.stringify(response)) < MAX_RESPONSE_BYTES, `${section} response too large`);
        total += estimateTokens(response.content);
    }
    assert.ok(total <= 4500, `default sections estimated at ${total} tokens`);
    for (const name of Object.keys(summary.components)) {
        const response = queryCatalog({ section: 'component', name }, summary);
        assert.ok(Buffer.byteLength(JSON.stringify(response)) < MAX_RESPONSE_BYTES);
        assert.ok(estimateTokens(response.content) <= 400, `${name} entry too large`);
    }
});

test('the example section is the golden template', () => {
    const { summary } = loadCatalogSummary({ pregeneratedPath: null });
    const response = queryCatalog({ section: 'example' }, summary);
    assert.deepEqual(response.content, JSON.parse(readFileSync(GOLDEN_EXAMPLE_PATH, 'utf8')));
});

test('overview is the default section and field_types lists only open types', () => {
    const { summary } = loadCatalogSummary({ pregeneratedPath: null });
    const overview = queryCatalog({}, summary);
    assert.equal(overview.section, 'overview');
    assert.ok(overview.content.page_definition.type.includes('list'));
    const fieldTypes = queryCatalog({ section: 'field_types' }, summary).content;
    const open = fieldTypes.types.map(entry => entry.type);
    assert.equal(open.length, 22);
    for (const closed of ['tel', 'slider', 'richtext', 'address']) {
        assert.ok(!open.includes(closed));
        assert.ok(fieldTypes.not_supported.includes(closed));
        assert.ok(fieldTypes.use_instead[closed]);
    }
});

test('component lookups return one catalog entry or COMPONENT_NOT_FOUND', () => {
    const { summary } = loadCatalogSummary({ pregeneratedPath: null });
    const found = queryCatalog({ section: 'component', name: 'TextInput' }, summary);
    assert.equal(found.ok, true);
    assert.equal(found.content.registry_name, 'TextInput');
    assert.equal(found.content.generator.usable, true);
    assert.deepEqual(Object.keys(found.content).sort(), ['binding', 'category', 'generator', 'kind', 'maturity', 'registry_name']);

    for (const name of ['textinput', 'NoSuchThing', '', undefined, 42, '__proto__', 'constructor']) {
        const missing = queryCatalog({ section: 'component', name }, summary);
        assert.equal(missing.ok, false);
        assert.equal(missing.errors[0].code, 'COMPONENT_NOT_FOUND');
        assert.equal(missing.catalog_sha256, summary.catalog_sha256);
    }
    assert.equal(queryCatalog({ section: 'everything' }, summary).errors[0].code, 'SECTION_UNKNOWN');
    assert.equal(queryCatalog({ section: 'overview', extra: 1 }, summary).errors[0].code, 'INPUT_UNKNOWN_KEY');
    for (const section of SECTIONS.filter(item => item !== 'component')) {
        assert.equal(queryCatalog({ section }, summary).ok, true);
    }
});

test('a pre-generated summary is used when its hashes match and gives the same answers as live generation', () => {
    withTempDir((dir) => {
        const file = path.join(dir, 'catalog-summary.json');
        const child = spawnSync(process.execPath, [summaryScript, '--out', file], { encoding: 'utf8' });
        assert.equal(child.status, 0, child.stderr);

        const pregenerated = loadCatalogSummary({ pregeneratedPath: file });
        const live = loadCatalogSummary({ pregeneratedPath: null });
        assert.equal(pregenerated.source, 'pregenerated');
        assert.equal(live.source, 'live');
        assert.equal(prettyJson(pregenerated.summary), prettyJson(live.summary));
        for (const request of [{}, { section: 'field_types' }, { section: 'example' }, { section: 'component', name: 'Dropdown' }]) {
            assert.deepEqual(queryCatalog(request, pregenerated.summary), queryCatalog(request, live.summary));
        }
    });
});

test('a stale or damaged pre-generated summary is ignored', () => {
    withTempDir((dir) => {
        const stale = path.join(dir, 'stale.json');
        const summary = buildCatalogSummary();
        writeFileSync(stale, prettyJson({ ...summary, catalog_sha256: '0'.repeat(64), sections: { overview: { tampered: true } } }));
        const loaded = loadCatalogSummary({ pregeneratedPath: stale });
        assert.equal(loaded.source, 'live');
        assert.notDeepEqual(loaded.summary.sections.overview, { tampered: true });

        const otherCode = path.join(dir, 'other-code.json');
        writeFileSync(otherCode, prettyJson({ ...summary, code_sha256: 'f'.repeat(64) }));
        assert.equal(loadCatalogSummary({ pregeneratedPath: otherCode }).source, 'live');

        const damaged = path.join(dir, 'damaged.json');
        writeFileSync(damaged, '{ not json');
        assert.equal(loadCatalogSummary({ pregeneratedPath: damaged }).source, 'live');
        assert.equal(loadCatalogSummary({ pregeneratedPath: path.join(dir, 'missing.json') }).source, 'live');
    });
});

test('responses over the hard size limit are refused', () => {
    const summary = buildCatalogSummary();
    const oversized = { ...summary, sections: { ...summary.sections, overview: { filler: 'x'.repeat(MAX_RESPONSE_BYTES) } } };
    const response = queryCatalog({ section: 'overview' }, oversized);
    assert.equal(response.ok, false);
    assert.equal(response.errors[0].code, 'RESPONSE_TOO_LARGE');
    assert.equal(response.content, undefined);
});

test('catalog-summary.mjs requires --out', () => {
    const child = spawnSync(process.execPath, [summaryScript], { encoding: 'utf8' });
    assert.equal(child.status, 64);
});
