import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..', '..');
const uiRoot = path.join(repoRoot, 'packages', 'javascript', 'browser', 'ui_components');
const customComponentsRoot = path.join(repoRoot, 'packages', 'javascript', 'browser', 'custom_components');
const pageGeneratorRoot = path.join(repoRoot, 'packages', 'javascript', 'browser', 'page-generator');
const customStudioRoot = path.join(repoRoot, 'tools', 'custom-component-studio');
const failOnViolations = process.argv.includes('--fail-on-violations');

// 純 Node 遞迴列檔(原用 rg --files;為零外部依賴/跨平台改內建,比照 audit-csp.mjs)
function walk(dir, out = []) {
    for (const ent of readdirSync(dir, { withFileTypes: true })) {
        const p = path.join(dir, ent.name);
        // vendor/ = 第三方原樣封存(leaflet/html2canvas),不受本庫 token 規範
        if (ent.isDirectory()) { if (ent.name !== 'node_modules' && ent.name !== 'vendor') walk(p, out); continue; }
        if (!/\.(js|css)$/.test(ent.name)) continue;
        if (ent.name.endsWith('.bak')) continue;
        out.push(p);
    }
    return out;
}

const files = [
    ...walk(uiRoot),
    ...walk(customComponentsRoot),
    ...walk(pageGeneratorRoot),
    ...walk(customStudioRoot),
]
    .filter((filePath) => {
        const normalized = filePath.replaceAll('\\', '/');
        return !normalized.includes('/page-generator/examples/')
            && !normalized.endsWith('/theme.css')
            && !normalized.endsWith('/themes/default.css')
            // 顏色資料來源檔(調色盤最近色計算需要 hex 數值,非樣式),比照 theme.css 排除
            && !normalized.endsWith('/editor/richtext-palette.js')
            // palette.css 為自動生成的色階 token 檔(色彩系統 foundation),比照 theme.css 排除
            && !normalized.endsWith('/palette.css')
            // theme-bus 為 FALLBACK_PAINT 唯一定義點(foundation 層;元件端禁散裝 hex 回退)
            && !normalized.endsWith('/utils/theme-bus.js');
    });

const rules = [
    {
        id: 'hex-color',
        description: 'Hardcoded hex colors should map to --cl-* tokens.',
        pattern: /#[0-9A-Fa-f]{3,8}\b/g,
        // 亮度自適應「文字對比遮罩」常數:依填色亮度擇黑/白半透明,屬物理對比而非主題色
        //(Heatmap/Pie/Sunburst/Flame/Timeline 的標籤上色邏輯),准予直寫。
        allow: new Set(['#00000099', '#ffffffcc', '#000000aa', '#ffffffdd'])
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
        id: 'font-size-literal',
        description: 'Use a --cl-font-size-* token instead of pixel font sizes.',
        pattern: /\bfont-size\s*:\s*\d+(?:\.\d+)?px\b/g
    },
    {
        id: 'named-color',
        description: 'Use a --cl-* color token instead of named CSS colors.',
        pattern: /\b(?:color|background(?:-color)?|border(?:-[a-z-]+)?-color)\s*:\s*(?:white|black|red|blue|green|gr[ae]y)\b/gi
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

console.log(`Scanned ${files.length} UI/runtime source files.`);
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
