import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import process from 'node:process';
import test from 'node:test';
import { buildSite, isRuntimeComponentFile, listRuntimeSources, SHELL_FILES } from './build-site.mjs';
import {
    CATALOG_PATH,
    GENERATOR_VERSION,
    GOLDEN_EXAMPLE_PATH,
    PACKAGE_FORMAT,
    REPO_ROOT,
    REPO_SUBSET,
    SHELL_DIR,
    UI_COMPONENTS_DIR
} from './paths.mjs';
import { fileSha256, sha256Hex } from './json-util.mjs';

const golden = () => JSON.parse(readFileSync(GOLDEN_EXAMPLE_PATH, 'utf8'));

const tempRoot = mkdtempSync(path.join(os.tmpdir(), 'b4a-gen-test-'));
test.after(() => rmSync(tempRoot, { recursive: true, force: true }));
let counter = 0;
const freshDir = () => path.join(tempRoot, `out-${counter += 1}`);

function listTree(root) {
    const files = [];
    const walk = (dir) => {
        for (const entry of readdirSync(dir, { withFileTypes: true })) {
            const full = path.join(dir, entry.name);
            if (entry.isDirectory()) walk(full);
            else files.push(path.relative(root, full).split(path.sep).join('/'));
        }
    };
    walk(root);
    return files.sort();
}

test('build writes the site and report layout and reports every file with its sha256', async () => {
    const outDir = freshDir();
    const result = await buildSite({ template: golden(), out_dir: outDir, title: '聯絡人原型' });
    assert.equal(result.ok, true, JSON.stringify(result.errors));
    assert.equal(result.generator_version, GENERATOR_VERSION);
    assert.equal(result.catalog_sha256, fileSha256(CATALOG_PATH));
    assert.match(result.validation_digest, /^[0-9a-f]{64}$/);
    assert.deepEqual(result.pages.map(page => page.id), ['contacts-list', 'contact-detail', 'contact-form']);

    const onDisk = listTree(outDir);
    assert.deepEqual(result.files.map(file => file.path), onDisk, 'files[] must list exactly what was written, sorted');
    for (const file of result.files) {
        const bytes = readFileSync(path.join(outDir, ...file.path.split('/')));
        assert.equal(file.sha256, sha256Hex(bytes), file.path);
        assert.equal(file.size, bytes.length, file.path);
        assert.ok(!path.isAbsolute(file.path) && !file.path.includes('\\') && !file.path.includes('..'));
    }
    assert.equal(result.file_count, onDisk.length);

    for (const name of SHELL_FILES) assert.ok(onDisk.includes(`site/${name}`), name);
    for (const required of [
        'site/site.json',
        'site/definition-template.json',
        'site/definitions/contacts-list.json',
        'site/definitions/contact-detail.json',
        'site/definitions/contact-form.json',
        'site/runtime/page-generator/DynamicPageRenderer.js',
        'site/runtime/page-generator/PageDefinitionAdapter.js',
        'site/runtime/ui_components/theme.css',
        'site/runtime/ui_components/i18n/index.js',
        'report/validation.json',
        'report/manifest.json'
    ]) {
        assert.ok(onDisk.includes(required), required);
    }
    assert.ok(!onDisk.some(file => /^site\/runtime\/ui_components\/(data|refresource)\//.test(file)));
    assert.ok(!onDisk.some(file => /demo|\.test\.|\.mjs$|\.md$|\.html$|\.svg$/i.test(file.replace(/^site\/index\.html$/, ''))));

    // 原始頁定義保留 api；執行期 page-generator 是實際檔案而不是轉匯出殼
    const listDefinition = JSON.parse(readFileSync(path.join(outDir, 'site', 'definitions', 'contacts-list.json'), 'utf8'));
    assert.deepEqual(listDefinition.api, { list: '/api/contacts', delete: '/api/contacts' });
    const renderer = readFileSync(path.join(outDir, 'site', 'runtime', 'page-generator', 'DynamicPageRenderer.js'), 'utf8');
    assert.ok(renderer.includes('class DynamicPageRenderer'));
    assert.ok(!renderer.includes('../../../../'));

    const site = JSON.parse(readFileSync(path.join(outDir, 'site', 'site.json'), 'utf8'));
    assert.deepEqual(site, { format: PACKAGE_FORMAT, title: '聯絡人原型', pages: ['contacts-list', 'contact-detail', 'contact-form'] });

    const manifest = JSON.parse(readFileSync(path.join(outDir, 'report', 'manifest.json'), 'utf8'));
    assert.equal(manifest.generator_version, GENERATOR_VERSION);
    assert.equal(manifest.catalog_sha256, result.catalog_sha256);
    assert.equal(manifest.validation_digest, result.validation_digest);
    assert.deepEqual(manifest.files, result.files.filter(file => file.path !== 'report/manifest.json'));
    const validation = JSON.parse(readFileSync(path.join(outDir, 'report', 'validation.json'), 'utf8'));
    assert.equal(validation.ok, true);
    assert.equal(validation.validation_digest, result.validation_digest);
});

test('the shell index.html only loads module scripts and stylesheets by URL', () => {
    const html = readFileSync(path.join(SHELL_DIR, 'index.html'), 'utf8');
    const scripts = [...html.matchAll(/<script\b[^>]*>/gi)].map(match => match[0]);
    assert.deepEqual(scripts, ['<script type="module" src="boot.js">']);
    assert.ok(!/<script\b[^>]*>\s*[^<\s]/i.test(html), 'no inline script');
    assert.ok(!/<style\b/i.test(html));
    assert.ok(!/\sstyle\s*=/i.test(html));
    assert.ok(!/\son[a-z]+\s*=/i.test(html));
    assert.match(html, /Content-Security-Policy" content="default-src 'self'; script-src 'self'; style-src 'self';/);
});

test('two builds of the same input are byte-for-byte identical', async () => {
    const first = freshDir();
    const second = freshDir();
    const request = (outDir) => ({ template: golden(), out_dir: outDir, title: '同一份輸入' });
    const a = await buildSite(request(first));
    const b = await buildSite(request(second));
    assert.equal(a.ok && b.ok, true);
    assert.deepEqual(a.files, b.files);
    const files = listTree(first);
    assert.deepEqual(files, listTree(second));
    for (const file of files) {
        assert.ok(readFileSync(path.join(first, file)).equals(readFileSync(path.join(second, file))), file);
    }
});

test('build runs the same validation as validate and writes nothing when it fails', async () => {
    const template = golden();
    template.definitions.pages[2].definition.fields.push({ name: 'level', type: 'slider', label: '程度' });
    template.definitions.pages[0].definition.page = { entity: '../escaped' };
    const outDir = freshDir();
    const result = await buildSite({ template, out_dir: outDir });
    assert.equal(result.ok, false);
    assert.ok(result.errors.some(error => error.code === 'FIELD_TYPE_UNSUPPORTED'));
    assert.ok(result.errors.some(error => error.code === 'PAGE_ENTITY_NOT_ALLOWED'));
    assert.equal(result.files, undefined);
    assert.equal(existsSync(outDir), false);
    assert.equal(existsSync(path.join(tempRoot, 'escaped')), false);
    assert.equal(existsSync(path.join(path.dirname(outDir), 'escaped-definition.json')), false);
});

test('build rejects a non-empty out_dir, a relative out_dir and a missing out_dir', async () => {
    const outDir = freshDir();
    mkdirSync(outDir);
    writeFileSync(path.join(outDir, 'keep.txt'), 'existing');
    const nonEmpty = await buildSite({ template: golden(), out_dir: outDir });
    assert.equal(nonEmpty.ok, false);
    assert.equal(nonEmpty.errors[0].code, 'OUT_DIR_NOT_EMPTY');
    assert.deepEqual(readdirSync(outDir), ['keep.txt']);

    const relative = await buildSite({ template: golden(), out_dir: 'relative/out' });
    assert.equal(relative.errors[0].code, 'OUT_DIR_INVALID');
    assert.equal(existsSync(path.resolve('relative')), false);

    const missing = await buildSite({ template: golden() });
    assert.equal(missing.errors[0].code, 'OUT_DIR_REQUIRED');

    const orphan = await buildSite({ template: golden(), out_dir: path.join(freshDir(), 'nested', 'out') });
    assert.equal(orphan.errors[0].code, 'OUT_DIR_INVALID');
});

test('build accepts an existing empty out_dir', async () => {
    const outDir = freshDir();
    mkdirSync(outDir);
    const result = await buildSite({ template: golden(), out_dir: outDir, page_ids: ['contact-form'] });
    assert.equal(result.ok, true, JSON.stringify(result.errors));
    assert.deepEqual(result.pages.map(page => page.id), ['contact-form']);
    const site = JSON.parse(readFileSync(path.join(outDir, 'site', 'site.json'), 'utf8'));
    assert.deepEqual(site.pages, ['contact-form']);
    assert.equal(site.title, '聯絡人管理', 'falls back to meta.title');
    assert.equal(existsSync(path.join(outDir, 'site', 'definitions', 'contacts-list.json')), false);
});

test('build rejects an out_dir that is, or contains, a symbolic link or junction', async () => {
    const target = freshDir();
    mkdirSync(target);
    const link = freshDir();
    symlinkSync(target, link, 'junction');
    const asLink = await buildSite({ template: golden(), out_dir: link });
    assert.equal(asLink.ok, false);
    assert.equal(asLink.errors[0].code, 'OUT_DIR_SYMLINK');
    assert.deepEqual(readdirSync(target), []);

    const container = freshDir();
    mkdirSync(container);
    symlinkSync(target, path.join(container, 'inner'), 'junction');
    const containsLink = await buildSite({ template: golden(), out_dir: container });
    assert.equal(containsLink.ok, false);
    assert.equal(containsLink.errors[0].code, 'OUT_DIR_SYMLINK');
    assert.deepEqual(readdirSync(target), []);
});

test('the CLI runs from the documented repo subset alone (worker image layout)', () => {
    const subsetRoot = path.join(tempRoot, 'subset');
    const skipped = new Set(['data', 'refresource', 'node_modules']);
    for (const relative of REPO_SUBSET) {
        const source = path.join(REPO_ROOT, ...relative.split('/'));
        cpSync(source, path.join(subsetRoot, ...relative.split('/')), {
            recursive: true,
            filter: (from) => {
                const rel = path.relative(UI_COMPONENTS_DIR, from);
                const insideComponents = rel !== '' && !rel.startsWith('..') && !path.isAbsolute(rel);
                return !(insideComponents && skipped.has(rel.split(path.sep)[0])) && !from.endsWith('catalog-summary.json');
            }
        });
    }
    assert.equal(existsSync(path.join(subsetRoot, 'package.json')), false, 'the root package.json is not part of the subset');
    const cli = path.join(subsetRoot, 'tools', 'generation', 'cli.mjs');
    const outDir = path.join(tempRoot, 'subset-out');
    const child = spawnSync(process.execPath, [cli, 'build'], {
        input: JSON.stringify({ template: golden(), out_dir: outDir }),
        encoding: 'utf8',
        maxBuffer: 16 * 1024 * 1024
    });
    assert.equal(child.status, 0, child.stderr);
    const result = JSON.parse(child.stdout);
    assert.equal(result.ok, true, JSON.stringify(result.errors));
    const reference = freshDir();
    return buildSite({ template: golden(), out_dir: reference }).then((local) => {
        assert.deepEqual(result.files, local.files, 'the subset produces the same files as the full checkout');
    });
});

test('runtime packaging keeps runtime assets and drops data, refresource, demos, tests and docs', () => {
    assert.equal(isRuntimeComponentFile('form/TextInput/TextInput.js'), true);
    assert.equal(isRuntimeComponentFile('layout/DataTable/DataTable.css'), true);
    assert.equal(isRuntimeComponentFile('vendor/leaflet/images/marker-icon.png'), true);
    assert.equal(isRuntimeComponentFile('data/RegionMap/RegionMap.js'), false);
    assert.equal(isRuntimeComponentFile('refresource/City/a.json'), false);
    assert.equal(isRuntimeComponentFile('common/Badge/demo.html'), false);
    assert.equal(isRuntimeComponentFile('demo-utils.js'), false);
    assert.equal(isRuntimeComponentFile('common/List/List.test.mjs'), false);
    assert.equal(isRuntimeComponentFile('common/TreeList/test-dom.mjs'), false);
    assert.equal(isRuntimeComponentFile('README.md'), false);
    assert.equal(isRuntimeComponentFile('common/icons/download-icons.svg'), false);
    assert.equal(isRuntimeComponentFile('metadata/build-metadata.mjs'), false);

    const sources = listRuntimeSources().map(source => source.path);
    assert.deepEqual(sources, [...sources].sort((a, b) => (a < b ? -1 : a > b ? 1 : 0)));
    assert.ok(sources.includes('runtime/page-generator/FieldResolver.js'));
    assert.ok(!sources.some(source => source.endsWith('.test.mjs')));
});
