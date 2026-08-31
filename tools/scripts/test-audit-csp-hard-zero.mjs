// Negative regression test for the shipped SPA template: a single CSP and SVG
// hit must fail, even if someone accidentally writes the SVG into the baseline.
// Run serially: this test briefly mutates the scanned root and baseline, then
// restores both in finally.
import { existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const fixture = path.join(
    repo,
    'templates',
    'spa',
    'frontend',
    '.__audit-csp-template-negative-test.js'
);
const baseline = path.join(repo, 'tools', 'scripts', 'svg-baseline.json');
const originalBaseline = readFileSync(baseline, 'utf8');

try {
    writeFileSync(fixture, `document.createElement('style');\nexport const forbidden = '<svg></svg>';\n`);
    writeFileSync(baseline, JSON.stringify({
        'templates/spa/frontend/.__audit-csp-template-negative-test.js': 1
    }, null, 2) + '\n');

    const result = spawnSync(process.execPath, ['tools/scripts/audit-csp.mjs', '--quiet'], {
        cwd: repo,
        encoding: 'utf8'
    });
    if (
        result.status === 0 ||
        !result.stdout.includes('A. <style> 元素注入: 1 檔 / 1 處') ||
        !result.stdout.includes('SVG 硬零違規')
    ) {
        process.stderr.write(result.stdout);
        process.stderr.write(result.stderr);
        throw new Error('audit-csp did not reject CSP/SVG violations in the shipped template');
    }
    console.log('audit-csp shipped-template CSP/SVG hard-zero regression: PASS');
} finally {
    if (existsSync(fixture)) rmSync(fixture);
    writeFileSync(baseline, originalBaseline);
}
