/**
 * 硬編碼顏色批次替換腳本
 * 將 JS 檔案中的硬編碼顏色值替換為 CSS 變數引用
 *
 * 用法: node tools/fix_hardcoded_colors.js [--dry-run]
 */
import { readFileSync, writeFileSync, readdirSync, statSync } from 'fs';
import { join, relative } from 'path';

const DRY_RUN = process.argv.includes('--dry-run');
const ROOT = process.cwd();

// 替換規則：[pattern, replacement, description]
// 順序重要 — 更具體的規則在前
const RULES = [
    // === 背景色 ===
    [/background:\s*#f5f5f5/gi, 'background: var(--cl-bg-secondary)', '淺灰背景 → 次要背景'],
    [/background:\s*#f8f9fa/gi, 'background: var(--cl-bg-tertiary)', '極淺灰背景 → 三級背景'],
    [/background:\s*#fafafa/gi, 'background: var(--cl-bg-input)', '輸入框背景'],
    [/background:\s*#e3f2fd/gi, 'background: var(--cl-bg-active)', '選中態背景'],
    [/background:\s*white\b/gi, 'background: var(--cl-bg)', '白色背景 → 主背景'],
    [/background:\s*#fff\b(?!f)/gi, 'background: var(--cl-bg)', '#fff 背景 → 主背景'],
    [/background:\s*#ffffff/gi, 'background: var(--cl-bg)', '#ffffff 背景 → 主背景'],
    [/background:\s*'white'/gi, "background: 'var(--cl-bg)'", 'JS 物件白色背景'],
    [/background:\s*"white"/gi, 'background: "var(--cl-bg)"', 'JS 物件白色背景'],

    // === 品牌色/語意色背景 ===
    [/background:\s*#2196F3/gi, 'background: var(--cl-primary)', '主色背景'],
    [/background:\s*#1976D2/gi, 'background: var(--cl-primary-dark)', '深主色背景'],
    [/background:\s*#4CAF50/gi, 'background: var(--cl-success)', '成功色背景'],
    [/background:\s*#F44336/gi, 'background: var(--cl-danger)', '危險色背景'],
    [/background:\s*#FF9800/gi, 'background: var(--cl-warning)', '警告色背景'],

    // === JS style 物件中的背景色 ===
    [/background'?\s*:\s*'#2196F3'/gi, "background: 'var(--cl-primary)'", 'JS 主色背景'],
    [/background'?\s*:\s*'#1976D2'/gi, "background: 'var(--cl-primary-dark)'", 'JS 深主色背景'],
    [/background'?\s*:\s*'#4CAF50'/gi, "background: 'var(--cl-success)'", 'JS 成功色背景'],
    [/background'?\s*:\s*'#F44336'/gi, "background: 'var(--cl-danger)'", 'JS 危險色背景'],
    [/background'?\s*:\s*'#FF9800'/gi, "background: 'var(--cl-warning)'", 'JS 警告色背景'],

    // === 文字色 ===
    [/color:\s*#333(?:333)?(?=['";,\s})])/gi, 'color: var(--cl-text)', '主文字色'],
    [/color:\s*'#333(?:333)?'/gi, "color: 'var(--cl-text)'", 'JS 主文字色'],
    [/color:\s*#666(?:666)?(?=['";,\s})])/gi, 'color: var(--cl-text-secondary)', '次要文字色'],
    [/color:\s*'#666(?:666)?'/gi, "color: 'var(--cl-text-secondary)'", 'JS 次要文字色'],
    [/color:\s*#999(?:999)?(?=['";,\s})])/gi, 'color: var(--cl-text-muted)', '淡化文字色'],
    [/color:\s*'#999(?:999)?'/gi, "color: 'var(--cl-text-muted)'", 'JS 淡化文字色'],
    [/color:\s*#aaa(?:aaa)?(?=['";,\s})])/gi, 'color: var(--cl-text-muted)', '淡化文字色'],

    // === 語意色文字 ===
    [/color:\s*#2196F3/gi, 'color: var(--cl-primary)', '主色文字'],
    [/color:\s*'#2196F3'/gi, "color: 'var(--cl-primary)'", 'JS 主色文字'],
    [/color:\s*#4CAF50/gi, 'color: var(--cl-success)', '成功色文字'],
    [/color:\s*'#4CAF50'/gi, "color: 'var(--cl-success)'", 'JS 成功色文字'],
    [/color:\s*#F44336/gi, 'color: var(--cl-danger)', '危險色文字'],
    [/color:\s*'#F44336'/gi, "color: 'var(--cl-danger)'", 'JS 危險色文字'],
    [/color:\s*#FF9800/gi, 'color: var(--cl-warning)', '警告色文字'],
    [/color:\s*'#FF9800'/gi, "color: 'var(--cl-warning)'", 'JS 警告色文字'],
    [/color:\s*#9E9E9E/gi, 'color: var(--cl-grey)', '灰色'],

    // === 邊框色 ===
    [/border(?:-color)?:\s*1px solid #ddd\b/gi, 'border: 1px solid var(--cl-border)', '邊框色'],
    [/border(?:-color)?:\s*1px solid #dee2e6/gi, 'border: 1px solid var(--cl-border-medium)', '中等邊框色'],
    [/border(?:-color)?:\s*1px solid #ccc\b/gi, 'border: 1px solid var(--cl-border-dark)', '深邊框色'],
    [/border(?:-color)?:\s*1px solid #eee\b/gi, 'border: 1px solid var(--cl-border-light)', '淺邊框色'],
    [/border-bottom:\s*1px solid #ddd\b/gi, 'border-bottom: 1px solid var(--cl-border)', '底部邊框'],
    [/border-bottom:\s*1px solid #eee\b/gi, 'border-bottom: 1px solid var(--cl-border-light)', '底部淺邊框'],
    [/border-top:\s*1px solid #ddd\b/gi, 'border-top: 1px solid var(--cl-border)', '頂部邊框'],
    [/solid #ddd\b/gi, 'solid var(--cl-border)', '邊框 #ddd'],
    [/solid #eee\b/gi, 'solid var(--cl-border-light)', '邊框 #eee'],

    // === 反白文字（在有色背景上） ===
    // 注意：僅替換 inline style 中的 color:white，不動 CSS class 中合理的 color: white
    [/color:\s*'white'/gi, "color: 'var(--cl-text-inverse)'", 'JS 反白文字'],
    [/color:\s*"white"/gi, 'color: "var(--cl-text-inverse)"', 'JS 反白文字'],

    // === borderColor 單獨設定 ===
    [/borderColor\s*=\s*'#ddd'/gi, "borderColor = 'var(--cl-border)'", 'JS borderColor'],
    [/borderColor\s*=\s*'#2196F3'/gi, "borderColor = 'var(--cl-primary)'", 'JS borderColor 主色'],
    [/borderColor\s*=\s*'#F44336'/gi, "borderColor = 'var(--cl-danger)'", 'JS borderColor 危險色'],

    // === box-shadow 中的色碼 ===
    [/box-shadow:\s*0 2px 8px rgba\(0,\s*0,\s*0,\s*0\.15\)/gi, 'box-shadow: var(--cl-shadow-md)', '中等陰影'],
];

// 需要跳過的行（Canvas 繪圖相關）
const SKIP_PATTERNS = [
    /ctx\./,          // Canvas 2D context
    /fillStyle/,      // Canvas fill
    /strokeStyle/,    // Canvas stroke
    /fillRect/,       // Canvas rect fill
    /clearRect/,      // Canvas clear
    /\.getContext/,   // Canvas context
    /drawImage/,      // Canvas draw
    /createLinearGradient/, // Canvas gradient
    /createRadialGradient/,
];

function shouldSkipLine(line) {
    return SKIP_PATTERNS.some(p => p.test(line));
}

function collectFiles(dir, ext = '.js') {
    const files = [];
    try {
        const entries = readdirSync(dir);
        for (const entry of entries) {
            const full = join(dir, entry);
            try {
                const stat = statSync(full);
                if (stat.isDirectory() && !entry.startsWith('.') && entry !== 'node_modules') {
                    files.push(...collectFiles(full, ext));
                } else if (stat.isFile() && full.endsWith(ext)) {
                    files.push(full);
                }
            } catch { /* skip */ }
        }
    } catch { /* skip */ }
    return files;
}

function processFile(filePath) {
    const content = readFileSync(filePath, 'utf-8');
    const lines = content.split('\n');
    const changes = [];
    let modified = false;

    const newLines = lines.map((line, i) => {
        if (shouldSkipLine(line)) return line;

        let newLine = line;
        for (const [pattern, replacement, desc] of RULES) {
            const before = newLine;
            // 重置 regex lastIndex
            pattern.lastIndex = 0;
            newLine = newLine.replace(pattern, replacement);
            if (newLine !== before) {
                changes.push({
                    line: i + 1,
                    before: before.trim(),
                    after: newLine.trim(),
                    rule: desc
                });
                modified = true;
            }
        }
        return newLine;
    });

    if (modified && !DRY_RUN) {
        writeFileSync(filePath, newLines.join('\n'), 'utf-8');
    }

    return { filePath, changes, modified };
}

// 主程式
console.log(`\n${'='.repeat(60)}`);
console.log(`硬編碼顏色批次替換${DRY_RUN ? ' (DRY RUN - 不會實際修改)' : ''}`);
console.log(`${'='.repeat(60)}\n`);

const dirs = [
    join(ROOT, 'packages', 'javascript', 'browser'),
];

let totalFiles = 0;
let totalChanges = 0;

for (const dir of dirs) {
    const files = collectFiles(dir);
    for (const file of files) {
        const result = processFile(file);
        if (result.modified) {
            totalFiles++;
            totalChanges += result.changes.length;
            const rel = relative(ROOT, result.filePath);
            console.log(`\n📝 ${rel} (${result.changes.length} 處替換)`);
            for (const c of result.changes) {
                console.log(`   L${c.line}: ${c.rule}`);
                console.log(`   - ${c.before}`);
                console.log(`   + ${c.after}`);
            }
        }
    }
}

console.log(`\n${'='.repeat(60)}`);
console.log(`✅ 完成：${totalFiles} 個檔案、${totalChanges} 處替換`);
if (DRY_RUN) console.log('⚠️  DRY RUN 模式，未實際修改任何檔案');
console.log(`${'='.repeat(60)}\n`);
