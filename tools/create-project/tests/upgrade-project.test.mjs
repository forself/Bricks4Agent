import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const upgradeTool = path.resolve(here, '..', 'upgrade-project.mjs');
const templateScripts = path.resolve(here, '..', 'template', 'scripts');
const node = process.execPath;

function upgrade(dest, args = []) {
    return spawnSync(node, [upgradeTool, '--dest', dest, ...args], { encoding: 'utf8' });
}

// 專案只有空的 scripts/(模擬 2.0 之前、缺少後來新增檔案的舊專案)
function legacyProject(prefix = 'b4a-upgrade-test-') {
    const root = mkdtempSync(path.join(os.tmpdir(), prefix));
    mkdirSync(path.join(root, 'scripts'));
    return root;
}

function localImports(file) {
    const parsed = spawnSync(node, ['--experimental-vm-modules', path.join(templateScripts, 'module-analysis.mjs'), file],
        { encoding: 'utf8' });
    assert.equal(parsed.status, 0, parsed.stderr);
    const analysis = JSON.parse(parsed.stdout);
    return [...analysis.imports, ...analysis.dynamicImports].filter((ref) => ref.startsWith('./') || ref.startsWith('../'));
}

test('upgrade delivers every template mechanism script and the result is closed under local imports', () => {
    const project = legacyProject();
    try {
        const result = upgrade(project);
        assert.equal(result.status, 0, result.stderr + result.stdout);

        const scripts = path.join(project, 'scripts');
        const delivered = readdirSync(scripts).filter((name) => name.endsWith('.mjs')).sort();
        const expected = readdirSync(templateScripts).filter((name) => name.endsWith('.mjs')).sort();
        assert.deepEqual(delivered, expected, 'MECHANISM_FILES must list every template/scripts/*.mjs');

        for (const name of delivered) {
            assert.equal(readFileSync(path.join(scripts, name), 'utf8'), readFileSync(path.join(templateScripts, name), 'utf8'));
            for (const ref of localImports(path.join(scripts, name))) {
                assert.equal(existsSync(path.resolve(scripts, ref)), true, `scripts/${name} imports ${ref}, which the upgrade did not deliver`);
            }
        }

        const templateMeta = JSON.parse(readFileSync(path.join(templateScripts, 'mechanism.json'), 'utf8'));
        const meta = JSON.parse(readFileSync(path.join(scripts, 'mechanism.json'), 'utf8'));
        assert.equal(meta.mechanismVersion, templateMeta.mechanismVersion);
    } finally { rmSync(project, { recursive: true, force: true }); }
});

test('upgraded scripts load: verify-sealed runs against a sealed directory', () => {
    const project = legacyProject();
    try {
        assert.equal(upgrade(project).status, 0);
        const dist = path.join(project, 'dist');
        mkdirSync(dist);
        writeFileSync(path.join(dist, 'index.html'), '<script type="module" src="app.js"></script>');
        writeFileSync(path.join(dist, 'app.js'), "import { value } from './value.js'; window.value = value;");
        writeFileSync(path.join(dist, 'value.js'), 'export const value = 1;');
        const verified = spawnSync(node, [path.join(project, 'scripts', 'verify-sealed.mjs'), dist], { encoding: 'utf8' });
        assert.equal(verified.status, 0, verified.stderr + verified.stdout);
    } finally { rmSync(project, { recursive: true, force: true }); }
});

test('upgrade skips customized files and --dry-run writes nothing', () => {
    const project = legacyProject();
    try {
        const scripts = path.join(project, 'scripts');
        writeFileSync(path.join(scripts, 'dev.mjs'), '// project-specific dev server\n');
        writeFileSync(path.join(scripts, 'mechanism.json'), JSON.stringify({ mechanismVersion: '2.0.0', customized: ['dev.mjs'] }));

        const dryRun = upgrade(project, ['--dry-run']);
        assert.equal(dryRun.status, 0, dryRun.stderr + dryRun.stdout);
        assert.deepEqual(readdirSync(scripts).sort(), ['dev.mjs', 'mechanism.json']);
        assert.equal(JSON.parse(readFileSync(path.join(scripts, 'mechanism.json'), 'utf8')).mechanismVersion, '2.0.0');

        const result = upgrade(project);
        assert.equal(result.status, 0, result.stderr + result.stdout);
        assert.equal(readFileSync(path.join(scripts, 'dev.mjs'), 'utf8'), '// project-specific dev server\n');
        assert.equal(existsSync(path.join(scripts, 'b4a-lock.mjs')), true);
        assert.equal(existsSync(path.join(scripts, 'snapshot-integrity.mjs')), true);
        assert.deepEqual(JSON.parse(readFileSync(path.join(scripts, 'mechanism.json'), 'utf8')).customized, ['dev.mjs']);
    } finally { rmSync(project, { recursive: true, force: true }); }
});
