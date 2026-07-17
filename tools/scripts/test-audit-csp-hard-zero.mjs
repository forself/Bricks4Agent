// Negative regression test for audit-csp G: a single runtime SVG hit must fail,
// even if someone accidentally writes it into svg-baseline.json.
// Run serially: this test briefly mutates the scanned root and baseline, then
// restores both in finally.
import { existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const fixture = path.join(
    repo,
    'packages',
    'javascript',
    'browser',
    'ui_components',
    '.__audit-csp-svg-negative-test.js'
);
const baseline = path.join(repo, 'tools', 'scripts', 'svg-baseline.json');
const originalBaseline = readFileSync(baseline, 'utf8');

try {
    writeFileSync(fixture, `export const forbidden = '<svg></svg>';\n`);
    writeFileSync(baseline, JSON.stringify({
        'packages/javascript/browser/ui_components/.__audit-csp-svg-negative-test.js': 1
    }, null, 2) + '\n');

    const result = spawnSync(process.execPath, ['tools/scripts/audit-csp.mjs', '--quiet'], {
        cwd: repo,
        encoding: 'utf8'
    });
    if (result.status === 0 || !result.stdout.includes('SVG 硬零違規 1 處')) {
        process.stderr.write(result.stdout);
        process.stderr.write(result.stderr);
        throw new Error('audit-csp did not reject the baseline-listed SVG fixture');
    }
    console.log('audit-csp G hard-zero negative regression: PASS');
} finally {
    if (existsSync(fixture)) rmSync(fixture);
    writeFileSync(baseline, originalBaseline);
}
