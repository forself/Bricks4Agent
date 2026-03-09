/**
 * Demo 深色切換批次注入腳本
 * 為所有 demo.html 加入 theme.css 引用和深色切換按鈕
 *
 * 用法: node tools/fix_demo_theme.js [--dry-run]
 */
import { readFileSync, writeFileSync, readdirSync, statSync } from 'fs';
import { join, relative, dirname } from 'path';
import { fileURLToPath } from 'url';

const DRY_RUN = process.argv.includes('--dry-run');
const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const UI_COMP_DIR = join(ROOT, 'packages', 'javascript', 'browser', 'ui_components');
const DEMOS_DIR = join(ROOT, 'packages', 'javascript', 'browser', 'demos');

// 硬編碼 CSS 色值替換（<style> 區塊內）
const CSS_RULES = [
    [/background:\s*#f5f5f5/gi, 'background: var(--cl-bg-secondary)'],
    [/background:\s*#f8f9fa/gi, 'background: var(--cl-bg-tertiary)'],
    [/background:\s*white\b/gi, 'background: var(--cl-bg)'],
    [/background:\s*#fff\b(?!f)/gi, 'background: var(--cl-bg)'],
    [/background:\s*#ffffff/gi, 'background: var(--cl-bg)'],
    [/background-color:\s*#f5f5f5/gi, 'background-color: var(--cl-bg-secondary)'],
    [/background-color:\s*white\b/gi, 'background-color: var(--cl-bg)'],
    [/background-color:\s*#fff\b(?!f)/gi, 'background-color: var(--cl-bg)'],
    [/color:\s*#333(?:333)?(?=[;\s}])/gi, 'color: var(--cl-text)'],
    [/color:\s*#666(?:666)?(?=[;\s}])/gi, 'color: var(--cl-text-secondary)'],
    [/color:\s*#999(?:999)?(?=[;\s}])/gi, 'color: var(--cl-text-muted)'],
    [/border(?:-color)?:\s*1px solid #ddd\b/gi, 'border: 1px solid var(--cl-border)'],
    [/border(?:-color)?:\s*1px solid #eee\b/gi, 'border: 1px solid var(--cl-border-light)'],
    [/border-bottom:\s*1px solid #ddd\b/gi, 'border-bottom: 1px solid var(--cl-border)'],
    [/border-bottom:\s*1px solid #eee\b/gi, 'border-bottom: 1px solid var(--cl-border-light)'],
    [/solid #ddd\b/gi, 'solid var(--cl-border)'],
    [/solid #eee\b/gi, 'solid var(--cl-border-light)'],
];

function collectHtmlFiles(dir) {
    const files = [];
    try {
        for (const entry of readdirSync(dir)) {
            const full = join(dir, entry);
            try {
                const stat = statSync(full);
                if (stat.isDirectory() && !entry.startsWith('.') && entry !== 'node_modules' && entry !== 'backup_alert_fix') {
                    files.push(...collectHtmlFiles(full));
                } else if (stat.isFile() && entry.endsWith('.html') && entry.includes('demo')) {
                    files.push(full);
                }
            } catch { /* skip */ }
        }
    } catch { /* skip */ }
    return files;
}

function calcRelativePath(from, to) {
    // 計算從 demo.html 到 ui_components/ 的相對路徑
    const fromDir = dirname(from);
    let rel = relative(fromDir, to).replace(/\\/g, '/');
    if (!rel.startsWith('.')) rel = './' + rel;
    return rel;
}

function processDemo(filePath) {
    let content = readFileSync(filePath, 'utf-8');
    const original = content;
    const changes = [];

    const themeCSS = join(UI_COMP_DIR, 'theme.css');
    const demoUtils = join(UI_COMP_DIR, 'demo-utils.js');
    const themeRel = calcRelativePath(filePath, themeCSS);
    const utilsRel = calcRelativePath(filePath, demoUtils);

    // 1. 加入 theme.css link（如果還沒有）
    if (!content.includes('theme.css')) {
        const themeLink = `    <link rel="stylesheet" href="${themeRel}">`;
        if (content.includes('</head>')) {
            content = content.replace('</head>', `${themeLink}\n</head>`);
            changes.push('加入 theme.css 引用');
        }
    }

    // 2. 加入深色切換腳本（如果還沒有）
    if (!content.includes('demo-utils.js') && !content.includes('createThemeToggle')) {
        const toggleScript = `    <script type="module">\n        import { createThemeToggle } from '${utilsRel}';\n        createThemeToggle();\n    </script>`;
        if (content.includes('</body>')) {
            content = content.replace('</body>', `${toggleScript}\n</body>`);
            changes.push('加入深色切換按鈕');
        }
    }

    // 3. 替換 <style> 中的硬編碼顏色
    content = content.replace(/<style[^>]*>([\s\S]*?)<\/style>/gi, (match, css) => {
        let newCss = css;
        for (const [pattern, replacement] of CSS_RULES) {
            pattern.lastIndex = 0;
            const before = newCss;
            newCss = newCss.replace(pattern, replacement);
            if (newCss !== before) {
                changes.push(`CSS 替換: ${replacement}`);
            }
        }
        return match.replace(css, newCss);
    });

    const modified = content !== original;
    if (modified && !DRY_RUN) {
        writeFileSync(filePath, content, 'utf-8');
    }

    return { filePath, changes, modified };
}

// 主程式
console.log(`\n${'='.repeat(60)}`);
console.log(`Demo 深色切換批次注入${DRY_RUN ? ' (DRY RUN)' : ''}`);
console.log(`${'='.repeat(60)}\n`);

// 收集所有 demo.html
const demos = [
    ...collectHtmlFiles(UI_COMP_DIR),
    ...collectHtmlFiles(DEMOS_DIR),
];

// 也收集非 "demo" 命名但在 demo 相關目錄的 HTML
function collectAllHtml(dir) {
    const files = [];
    try {
        for (const entry of readdirSync(dir)) {
            const full = join(dir, entry);
            try {
                const stat = statSync(full);
                if (stat.isDirectory() && !entry.startsWith('.') && entry !== 'node_modules' && entry !== 'backup_alert_fix') {
                    files.push(...collectAllHtml(full));
                } else if (stat.isFile() && entry.endsWith('.html')) {
                    files.push(full);
                }
            } catch { /* skip */ }
        }
    } catch { /* skip */ }
    return files;
}

const allDemos = [...new Set([...demos, ...collectAllHtml(DEMOS_DIR)])];

let totalFiles = 0;
let totalChanges = 0;

for (const file of allDemos) {
    const result = processDemo(file);
    if (result.modified) {
        totalFiles++;
        totalChanges += result.changes.length;
        console.log(`📝 ${relative(ROOT, result.filePath)} (${result.changes.length} 變更)`);
        for (const c of result.changes) {
            console.log(`   + ${c}`);
        }
    }
}

console.log(`\n${'='.repeat(60)}`);
console.log(`✅ 完成：${totalFiles} 個檔案、${totalChanges} 處變更`);
if (DRY_RUN) console.log('⚠️  DRY RUN 模式，未實際修改');
console.log(`${'='.repeat(60)}\n`);
