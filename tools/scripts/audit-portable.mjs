#!/usr/bin/env node
/**
 * Portable style audit — no ripgrep dependency.
 * Mirrors audit-ui-style-rules.mjs logic using Node.js built-in fs.
 */
import { readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..', '..');
const uiRoot = path.join(repoRoot, 'packages', 'javascript', 'browser', 'ui_components');

function walkFiles(dir, predicate, out = []) {
    for (const entry of readdirSync(dir)) {
        const fullPath = path.join(dir, entry);
        const stats = statSync(fullPath);
        if (stats.isDirectory()) {
            walkFiles(fullPath, predicate, out);
        } else if (predicate(fullPath)) {
            out.push(fullPath);
        }
    }
    return out;
}

const files = walkFiles(uiRoot, (fp) => {
    const ext = path.extname(fp);
    if (ext !== '.js' && ext !== '.css') return false;
    if (fp.endsWith('.bak')) return false;
    const norm = fp.replaceAll('\\', '/');
    if (norm.endsWith('/theme.css') || norm.endsWith('/themes/default.css')) return false;
    return true;
});

const rules = [
    {
        id: 'hex-color',
        description: 'Hardcoded hex colors should map to --cl-* tokens.',
        pattern: /#[0-9A-Fa-f]{3,8}\b/g
    },
    {
        id: 'rgba-color',
        description: 'Direct rgba()/rgb() use should be replaced by shared tokens.',
        pattern: /\brgba?\([^)]*\)/g,
        allow: new Set([
            'rgba(0, 0, 0, 0.08)',
            'rgba(255, 255, 255, 0.15)',
            'rgba(255, 255, 255, 0.2)',
            'rgba(255,255,255,0.15)',
            'rgba(255,255,255,0.2)'
        ])
    },
    {
        id: 'font-family',
        description: 'Use var(--cl-font-family) instead of hardcoded font stacks.',
        pattern: /\bfont-family\s*:\s*[^;]*(?:-apple-system|BlinkMacSystemFont|'Segoe UI'|Roboto|'Helvetica Neue'|Arial|sans-serif|Consolas|Monaco|monospace|Microsoft JhengHei|SimSun)/g
    },
    {
        id: 'shadow-radius-literal',
        description: 'Common box-shadow and border-radius values should use theme tokens.',
        pattern: /\bborder-radius\s*:\s*(?:4px|6px|8px|12px|50%)|\bbox-shadow\s*:\s*(?:0\s+1px\s+3px[^;]*|0\s+2px\s+4px[^;]*|0\s+4px\s+8px[^;]*|0\s+4px\s+12px[^;]*|0\s+8px\s+24px[^;]*|0\s+10px\s+25px[^;]*)/g
    }
];

const findings = [];

for (const filePath of files) {
    const source = readFileSync(filePath, 'utf8');
    for (const rule of rules) {
        const regex = new RegExp(rule.pattern.source, rule.pattern.flags);
        const matches = Array.from(source.matchAll(regex))
            .map(m => m[0])
            .filter(m => !rule.allow?.has(m))
            .filter(m => !(rule.id === 'rgba-color' && m.includes('var(--cl-')))
            .filter(m => !(rule.id === 'shadow-radius-literal' && m.includes('var(--cl-')));
        if (matches.length > 0) {
            findings.push({
                filePath: path.relative(repoRoot, filePath).replaceAll('\\', '/'),
                ruleId: rule.id,
                count: matches.length,
                samples: [...new Set(matches)].slice(0, 3)
            });
        }
    }
}

const totalViolations = findings.reduce((s, f) => s + f.count, 0);
const filesWithViolations = new Set(findings.map(f => f.filePath)).size;

console.log(`Scanned ${files.length} UI component source files.`);
console.log(`Found ${totalViolations} style-rule hits across ${filesWithViolations} files.`);

if (findings.length > 0) {
    console.log('');
    console.log('Top files:');
    findings
        .sort((a, b) => b.count - a.count || a.filePath.localeCompare(b.filePath))
        .slice(0, 40)
        .forEach(f => {
            console.log(`- ${f.filePath} :: ${f.ruleId} (${f.count}) :: ${f.samples.join(', ')}`);
        });
}

if (totalViolations > 0) {
    process.exitCode = 1;
    console.log(`\n⚠ ${totalViolations} violations found. Fix or whitelist required.`);
} else {
    console.log('\n✓ All clear — no style violations detected.');
}
