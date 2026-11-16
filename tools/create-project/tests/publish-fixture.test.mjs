import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { cpSync, mkdtempSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync, existsSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { verifyConsumerSnapshot } from '../template/scripts/snapshot-integrity.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const scripts = path.resolve(here, '..', 'template', 'scripts');
const node = process.execPath;

function put(root, relative, content) {
    const target = path.join(root, relative);
    mkdirSync(path.dirname(target), { recursive: true });
    writeFileSync(target, content);
}

function fixture() {
    const temp = mkdtempSync(path.join(os.tmpdir(), 'b4a-publish-'));
    const project = path.join(temp, 'consumer');
    const b4a = path.join(temp, 'Bricks4Agent');
    mkdirSync(path.join(project, 'scripts'), { recursive: true });
    mkdirSync(path.join(project, 'src', 'frontend', 'styles'), { recursive: true });
    for (const name of ['ui_components', 'page-generator', 'custom_components']) mkdirSync(path.join(project, 'lib', name), { recursive: true });
    put(project, 'scripts/publish.mjs', readFileSync(path.join(scripts, 'publish.mjs')));
    for (const file of ['b4a-lock.mjs', 'resolve-b4a.mjs', 'snapshot-integrity.mjs', 'verify-sealed.mjs',
        'publish-policy.mjs', 'module-analysis.mjs', 'mechanism.json']) {
        put(project, `scripts/${file}`, readFileSync(path.join(scripts, file)));
    }
    put(project, 'src/frontend/index.html', '<link rel="stylesheet" href="styles/main.css"><script type="module" src="app.js"></script>');
    put(project, 'src/frontend/styles/main.css', 'body{background:url("../../../lib/ui_components/assets/pixel.png")}');
    put(project, 'src/frontend/app.js', "import { chosen } from '../../lib/ui_components/index.js'; window.chosen = chosen;");
    put(project, 'lib/ui_components/index.js', "export { chosen } from './chosen.js'; import './index.css';");
    put(project, 'lib/ui_components/theme.css', '@import "./index.css";');
    put(project, 'lib/ui_components/chosen.js', 'export const chosen = 1;');
    put(project, 'lib/ui_components/index.css', 'body{color:black}');
    put(project, 'lib/ui_components/assets/pixel.png', 'fixture asset');
    put(project, 'lib/ui_components/unused.js', 'export const unused = true;');
    put(project, 'lib/ui_components/loader.js', "export const load = (name) => import('./plugins/' + name + '.js');");
    put(project, 'lib/ui_components/plugins/one.js', 'export const one = 1;');
    // 沒有任何靜態引用的執行期資產(裁剪時必須警告)與一個未使用的元件目錄(不警告)
    put(project, 'lib/ui_components/assets/extra.png', 'fixture extra asset');
    put(project, 'lib/ui_components/maps/region.png', 'fixture map data');
    put(project, 'lib/ui_components/maps/README.md', 'docs are not assets');
    put(project, 'lib/ui_components/component.manifest.json', '{}');
    put(project, 'lib/ui_components/other/Other.js', 'export const other = 1;');
    put(project, 'lib/ui_components/other/other.css', 'a{color:red}');
    put(project, 'lib/page-generator/generator.js', 'export const generator = 1;');
    put(project, 'lib/custom_components/custom.js', 'export const custom = 1;');
    const browser = path.join(b4a, 'packages/javascript/browser');
    for (const name of ['ui_components', 'page-generator', 'custom_components']) {
        cpSync(path.join(project, 'lib', name), path.join(browser, name), { recursive: true });
    }
    put(project, '.b4a-root', b4a);
    return { temp, project };
}

function publish(project, args = []) {
    return spawnSync(node, [path.join(project, 'scripts', 'publish.mjs'), ...args], {
        cwd: project, encoding: 'utf8', env: { ...process.env, B4A_ROOT: path.dirname(project) + path.sep + 'Bricks4Agent' }
    });
}

test('publish keeps the default full snapshot and verifies its sealed output', () => {
    const { temp, project } = fixture();
    try {
        const result = publish(project);
        assert.equal(result.status, 0, result.stderr + result.stdout);
        const dist = path.join(project, 'dist');
        assert.equal(existsSync(path.join(dist, 'lib', 'ui_components', 'unused.js')), true);
        assert.equal(existsSync(path.join(dist, 'lib', 'ui_components', 'maps', 'region.png')), true);
        assert.doesNotMatch(result.stderr + result.stdout, /實驗性|警告/);
        execFileSync(node, [path.join(project, 'scripts', 'verify-sealed.mjs'), dist], { stdio: 'pipe' });
        const verified = verifyConsumerSnapshot({
            browserRoot: path.join(temp, 'Bricks4Agent', 'packages', 'javascript', 'browser'),
            consumerRoot: dist, expectedTree: '(no tree)'
        });
        assert.equal(verified.valid, true, verified.errors.join('; '));
    } finally { rmSync(temp, { recursive: true, force: true }); }
});

test('optional policy retains reachable imports/assets and explicit dynamic candidates; failure preserves prior dist', () => {
    const { temp, project } = fixture();
    try {
        put(project, 'publish-policy.json', JSON.stringify({
            version: 1,
            entrypoints: [],
            keep: ['ui_components/loader.js'],
            dynamicImportKeep: { 'ui_components/loader.js': ['ui_components/plugins'] }
        }));
        let result = publish(project, ['--policy', 'publish-policy.json']);
        assert.equal(result.status, 0, result.stderr + result.stdout);
        const dist = path.join(project, 'dist');
        assert.equal(existsSync(path.join(dist, 'lib', 'ui_components', 'chosen.js')), true);
        assert.equal(existsSync(path.join(dist, 'lib', 'ui_components', 'index.css')), true);
        assert.equal(existsSync(path.join(dist, 'lib', 'ui_components', 'assets', 'pixel.png')), true);
        assert.equal(existsSync(path.join(dist, 'lib', 'ui_components', 'plugins', 'one.js')), true);
        assert.equal(existsSync(path.join(dist, 'lib', 'ui_components', 'unused.js')), false);
        // policy 模式標示為實驗性,並把未被選入的資產印成警告(只警告,不改變選取)
        assert.match(result.stderr, /\[實驗性\]/);
        assert.match(result.stdout, /\[實驗性\]/);
        assert.match(result.stderr, /警告:3 個資產檔/);
        for (const omitted of ['lib/ui_components/assets/extra.png', 'lib/ui_components/maps/region.png', 'lib/ui_components/theme.css']) {
            assert.equal(result.stderr.includes(omitted), true, `${omitted} should be reported`);
        }
        for (const quiet of ['other.css', 'unused.js', 'README.md', 'component.manifest.json']) {
            assert.equal(result.stderr.includes(quiet), false, `${quiet} should not be reported`);
        }
        assert.equal(existsSync(path.join(dist, 'lib', 'ui_components', 'maps', 'region.png')), false);
        execFileSync(node, [path.join(project, 'scripts', 'verify-sealed.mjs'), dist], { stdio: 'pipe' });
        const verified = verifyConsumerSnapshot({
            browserRoot: path.join(temp, 'Bricks4Agent', 'packages', 'javascript', 'browser'),
            consumerRoot: dist, expectedTree: '(no tree)'
        });
        assert.equal(verified.valid, true, verified.errors.join('; '));
        const before = readFileSync(path.join(dist, 'lib', 'SNAPSHOT.json'), 'utf8');
        put(project, 'lib/ui_components/chosen.js', "export const chosen = await import('./' + 'unknown.js');");
        result = publish(project, ['--policy', 'publish-policy.json']);
        assert.notEqual(result.status, 0);
        assert.match(result.stderr, /Unresolved dynamic import/);
        assert.equal(readFileSync(path.join(dist, 'lib', 'SNAPSHOT.json'), 'utf8'), before);
        assert.equal(readdirSync(project).some((name) => name.startsWith('.dist-staging-') || name.startsWith('.dist-backup-')), false);
    } finally { rmSync(temp, { recursive: true, force: true }); }
});

test('verify-sealed keeps accepting classic scripts that are not valid ES modules', () => {
    const temp = mkdtempSync(path.join(os.tmpdir(), 'b4a-publish-'));
    const dist = path.join(temp, 'dist');
    const verify = () => spawnSync(node, [path.join(scripts, 'verify-sealed.mjs'), dist], { encoding: 'utf8' });
    try {
        // sloppy 語法(八進位、with)無法當 ES 模組解析;2.1 的 regex 驗證器接受這種檔案
        put(dist, 'index.html', '<script src="legacy.js"></script>');
        put(dist, 'legacy.js', 'var mode = 010; function read(o) { with (o) { return value; } }\n' +
            "/** @type {import('./types-only.js').Shape} */\nvar lazy = function () { return import('./lazy.js'); };\n");
        put(dist, 'lazy.js', 'export const lazy = 1;');
        let result = verify();
        assert.equal(result.status, 0, result.stderr + result.stdout);

        rmSync(path.join(dist, 'lazy.js'));
        result = verify();
        assert.notEqual(result.status, 0);
        assert.match(result.stderr, /目標不存在.*lazy\.js/);

        put(dist, 'lazy.js', 'export const lazy = 1;');
        put(dist, 'broken.js', 'export const = ;');
        result = verify();
        assert.notEqual(result.status, 0);
        assert.match(result.stderr, /JavaScript 模組解析失敗.*broken\.js/);
    } finally { rmSync(temp, { recursive: true, force: true }); }
});
