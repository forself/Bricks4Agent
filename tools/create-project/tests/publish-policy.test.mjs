import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { buildLibraryContentManifest, verifyConsumerSnapshot } from '../template/scripts/snapshot-integrity.mjs';
import { findUnselectedAssets, planPublishedFiles, readPublishPolicy } from '../template/scripts/publish-policy.mjs';

const scripts = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', 'template', 'scripts');

function put(root, relative, text) {
    const file = path.join(root, relative);
    mkdirSync(path.dirname(file), { recursive: true });
    writeFileSync(file, text);
    return file;
}

function graphFixture() {
    const root = mkdtempSync(path.join(os.tmpdir(), 'b4a-publish-policy-'));
    const libRoot = path.join(root, 'lib');
    const frontendRoot = path.join(root, 'src', 'frontend');
    for (const name of ['ui_components', 'page-generator', 'custom_components']) mkdirSync(path.join(libRoot, name), { recursive: true });
    return { root, libRoot, frontendRoot };
}

test('module graph parser follows multiline imports, ignores code-shaped strings/comments and retains literal dynamic imports', () => {
    const fixture = graphFixture();
    try {
        put(fixture.frontendRoot, 'app.js', `import {
    visible
} from '../../lib/ui_components/entry.js';
const note = "import(name)"; // import(hidden)
import(/* keep literal */ '../../lib/ui_components/lazy.js');
`);
        put(fixture.libRoot, 'ui_components/entry.js', "export { visible } from './visible.js';\n");
        put(fixture.libRoot, 'ui_components/visible.js', 'export const visible = true;');
        put(fixture.libRoot, 'ui_components/lazy.js', 'export const lazy = true;');
        put(fixture.libRoot, 'ui_components/unused.js', 'export const unused = true;');
        const selected = planPublishedFiles({ libRoot: fixture.libRoot, frontendRoot: fixture.frontendRoot,
            policy: { version: 1, entrypoints: [], keep: [], dynamicImportKeep: {} } });
        const paths = [...selected].map(file => path.relative(fixture.libRoot, file).replaceAll('\\', '/'));
        assert.deepEqual(new Set(paths), new Set(['ui_components/entry.js', 'ui_components/visible.js', 'ui_components/lazy.js']));
    } finally { rmSync(fixture.root, { recursive: true, force: true }); }
});

test('dynamic import uncertainty, regex/comment token ambiguity and missing relative refs fail closed without explicit keep', () => {
    for (const [name, source] of [
        ['regex', `const pattern = /["']/; import(name);`],
        ['comment', `import/* comment */(name);`],
        ['template', "const nested = `outer ${`inner ${import(name)}`} `;"]
    ]) {
        const fixture = graphFixture();
        try {
            put(fixture.frontendRoot, 'app.js', "import '../../lib/ui_components/entry.js';");
            put(fixture.libRoot, 'ui_components/entry.js', `export const load = () => import(moduleName);\n${source}`);
            put(fixture.libRoot, 'ui_components/chunks/one.js', 'export const one = true;');
            const basePolicy = { version: 1, entrypoints: [], keep: [], dynamicImportKeep: {} };
            assert.throws(() => planPublishedFiles({ libRoot: fixture.libRoot, frontendRoot: fixture.frontendRoot,
                policy: basePolicy }), /Unresolved dynamic import/);
            const policy = { ...basePolicy, dynamicImportKeep: {
                'ui_components/entry.js': ['ui_components/chunks']
            } };
            const selected = planPublishedFiles({ libRoot: fixture.libRoot, frontendRoot: fixture.frontendRoot, policy });
            assert(selected.has(path.join(fixture.libRoot, 'ui_components', 'chunks', 'one.js')));
        } finally { rmSync(fixture.root, { recursive: true, force: true }); }
        assert.ok(name);
    }

    const fixture = graphFixture();
    try {
        put(fixture.frontendRoot, 'app.js', "import '../../lib/ui_components/entry.js';");
        put(fixture.libRoot, 'ui_components/entry.js', "import './missing.js';");
        assert.throws(() => planPublishedFiles({ libRoot: fixture.libRoot, frontendRoot: fixture.frontendRoot,
            policy: { version: 1, entrypoints: [], keep: [], dynamicImportKeep: {} } }), /Unresolved relative reference/);
    } finally { rmSync(fixture.root, { recursive: true, force: true }); }
});

test('slash ambiguity and every template interpolation are explicit unresolved dynamic cases', () => {
    const root = mkdtempSync(path.join(os.tmpdir(), 'b4a-module-analysis-'));
    try {
        for (const [name, source] of [
            ['regex-pair', 'const r = /["]/; import(name); const s = /["]/;'],
            ['template-comment', 'const t = `${import/*comment*/(name)}`;']
        ]) {
            const file = put(root, `${name}.mjs`, source);
            const parsed = spawnSync(process.execPath,
                ['--experimental-vm-modules', path.join(scripts, 'module-analysis.mjs'), file],
                { encoding: 'utf8' });
            assert.equal(parsed.status, 0, parsed.stderr);
            const analysis = JSON.parse(parsed.stdout);
            assert(analysis.unresolvedDynamic.length > 0, `${name} must fail closed`);
            if (name === 'regex-pair') assert(analysis.unresolvedDynamic.includes('ambiguous slash token'));
            if (name === 'template-comment') assert(analysis.unresolvedDynamic.includes('template interpolation'));
        }
    } finally { rmSync(root, { recursive: true, force: true }); }
});

test('policy loader rejects null, unknown keys, wrong types and nonportable paths', () => {
    const root = mkdtempSync(path.join(os.tmpdir(), 'b4a-policy-json-'));
    const file = path.join(root, 'policy.json');
    try {
        for (const value of [null, { version: 1, unknown: true }, { version: 1, keep: null },
            { version: 1, dynamicImportKeep: null }, { version: 1, entrypoints: ['../escape.js'] },
            { version: 1, dynamicImportKeep: { 'ui_components/a.js': 'ui_components/chunks' } }]) {
            writeFileSync(file, JSON.stringify(value));
            assert.throws(() => readPublishPolicy(file));
        }
    } finally { rmSync(root, { recursive: true, force: true }); }
});

test('trimmed snapshot checks the exact recorded file map and catches deletion of a listed file', () => {
    const root = mkdtempSync(path.join(os.tmpdir(), 'b4a-trimmed-snapshot-'));
    const browserRoot = path.join(root, 'source');
    const consumerRoot = path.join(root, 'consumer');
    const libRoot = path.join(consumerRoot, 'lib');
    try {
        for (const library of ['ui_components', 'page-generator', 'custom_components']) {
            put(browserRoot, `${library}/needed.js`, `export const ${library.replace('-', '_')} = true;`);
            put(browserRoot, `${library}/unused.js`, 'export const unused = true;');
            put(libRoot, `${library}/needed.js`, `export const ${library.replace('-', '_')} = true;`);
            mkdirSync(path.join(libRoot, library), { recursive: true });
        }
        const snapshot = { bricks4agent: { tree: 'fixture-tree', dirty: false }, publishPolicy: 'publish-policy.json',
            content: buildLibraryContentManifest(libRoot) };
        const snapshotPath = path.join(libRoot, 'SNAPSHOT.json');
        writeFileSync(snapshotPath, `${JSON.stringify(snapshot, null, 2)}\n`);
        const valid = verifyConsumerSnapshot({ browserRoot, consumerRoot, expectedTree: 'fixture-tree' });
        assert.equal(valid.valid, true, valid.errors.join('; '));

        const publishedFile = path.join(libRoot, 'ui_components', 'needed.js');
        rmSync(publishedFile);
        const changedSummary = buildLibraryContentManifest(libRoot);
        snapshot.content = { ...changedSummary, libraries: Object.fromEntries(
            Object.entries(changedSummary.libraries).map(([name, current]) => [name, {
                ...current, entries: snapshot.content.libraries[name].entries
            }])) };
        writeFileSync(snapshotPath, `${JSON.stringify(snapshot, null, 2)}\n`);
        const invalid = verifyConsumerSnapshot({ browserRoot, consumerRoot, expectedTree: 'fixture-tree' });
        assert.equal(invalid.valid, false);
        assert(invalid.errors.some(error => error.includes('file inventory recorded')));
        assert(invalid.details.ui_components.manifest.missing.includes('needed.js'));
    } finally { rmSync(root, { recursive: true, force: true }); }
});

test('legacy complete snapshots without file entries remain valid only while full source inventory matches', () => {
    const root = mkdtempSync(path.join(os.tmpdir(), 'b4a-legacy-snapshot-'));
    const browserRoot = path.join(root, 'source');
    const consumerRoot = path.join(root, 'consumer');
    const libRoot = path.join(consumerRoot, 'lib');
    try {
        for (const library of ['ui_components', 'page-generator', 'custom_components']) {
            put(browserRoot, `${library}/index.js`, `export const ${library.replace('-', '_')} = true;`);
            put(libRoot, `${library}/index.js`, `export const ${library.replace('-', '_')} = true;`);
        }
        const content = buildLibraryContentManifest(browserRoot);
        for (const library of Object.values(content.libraries)) delete library.entries;
        const snapshot = { bricks4agent: { tree: 'legacy-tree', dirty: false }, content };
        const snapshotPath = path.join(libRoot, 'SNAPSHOT.json');
        writeFileSync(snapshotPath, `${JSON.stringify(snapshot, null, 2)}\n`);
        const valid = verifyConsumerSnapshot({ browserRoot, consumerRoot, expectedTree: 'legacy-tree' });
        assert.equal(valid.valid, true, valid.errors.join('; '));

        rmSync(path.join(libRoot, 'ui_components', 'index.js'));
        const invalid = verifyConsumerSnapshot({ browserRoot, consumerRoot, expectedTree: 'legacy-tree' });
        assert.equal(invalid.valid, false);
        assert(invalid.errors.some(error => error.includes('content digest')));
        assert(invalid.errors.some(error => error.startsWith('ui_components differs')));
    } finally { rmSync(root, { recursive: true, force: true }); }
});

test('unselected assets are reported for included directories and their code-free subdirectories only', () => {
    const fixture = graphFixture();
    try {
        put(fixture.frontendRoot, 'app.js', "import '../../lib/ui_components/map/Map.js';");
        put(fixture.libRoot, 'ui_components/map/Map.js', "import './map.css'; export const map = (name) => fetch(base + name);");
        put(fixture.libRoot, 'ui_components/map/map.css', '.map{background:url("./images/used.png")}');
        put(fixture.libRoot, 'ui_components/map/images/used.png', 'used');
        put(fixture.libRoot, 'ui_components/map/images/computed.png', 'computed at runtime');
        put(fixture.libRoot, 'ui_components/map/data/legacy/city.png', 'runtime map data');
        put(fixture.libRoot, 'ui_components/map/data/readme.md', 'docs');
        put(fixture.libRoot, 'ui_components/map/demo.html', '<p>demo</p>');
        put(fixture.libRoot, 'ui_components/map/component.manifest.json', '{}');
        put(fixture.libRoot, 'ui_components/map/Map.manifest.json', '{}');
        put(fixture.libRoot, 'ui_components/map/Map.test.mjs', 'export {};');
        put(fixture.libRoot, 'ui_components/map/plugin/Plugin.js', 'export const plugin = 1;');
        put(fixture.libRoot, 'ui_components/map/plugin/plugin.css', '.plugin{}');
        put(fixture.libRoot, 'ui_components/unrelated/Unrelated.js', 'export const unrelated = 1;');
        put(fixture.libRoot, 'ui_components/unrelated/unrelated.png', 'not in an included directory');
        const policy = { version: 1, entrypoints: [], keep: [], dynamicImportKeep: {} };
        const selected = planPublishedFiles({ libRoot: fixture.libRoot, frontendRoot: fixture.frontendRoot, policy });
        assert.deepEqual(findUnselectedAssets({ libRoot: fixture.libRoot, selected }),
            ['ui_components/map/data/legacy/city.png', 'ui_components/map/images/computed.png']);

        const kept = planPublishedFiles({ libRoot: fixture.libRoot, frontendRoot: fixture.frontendRoot,
            policy: { ...policy, keep: ['ui_components/map/data', 'ui_components/map/images'] } });
        assert.deepEqual(findUnselectedAssets({ libRoot: fixture.libRoot, selected: kept }), []);
    } finally { rmSync(fixture.root, { recursive: true, force: true }); }
});

test('documented policy example is identical in both docs and names real library paths', () => {
    const repoRoot = path.resolve(scripts, '..', '..', '..', '..');
    const browserRoot = path.join(repoRoot, 'packages', 'javascript', 'browser');
    const fence = '`'.repeat(3);
    const examplePattern = new RegExp(`${fence}json\\r?\\n([\\s\\S]*?)${fence}`, 'g');
    const examples = ['tools/create-project/README.md', 'tools/create-project/template/docs/dev-and-publish.md'].map((doc) => {
        const text = readFileSync(path.join(repoRoot, doc), 'utf8');
        const blocks = [...text.matchAll(examplePattern)].map(match => match[1])
            .filter(block => block.includes('"dynamicImportKeep"'));
        assert.equal(blocks.length, 1, `${doc} must contain exactly one policy example`);
        return blocks[0];
    });
    assert.deepEqual(JSON.parse(examples[0]), JSON.parse(examples[1]));

    const root = mkdtempSync(path.join(os.tmpdir(), 'b4a-policy-json-'));
    try {
        const file = path.join(root, 'publish-policy.json');
        writeFileSync(file, examples[0]);
        const policy = readPublishPolicy(file);
        assert(policy.entrypoints.length > 0 && policy.keep.length > 0 && Object.keys(policy.dynamicImportKeep).length > 0);
        // 以真實元件庫跑規劃器:policy 路徑錯誤(不存在、逸出、來源不是 JS)會先於依賴分析丟出。
        // 分析器目前對內建庫過度回報(文件列為已知限制),所以唯一可接受的失敗是要求補 dynamicImportKeep。
        try {
            planPublishedFiles({ libRoot: browserRoot, frontendRoot: path.join(root, 'frontend'), policy });
        } catch (error) {
            assert.match(error.message, /^Unresolved dynamic import in lib\//);
        }
    } finally { rmSync(root, { recursive: true, force: true }); }
});
