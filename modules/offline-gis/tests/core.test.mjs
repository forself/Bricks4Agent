import test from 'node:test';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { project, unproject, distance, polygonContains, densityCells } from '../frontend/geometry.js';
import { validateGis } from '../tools/generate.mjs';
import { validateManifest } from '../../../packages/javascript/browser/ui_components/metadata/manifest-schema.js';
import { classes, createGisFactory } from '../frontend/registry.js';
import { GisApi } from '../frontend/api.js';

const definition = () => JSON.parse(fs.readFileSync(new URL('../fixtures/gis.page.json', import.meta.url)));
test('Mercator round trip covers Taiwan, Penghu, Kinmen and Matsu', () => {
    for (const point of [[121.56, 25.03], [119.57, 23.57], [118.32, 24.44], [119.95, 26.16]]) {
        const result = unproject(...project(...point, 10), 10);
        assert.ok(result.every((n, i) => Math.abs(n - point[i]) < 1e-10));
    }
});
test('precise circle and boundary-inclusive concave polygon differ from bbox', () => {
    assert.ok(distance([121, 25], [121.01, 25]) > 1000);
    const ring = [[0, 0], [4, 0], [4, 1], [1, 1], [1, 4], [0, 4]];
    assert.equal(polygonContains([3, 3], ring), false);
    assert.equal(polygonContains([1, 2], ring), true);
    assert.equal(polygonContains([0.5, 3], ring), true);
});
test('density is reproducible weighted screen grid, no significance claim', () => {
    const cells = densityCells([{ lon: 121, lat: 25, weight: 2 }, { lon: 121, lat: 25, weight: 3 }], 8);
    assert.equal(cells.length, 1); assert.equal(cells[0].count, 2); assert.equal(cells[0].weight, 5);
});
test('definition uses existing schema and rejects code, URLs, arbitrary commands, invalid options', () => {
    assert.equal(validateGis(definition()).type, 'tool');
    for (const mutate of [d => d.root.children[2].options.api = 'https://evil.invalid/',
        d => d.root.children[2].options.onChange = 'fetch("/secret")',
        d => d.root.children[0].events.onChange = 'gis.search;alert(1)',
        d => d.root.children[2].options.center = [181, 0],
        d => d.root.children[2].options.center = null,
        d => d.root.children[3].options.max = -1,
        d => d.root.children[0].options = JSON.parse('{"__proto__":{}}')]) {
        const d = definition(); mutate(d); assert.throws(() => validateGis(d));
    }
});
test('JSON property order does not change the command contract', () => {
    const d = definition(); d.root.children[0].events = { onSelect: 'gis.locate', onChange: 'gis.search' };
    assert.equal(validateGis(d), d);
});
test('opt-in local registry does not overwrite global names and rejects unknown types', () => {
    const factory = createGisFactory({});
    assert.equal(factory.has('LeafletMap'), false); assert.throws(() => factory.create('LeafletMap', {}));
    for (const name of Object.keys(classes)) {
        const instance = factory.create(name, {}); assert.equal(typeof instance.destroy, 'function'); instance.destroy();
    }
});
test('module manifests satisfy existing B4A schema and actual public methods', () => {
    for (const [name, Type] of Object.entries(classes)) {
        const manifest = JSON.parse(fs.readFileSync(new URL(`../frontend/${name}.manifest.json`, import.meta.url)));
        const result = validateManifest(manifest, { browserRoot: fileURLToPath(new URL('../../../packages/javascript/browser/', import.meta.url)),
            registryNames: new Set(Object.keys(classes)), publicMethods: Object.getOwnPropertyNames(Type.prototype) });
        assert.deepEqual(result.errors, []);
    }
});
test('adapter accepts an independently configured service and confines requests to it', async () => {
    const original = globalThis.fetch; const calls = [];
    globalThis.fetch = async (url, options) => { calls.push({ url: url.href, options }); return { ok: true, json: async () => ({ candidates: [] }), blob: async () => new Blob(['fixture']) }; };
    try {
        const api = new GisApi('https://service.example.test/custom/api/', { headers: () => ({ Authorization: 'Bearer test-only' }) });
        await api.search('台北1-1號');
        assert.equal(calls[0].url, 'https://service.example.test/custom/api/addresses?q=' + encodeURIComponent('台北1-1號'));
        assert.equal(calls[0].options.headers.Authorization, 'Bearer test-only');
        assert.equal(calls[0].options.credentials, 'same-origin');
        await assert.rejects(api.request('https://other.example.test/'), /configured GIS service/);
        await assert.rejects(api.request('../private'), /configured GIS service/);
        assert.equal(calls.length, 1);
        await api.tile(1, 0, 0);
        assert.equal(calls[1].options.referrerPolicy, 'origin');
        for (const base of ['file:///tmp/', 'https://user:pass@example.test/api/', 'https://example.test/api/?token=bad']) assert.throws(() => new GisApi(base));
    } finally { globalThis.fetch = original; }
});
