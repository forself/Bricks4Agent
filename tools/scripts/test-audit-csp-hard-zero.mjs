// Negative regression test for the shipped templates (SPA template and the
// definition-site shell): a single CSP and SVG hit must fail, even if someone
// accidentally writes the SVG into the baseline.
// Run serially: this test briefly mutates the scanned roots and baseline, then
// restores them in finally.
import { existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const baseline = path.join(repo, 'tools', 'scripts', 'svg-baseline.json');
const originalBaseline = readFileSync(baseline, 'utf8');

const scenarios = [
    { name: 'shipped SPA template', dir: ['templates', 'spa', 'frontend'] },
    { name: 'definition-site shell', dir: ['templates', 'definition-site'] }
];

function runScenario({ name, dir }) {
    const relativeFixture = [...dir, '.__audit-csp-template-negative-test.js'].join('/');
    const fixture = path.join(repo, ...dir, '.__audit-csp-template-negative-test.js');
    const htmlFixture = path.join(repo, ...dir, '.__audit-csp-template-negative-test.html');
    try {
        writeFileSync(fixture, `document.createElement('style');\nexport const forbidden = '<svg></svg>';\n`);
        writeFileSync(htmlFixture, '<p style="color:red">x</p>\n');
        writeFileSync(baseline, JSON.stringify({ [relativeFixture]: 1 }, null, 2) + '\n');

        const result = spawnSync(process.execPath, ['tools/scripts/audit-csp.mjs', '--quiet'], {
            cwd: repo,
            encoding: 'utf8'
        });
        if (
            result.status === 0 ||
            !result.stdout.includes('A. <style> 元素注入: 1 檔 / 1 處') ||
            !result.stdout.includes('I. HTML literal style= attribute: 1 檔 / 1 處') ||
            !result.stdout.includes('SVG 硬零違規')
        ) {
            process.stderr.write(result.stdout);
            process.stderr.write(result.stderr);
            throw new Error(`audit-csp did not reject CSP/SVG violations in the ${name}`);
        }
        console.log(`audit-csp ${name} CSP/SVG hard-zero regression: PASS`);
    } finally {
        if (existsSync(fixture)) rmSync(fixture);
        if (existsSync(htmlFixture)) rmSync(htmlFixture);
        writeFileSync(baseline, originalBaseline);
    }
}

for (const scenario of scenarios) runScenario(scenario);
