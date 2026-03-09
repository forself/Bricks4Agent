#!/usr/bin/env node
/**
 * 命名色 / rgba 批次修正腳本
 * 將 JS 元件中殘留的 'white'、'black'、'red' 和 rgba() 硬編碼
 * 替換為 CSS 變數引用
 *
 * 使用方式：
 *   node tools/fix-named-colors.js          # 預覽
 *   node tools/fix-named-colors.js --apply  # 執行
 */
import fs from 'fs';
import path from 'path';

const APPLY = process.argv.includes('--apply');
const UI_DIR = path.resolve('packages/javascript/browser/ui_components');

// 排除目錄
const EXCLUDE_DIRS = ['backup', 'node_modules'];

// canvas 上下文行（不替換）
const CANVAS_PATTERNS = [
    /\.fillStyle\s*=/,
    /\.strokeStyle\s*=/,
    /\.shadowColor\s*=/,
    /addColorStop/,
    /ctx\./,
    /tCtx\./,
    /PALETTE/,
    /cursor:.*url\(.*data:/,  // CSS cursor data URI
];

function isCanvasLine(line) {
    return CANVAS_PATTERNS.some(p => p.test(line));
}

function isSvgAttribute(line) {
    // SVG fill="white" stroke="white" 等圖示屬性 — 不替換
    return /\b(fill|stroke)="/.test(line);
}

function isComment(line) {
    const t = line.trim();
    return t.startsWith('//') || t.startsWith('*') || t.startsWith('/*');
}

function isJSDoc(line) {
    const t = line.trim();
    return t.startsWith('*') || t.startsWith('/**') || t.startsWith('*/');
}

// ── 替換規則 ──

/**
 * 'white' 替換邏輯：
 *   背景相關 → var(--cl-bg)
 *   文字/前景色 → var(--cl-text-inverse)
 *   邊框分隔線 → var(--cl-bg)
 */
function replaceWhite(line) {
    if (line.includes('var(--cl-')) return { line, count: 0 };
    if (isCanvasLine(line)) return { line, count: 0 };
    if (isSvgAttribute(line)) return { line, count: 0 };
    if (isComment(line)) return { line, count: 0 };

    let count = 0;
    let result = line;

    // background/bg 上下文 → var(--cl-bg)
    // style.background = 'white'
    result = result.replace(/(\.background\s*=\s*)'white'/g, (m, prefix) => {
        count++; return `${prefix}'var(--cl-bg)'`;
    });
    // background:white / background: white (in cssText)
    result = result.replace(/(background\s*:\s*)white(?=[;'"`\s}])/g, (m, prefix) => {
        count++; return `${prefix}var(--cl-bg)`;
    });
    // background:${...} : 'white' (ternary in template literal)
    result = result.replace(/(:\s*)'white'(\s*\))/g, (m, pre, post) => {
        // 只在有 background/bg 上下文時替換
        if (/background|bg/i.test(line)) {
            count++; return `${pre}'var(--cl-bg)'${post}`;
        }
        return m;
    });

    // color 上下文 → var(--cl-text-inverse)
    // style.color = 'white'
    result = result.replace(/(\.color\s*=\s*)'white'/g, (m, prefix) => {
        count++; return `${prefix}'var(--cl-text-inverse)'`;
    });
    // color:white / color: white (in cssText)
    result = result.replace(/((?:^|[;{])[\s]*color\s*:\s*)white(?=[;'"`\s}])/g, (m, prefix) => {
        count++; return `${prefix}var(--cl-text-inverse)`;
    });

    // border:...white (邊框分隔用) → var(--cl-bg)
    result = result.replace(/(border[^:]*:\s*[^;]*?)white/g, (m, prefix) => {
        if (result !== line) return m; // 避免重複替換
        count++; return `${prefix}var(--cl-bg)`;
    });

    // 剩餘的 'white' — 根據上下文判斷
    if (result.includes("'white'") && count === 0) {
        // 函數參數中的 white（如 btnStyle('var(--cl-primary)', 'white')）
        if (/btnStyle|textColor|fontColor/.test(result)) {
            result = result.replace(/'white'/g, () => { count++; return "'var(--cl-text-inverse)'"; });
        }
        // bgColors fallback
        else if (/\|\|\s*'white'/.test(result)) {
            result = result.replace(/'white'/g, () => { count++; return "'var(--cl-bg)'"; });
        }
        // baseColor = 'white' (text color for filled buttons)
        else if (/baseColor\s*=\s*'white'/.test(result)) {
            result = result.replace(/'white'/g, () => { count++; return "'var(--cl-text-inverse)'"; });
        }
        // 其他 style 上下文中的 white → var(--cl-bg)
        else if (/style|Style|cssText|background|\.bg/.test(result)) {
            result = result.replace(/'white'/g, () => { count++; return "'var(--cl-bg)'"; });
        }
    }

    return { line: result, count };
}

/**
 * 'black' → var(--cl-text-dark)
 */
function replaceBlack(line) {
    if (line.includes('var(--cl-')) return { line, count: 0 };
    if (isCanvasLine(line)) return { line, count: 0 };
    if (isSvgAttribute(line)) return { line, count: 0 };
    if (isComment(line)) return { line, count: 0 };

    let count = 0;
    let result = line;

    result = result.replace(/(\.color\s*=\s*)'black'/g, (m, prefix) => {
        count++; return `${prefix}'var(--cl-text-dark)'`;
    });
    result = result.replace(/(color\s*:\s*)black(?=[;'"`\s}])/g, (m, prefix) => {
        count++; return `${prefix}var(--cl-text-dark)`;
    });

    return { line: result, count };
}

/**
 * 'red' → var(--cl-danger)
 */
function replaceRed(line) {
    if (line.includes('var(--cl-')) return { line, count: 0 };
    if (isCanvasLine(line)) return { line, count: 0 };
    if (isComment(line)) return { line, count: 0 };

    let count = 0;
    let result = line;

    result = result.replace(/(\.color\s*=\s*)'red'/g, (m, prefix) => {
        count++; return `${prefix}'var(--cl-danger)'`;
    });
    result = result.replace(/(color\s*:\s*)red(?=[;'"`\s}])/g, (m, prefix) => {
        count++; return `${prefix}var(--cl-danger)`;
    });

    return { line: result, count };
}

/**
 * rgba() 硬編碼 → CSS 變數版本（僅 UI 元素，跳過 canvas）
 */
function replaceRgba(line) {
    if (line.includes('var(--cl-')) return { line, count: 0 };
    if (isCanvasLine(line)) return { line, count: 0 };
    if (isComment(line)) return { line, count: 0 };

    let count = 0;
    let result = line;

    // rgba(33, 150, 243, x) → primary
    result = result.replace(/rgba\(\s*33\s*,\s*150\s*,\s*243\s*,\s*([\d.]+)\s*\)/g, (m, alpha) => {
        count++; return `rgba(var(--cl-primary-rgb), ${alpha})`;
    });

    // rgba(244, 67, 54, x) → danger
    result = result.replace(/rgba\(\s*244\s*,\s*67\s*,\s*54\s*,\s*([\d.]+)\s*\)/g, (m, alpha) => {
        count++; return `rgba(var(--cl-danger-rgb), ${alpha})`;
    });

    // rgba(76, 175, 80, x) → success
    result = result.replace(/rgba\(\s*76\s*,\s*175\s*,\s*80\s*,\s*([\d.]+)\s*\)/g, (m, alpha) => {
        count++; return `rgba(var(--cl-success-rgb), ${alpha})`;
    });

    // rgba(255, 152, 0, x) → warning
    result = result.replace(/rgba\(\s*255\s*,\s*152\s*,\s*0\s*,\s*([\d.]+)\s*\)/g, (m, alpha) => {
        count++; return `rgba(var(--cl-warning-rgb), ${alpha})`;
    });

    return { line: result, count };
}

// ── 主邏輯 ──

function getAllJsFiles(dir) {
    const files = [];
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        if (EXCLUDE_DIRS.includes(entry.name)) continue;
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) files.push(...getAllJsFiles(full));
        else if (entry.name.endsWith('.js') && !entry.name.endsWith('.min.js'))
            files.push(full);
    }
    return files;
}

function processFile(filePath) {
    const content = fs.readFileSync(filePath, 'utf-8');
    const lines = content.split('\n');
    let total = 0;
    const details = [];

    const newLines = lines.map((line, i) => {
        let current = line;
        for (const fn of [replaceWhite, replaceBlack, replaceRed, replaceRgba]) {
            const r = fn(current);
            if (r.count > 0) {
                total += r.count;
                details.push({ line: i + 1, fn: fn.name, count: r.count });
            }
            current = r.line;
        }
        return current;
    });

    if (total > 0 && APPLY) {
        fs.writeFileSync(filePath, newLines.join('\n'), 'utf-8');
    }

    return { total, details };
}

// ── 執行 ──
console.log(`\n🎨 命名色 / rgba 修正工具`);
console.log(`模式：${APPLY ? '✅ 實際執行' : '👀 預覽模式（加 --apply 執行）'}\n`);

const files = getAllJsFiles(UI_DIR);
console.log(`掃描 ${files.length} 個 JS 檔案...\n`);

let grandTotal = 0;
let changedFiles = 0;

for (const file of files) {
    const rel = path.relative(UI_DIR, file);
    const result = processFile(file);
    if (result.total > 0) {
        changedFiles++;
        grandTotal += result.total;
        console.log(`  📝 ${rel}: ${result.total} 處替換`);
    }
}

console.log(`\n${'='.repeat(50)}`);
console.log(`📊 總計：${changedFiles} 個檔案、${grandTotal} 處替換`);
console.log(`${'='.repeat(50)}\n`);

if (!APPLY && grandTotal > 0) {
    console.log(`💡 確認無誤後，執行 node tools/fix-named-colors.js --apply 寫入\n`);
}
