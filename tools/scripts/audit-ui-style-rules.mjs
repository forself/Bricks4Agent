import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..', '..');
const uiRoot = path.join(repoRoot, 'packages', 'javascript', 'browser', 'ui_components');
const failOnViolations = process.argv.includes('--fail-on-violations');

const rgArgs = [
    '--files',
    uiRoot,
    '--glob',
    '*.js',
    '--glob',
    '*.css',
    '--glob',
    '!**/*.bak',
    '--glob',
    '!**/README.md',
    '--glob',
    '!**/STYLE_CONVENTION.md'
];

const files = execFileSync('rg', rgArgs, { cwd: repoRoot, encoding: 'utf8' })
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
    .filter((filePath) => {
        const normalized = filePath.replaceAll('\\', '/');
        return !normalized.endsWith('/theme.css')
            && !normalized.endsWith('/themes/default.css');
    });

const rules = [
    {
        id: 'hex-color',
        description: 'Hardcoded hex colors should map to --cl-* tokens.',
        pattern: /#[0-9A-Fa-f]{3,8}\b/g
    },
    {
        id: 'rgba-color',
        description: 'Direct rgba()/rgb() use should be replaced by shared tokens unless intentionally exempt.',
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
    const absolutePath = path.isAbsolute(filePath) ? filePath : path.join(repoRoot, filePath);
    const source = readFileSync(absolutePath, 'utf8');

    for (const rule of rules) {
        const matches = Array.from(source.matchAll(rule.pattern))
            .map((match) => match[0])
            .filter((match) => !rule.allow?.has(match))
            .filter((match) => !(rule.id === 'rgba-color' && match.includes('var(--cl-')))
            .filter((match) => !(rule.id === 'shadow-radius-literal' && match.includes('var(--cl-')));

        if (matches.length === 0) {
            continue;
        }

        findings.push({
            filePath: path.relative(repoRoot, absolutePath).replaceAll('\\', '/'),
            ruleId: rule.id,
            description: rule.description,
            count: matches.length,
            samples: [...new Set(matches)].slice(0, 3)
        });
    }
}

const totalViolations = findings.reduce((sum, finding) => sum + finding.count, 0);
const filesWithViolations = new Set(findings.map((finding) => finding.filePath)).size;

console.log(`Scanned ${files.length} UI component source files.`);
console.log(`Found ${totalViolations} style-rule hits across ${filesWithViolations} files.`);

if (findings.length > 0) {
    console.log('');
    console.log('Top files:');
    findings
        .sort((a, b) => b.count - a.count || a.filePath.localeCompare(b.filePath))
        .slice(0, 20)
        .forEach((finding) => {
            console.log(`- ${finding.filePath} :: ${finding.ruleId} (${finding.count}) :: ${finding.samples.join(', ')}`);
        });
}

if (failOnViolations && totalViolations > 0) {
    process.exitCode = 1;
}
