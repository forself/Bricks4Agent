#!/usr/bin/env node
/**
 * 樣式遷移腳本
 * 將 UI 元件中的硬編碼色碼替換為 CSS 變數引用
 *
 * 使用方式：
 *   node tools/migrate-styles.js          # 預覽模式（不寫入）
 *   node tools/migrate-styles.js --apply  # 實際執行替換
 */
import fs from 'fs';
import path from 'path';

const APPLY = process.argv.includes('--apply');
const UI_DIR = path.resolve('packages/javascript/browser/ui_components');

// 色碼 → CSS 變數映射表
// 順序很重要：長的先匹配，避免 #333 匹配到 #333333 的前綴
const COLOR_MAP = [
    // Primary 系列
    ['#2196F3', 'var(--cl-primary)'],
    ['#2196f3', 'var(--cl-primary)'],
    ['#1976D2', 'var(--cl-primary-dark)'],
    ['#1976d2', 'var(--cl-primary-dark)'],
    ['#1565C0', 'var(--cl-primary-dark)'],
    ['#1565c0', 'var(--cl-primary-dark)'],
    ['#e3f2fd', 'var(--cl-primary-light)'],
    ['#E3F2FD', 'var(--cl-primary-light)'],
    ['#BBDEFB', 'var(--cl-primary-light)'],
    ['#bbdefb', 'var(--cl-primary-light)'],

    // Success 系列
    ['#4CAF50', 'var(--cl-success)'],
    ['#4caf50', 'var(--cl-success)'],
    ['#28a745', 'var(--cl-success)'],
    ['#388E3C', 'var(--cl-success)'],
    ['#388e3c', 'var(--cl-success)'],
    ['#43A047', 'var(--cl-success)'],
    ['#43a047', 'var(--cl-success)'],
    ['#e8f5e9', 'var(--cl-success-light)'],
    ['#E8F5E9', 'var(--cl-success-light)'],

    // Warning 系列
    ['#FF9800', 'var(--cl-warning)'],
    ['#ff9800', 'var(--cl-warning)'],
    ['#ffc107', 'var(--cl-warning)'],
    ['#FFC107', 'var(--cl-warning)'],
    ['#F57C00', 'var(--cl-warning)'],
    ['#f57c00', 'var(--cl-warning)'],
    ['#ffeb3b', 'var(--cl-warning)'],
    ['#FFEB3B', 'var(--cl-warning)'],
    ['#fff3e0', 'var(--cl-warning-light)'],
    ['#FFF3E0', 'var(--cl-warning-light)'],

    // Danger 系列
    ['#F44336', 'var(--cl-danger)'],
    ['#f44336', 'var(--cl-danger)'],
    ['#dc3545', 'var(--cl-danger)'],
    ['#D32F2F', 'var(--cl-danger)'],
    ['#d32f2f', 'var(--cl-danger)'],
    ['#E53935', 'var(--cl-danger)'],
    ['#e53935', 'var(--cl-danger)'],
    ['#c62828', 'var(--cl-danger)'],
    ['#C62828', 'var(--cl-danger)'],
    ['#fdecea', 'var(--cl-danger-light)'],

    // Info
    ['#17a2b8', 'var(--cl-info)'],
    ['#03A9F4', 'var(--cl-info)'],
    ['#03a9f4', 'var(--cl-info)'],

    // Danger 擴充
    ['#E74C3C', 'var(--cl-danger)'],
    ['#e74c3c', 'var(--cl-danger)'],
    ['#ef4444', 'var(--cl-danger)'],
    ['#ff4444', 'var(--cl-danger)'],
    ['#FF5252', 'var(--cl-danger)'],
    ['#FF8A80', 'var(--cl-danger-light)'],
    ['#B71C1C', 'var(--cl-danger-dark)'],
    ['#ffebee', 'var(--cl-bg-danger-light)'],
    ['#FFCDD2', 'var(--cl-bg-danger-lighter)'],
    ['#ffcdd2', 'var(--cl-bg-danger-lighter)'],
    ['#fce4ec', 'var(--cl-bg-danger-light)'],

    // Success 擴充
    ['#2E7D32', 'var(--cl-success-dark)'],
    ['#2e7d32', 'var(--cl-success-dark)'],
    ['#10b981', 'var(--cl-success)'],
    ['#66BB6A', 'var(--cl-success)'],
    ['#8BC34A', 'var(--cl-light-green)'],
    ['#A5D6A7', 'var(--cl-success-light)'],
    ['#50C878', 'var(--cl-success)'],
    ['#1ABC9C', 'var(--cl-teal)'],
    ['#f1f8e9', 'var(--cl-bg-success-light)'],

    // Warning 擴充
    ['#e65100', 'var(--cl-warning-dark)'],
    ['#E65100', 'var(--cl-warning-dark)'],
    ['#f59e0b', 'var(--cl-warning)'],
    ['#F39C12', 'var(--cl-warning)'],
    ['#E67E22', 'var(--cl-warning)'],
    ['#ff9900', 'var(--cl-warning)'],
    ['#e8bb00', 'var(--cl-warning)'],
    ['#FFAB40', 'var(--cl-warning)'],
    ['#FFD180', 'var(--cl-warning-light)'],

    // Purple 系列
    ['#9C27B0', 'var(--cl-purple)'],
    ['#9c27b0', 'var(--cl-purple)'],
    ['#7B1FA2', 'var(--cl-purple-dark)'],
    ['#7b1fa2', 'var(--cl-purple-dark)'],
    ['#673AB7', 'var(--cl-purple)'],
    ['#673ab7', 'var(--cl-purple)'],
    ['#512DA8', 'var(--cl-purple-dark)'],
    ['#512da8', 'var(--cl-purple-dark)'],
    ['#9B59B6', 'var(--cl-purple)'],
    ['#9b59b6', 'var(--cl-purple)'],
    ['#AB47BC', 'var(--cl-purple)'],
    ['#CE93D8', 'var(--cl-purple-light)'],
    ['#8b5cf6', 'var(--cl-purple)'],
    ['#ec4899', 'var(--cl-pink)'],
    ['#c7d2fe', 'var(--cl-purple-light)'],

    // Indigo 系列
    ['#3F51B5', 'var(--cl-indigo)'],
    ['#3f51b5', 'var(--cl-indigo)'],
    ['#303F9F', 'var(--cl-indigo-dark)'],
    ['#303f9f', 'var(--cl-indigo-dark)'],
    ['#5a67d8', 'var(--cl-indigo)'],
    ['#448AFF', 'var(--cl-indigo)'],
    ['#82B1FF', 'var(--cl-indigo)'],

    // Teal / Cyan 系列
    ['#009688', 'var(--cl-teal)'],
    ['#00796B', 'var(--cl-teal-dark)'],
    ['#00BCD4', 'var(--cl-cyan)'],
    ['#00838F', 'var(--cl-cyan-dark)'],
    ['#0097A7', 'var(--cl-cyan-dark)'],
    ['#06b6d4', 'var(--cl-cyan)'],

    // Pink 系列
    ['#E91E63', 'var(--cl-pink)'],
    ['#e91e63', 'var(--cl-pink)'],
    ['#C2185B', 'var(--cl-pink-dark)'],

    // Deep Orange 系列
    ['#FF5722', 'var(--cl-deep-orange)'],
    ['#ff5722', 'var(--cl-deep-orange)'],
    ['#E64A19', 'var(--cl-deep-orange-dark)'],

    // Brown / Blue Grey 系列
    ['#795548', 'var(--cl-brown)'],
    ['#5D4037', 'var(--cl-brown-dark)'],
    ['#607D8B', 'var(--cl-blue-grey)'],
    ['#455A64', 'var(--cl-blue-grey-dark)'],
    ['#78909C', 'var(--cl-blue-grey)'],
    ['#CFD8DC', 'var(--cl-border-muted)'],

    // Grey 系列
    ['#9E9E9E', 'var(--cl-grey)'],
    ['#757575', 'var(--cl-grey-dark)'],
    ['#BDBDBD', 'var(--cl-grey-light)'],
    ['#bdbdbd', 'var(--cl-grey-light)'],
    ['#424242', 'var(--cl-text)'],

    // Office / 品牌色
    ['#2B579A', 'var(--cl-brand-word)'],
    ['#217346', 'var(--cl-brand-excel)'],
    ['#5865f2', 'var(--cl-brand-discord)'],
    ['#0a66c2', 'var(--cl-brand-linkedin)'],

    // 功能色 — 繪圖/圖表
    ['#ff0000', 'var(--cl-canvas-red)'],
    ['#0066ff', 'var(--cl-canvas-blue)'],
    ['#fff9c4', 'var(--cl-canvas-highlight)'],
    ['#3388ff', 'var(--cl-chart-link)'],
    ['#667eea', 'var(--cl-gradient-start)'],
    ['#764ba2', 'var(--cl-gradient-end)'],

    // 額外背景
    ['#fafafa', 'var(--cl-bg-input)'],
    ['#f0f0f0', 'var(--cl-bg-subtle)'],
    ['#f0f7ff', 'var(--cl-bg-info-light)'],
    ['#e7f5ff', 'var(--cl-bg-info-light)'],
    ['#cce8ff', 'var(--cl-bg-info-light)'],
    ['#f0f9ff', 'var(--cl-bg-info-light)'],
    ['#37352f', 'var(--cl-bg-code)'],
    ['#2d2d2d', 'var(--cl-bg-dark)'],
    ['#1e1e1e', 'var(--cl-bg-dark)'],
    ['#fcfcfc', 'var(--cl-bg)'],
    ['#fafbfc', 'var(--cl-bg)'],
    ['#f9fafb', 'var(--cl-bg)'],
    ['#f3f3f3', 'var(--cl-bg-secondary)'],
    ['#f3f4f6', 'var(--cl-bg-secondary)'],
    ['#e8e8e8', 'var(--cl-border-light)'],

    // 額外邊框
    ['#dee2e6', 'var(--cl-border-medium)'],
    ['#e9ecef', 'var(--cl-border-subtle)'],
    ['#e5e7eb', 'var(--cl-border-medium)'],
    ['#cbd5e1', 'var(--cl-border-medium)'],
    ['#d0d0d0', 'var(--cl-border-dark)'],
    ['#c4c6ca', 'var(--cl-border-dark)'],
    ['#e4e6eb', 'var(--cl-border-light)'],

    // 額外文字
    ['#000000', 'var(--cl-text-dark)'],
    ['#495057', 'var(--cl-text-heading)'],
    ['#8c8c8c', 'var(--cl-text-dim)'],
    ['#555555', 'var(--cl-text-secondary)'],
    ['#6c757d', 'var(--cl-text-secondary)'],
    ['#212529', 'var(--cl-text)'],
    ['#2c3e50', 'var(--cl-text)'],
    ['#37373d', 'var(--cl-text)'],
    ['#334155', 'var(--cl-text)'],
    ['#374151', 'var(--cl-text)'],
    ['#4b5563', 'var(--cl-text-heading)'],
    ['#111827', 'var(--cl-text)'],
    ['#64748b', 'var(--cl-text-secondary)'],
    ['#9ca3af', 'var(--cl-text-muted)'],
    ['#94a3b8', 'var(--cl-text-muted)'],

    // 額外 Primary 近似色
    ['#4A90D9', 'var(--cl-primary)'],
    ['#4a90d9', 'var(--cl-primary)'],
    ['#3a7bc8', 'var(--cl-primary-dark)'],
    ['#1971c2', 'var(--cl-primary-dark)'],
    ['#0369a1', 'var(--cl-primary-dark)'],
    ['#0c4a6e', 'var(--cl-primary-dark)'],
    ['#3498DB', 'var(--cl-primary)'],
    ['#3498db', 'var(--cl-primary)'],
    ['#3b82f6', 'var(--cl-primary)'],
    ['#519aba', 'var(--cl-primary)'],
    ['#64B5F6', 'var(--cl-primary-light)'],
    ['#64b5f6', 'var(--cl-primary-light)'],
    ['#00aa00', 'var(--cl-success)'],
    ['#9900ff', 'var(--cl-purple)'],
    ['#84cc16', 'var(--cl-lime)'],

    // 文字色系（6 位完整形式必須在 3 位之前）
    ['#333333', 'var(--cl-text)'],
    ['#1a1a2e', 'var(--cl-text)'],
    ['#666666', 'var(--cl-text-secondary)'],
    ['#888888', 'var(--cl-text-muted)'],
    ['#999999', 'var(--cl-text-placeholder)'],
    ['#aaaaaa', 'var(--cl-text-light)'],
    ['#AAAAAA', 'var(--cl-text-light)'],

    // 背景色系（6 位完整形式）
    ['#ffffff', 'var(--cl-bg)'],
    ['#FFFFFF', 'var(--cl-bg)'],
    ['#f5f5f5', 'var(--cl-bg-secondary)'],
    ['#F5F5F5', 'var(--cl-bg-secondary)'],
    ['#f8f9fa', 'var(--cl-bg-tertiary)'],
    ['#F8F9FA', 'var(--cl-bg-tertiary)'],
    ['#f0f2f5', 'var(--cl-bg-hover)'],
    ['#f9f9f9', 'var(--cl-bg-disabled)'],
    ['#F9F9F9', 'var(--cl-bg-disabled)'],
    ['#2b2b2b', 'var(--cl-bg-dark)'],
    ['#2B2B2B', 'var(--cl-bg-dark)'],

    // 邊框色系（6 位完整形式）
    ['#dddddd', 'var(--cl-border)'],
    ['#DDDDDD', 'var(--cl-border)'],
    ['#e2e8f0', 'var(--cl-border)'],
    ['#E2E8F0', 'var(--cl-border)'],
    ['#eeeeee', 'var(--cl-border-light)'],
    ['#EEEEEE', 'var(--cl-border-light)'],
    ['#e0e0e0', 'var(--cl-border-light)'],
    ['#E0E0E0', 'var(--cl-border-light)'],
    ['#cccccc', 'var(--cl-border-dark)'],
    ['#CCCCCC', 'var(--cl-border-dark)'],
];

// 3 位短色碼映射（需要精確邊界匹配）
const SHORT_COLOR_MAP = [
    ['#333', 'var(--cl-text)'],
    ['#555', 'var(--cl-text-secondary)'],
    ['#666', 'var(--cl-text-secondary)'],
    ['#777', 'var(--cl-text-muted)'],
    ['#888', 'var(--cl-text-muted)'],
    ['#999', 'var(--cl-text-placeholder)'],
    ['#aaa', 'var(--cl-text-light)'],
    ['#AAA', 'var(--cl-text-light)'],
    ['#bbb', 'var(--cl-text-light)'],
    ['#fff', 'var(--cl-bg)'],
    ['#FFF', 'var(--cl-bg)'],
    ['#ddd', 'var(--cl-border)'],
    ['#DDD', 'var(--cl-border)'],
    ['#eee', 'var(--cl-border-light)'],
    ['#EEE', 'var(--cl-border-light)'],
    ['#ccc', 'var(--cl-border-dark)'],
    ['#CCC', 'var(--cl-border-dark)'],
    ['#000', 'var(--cl-text-dark)'],
    ['#444', 'var(--cl-text)'],
    ['#039', 'var(--cl-primary-dark)'],
];

// rgba 替換
const RGBA_MAP = [
    [/rgba\(\s*33\s*,\s*150\s*,\s*243\s*,/g, 'rgba(var(--cl-primary-rgb),'],
    [/rgba\(\s*244\s*,\s*67\s*,\s*54\s*/g, 'rgba(var(--cl-danger-rgb)'],  // 少見，先不處理
];

// 排除的目錄和檔案模式
const EXCLUDE_DIRS = ['backup', 'backup_alert_fix', 'backup_alerts', 'node_modules'];
const EXCLUDE_FILES = ['theme.css'];

// 排除替換的上下文模式（canvas 繪圖指令）
const SKIP_LINE_PATTERNS = [
    /\.fillStyle\s*=/,         // canvas fill
    /\.strokeStyle\s*=/,       // canvas stroke
    /addColorStop/,           // canvas gradient
    /PALETTE/,               // color palette constants（全大寫陣列）
];

function shouldSkipLine(line) {
    return SKIP_LINE_PATTERNS.some(pattern => pattern.test(line));
}

function getAllJsFiles(dir) {
    const files = [];
    const entries = fs.readdirSync(dir, { withFileTypes: true });
    for (const entry of entries) {
        if (EXCLUDE_DIRS.includes(entry.name)) continue;
        if (EXCLUDE_FILES.includes(entry.name)) continue;
        const fullPath = path.join(dir, entry.name);
        if (entry.isDirectory()) {
            files.push(...getAllJsFiles(fullPath));
        } else if (entry.name.endsWith('.js') && !entry.name.endsWith('.min.js')) {
            files.push(fullPath);
        }
    }
    return files;
}

function migrateFile(filePath) {
    const content = fs.readFileSync(filePath, 'utf-8');
    const lines = content.split('\n');
    let totalReplacements = 0;
    const replacementDetails = [];

    const newLines = lines.map((line, lineNum) => {
        // 跳過不應替換的行
        if (shouldSkipLine(line)) return line;
        // 跳過已經使用 CSS 變數的行
        if (line.includes('var(--cl-')) return line;
        // 跳過純註解行
        if (line.trim().startsWith('//') || line.trim().startsWith('*')) return line;

        let newLine = line;

        // 先替換 6 位色碼
        for (const [hex, cssVar] of COLOR_MAP) {
            if (newLine.includes(hex)) {
                const count = newLine.split(hex).length - 1;
                newLine = newLine.split(hex).join(cssVar);
                totalReplacements += count;
                replacementDetails.push({ line: lineNum + 1, from: hex, to: cssVar, count });
            }
        }

        // 再替換 3 位短色碼（需要邊界匹配，避免替換 6 位色碼的前 3 位）
        for (const [hex, cssVar] of SHORT_COLOR_MAP) {
            // 用正則確保 3 位色碼後面不是 hex 字元
            const regex = new RegExp(
                hex.replace('#', '\\#') + '(?![0-9a-fA-F])',
                'g'
            );
            const matches = newLine.match(regex);
            if (matches) {
                newLine = newLine.replace(regex, cssVar);
                totalReplacements += matches.length;
                replacementDetails.push({ line: lineNum + 1, from: hex, to: cssVar, count: matches.length });
            }
        }

        return newLine;
    });

    const newContent = newLines.join('\n');

    if (totalReplacements > 0 && APPLY) {
        fs.writeFileSync(filePath, newContent, 'utf-8');
    }

    return { totalReplacements, replacementDetails, changed: totalReplacements > 0 };
}

// 主程式
console.log(`\n🎨 Bricks4Agent 樣式遷移工具`);
console.log(`模式：${APPLY ? '✅ 實際執行' : '👀 預覽模式（加 --apply 執行）'}\n`);

const files = getAllJsFiles(UI_DIR);
console.log(`掃描 ${files.length} 個 JS 檔案...\n`);

let grandTotal = 0;
let changedFiles = 0;
const report = [];

for (const file of files) {
    const relPath = path.relative(UI_DIR, file);
    const result = migrateFile(file);

    if (result.changed) {
        changedFiles++;
        grandTotal += result.totalReplacements;
        report.push({ file: relPath, ...result });
        console.log(`  📝 ${relPath}: ${result.totalReplacements} 處替換`);
    }
}

console.log(`\n${'='.repeat(50)}`);
console.log(`📊 總計：${changedFiles} 個檔案、${grandTotal} 處替換`);
console.log(`${'='.repeat(50)}\n`);

if (!APPLY && grandTotal > 0) {
    console.log(`💡 確認無誤後，執行 node tools/migrate-styles.js --apply 寫入變更\n`);
}

// 輸出詳細報告
if (process.argv.includes('--verbose')) {
    console.log('\n📋 詳細替換報告：\n');
    for (const item of report) {
        console.log(`\n--- ${item.file} (${item.totalReplacements} 處) ---`);
        for (const detail of item.replacementDetails) {
            console.log(`  L${detail.line}: ${detail.from} → ${detail.to} (×${detail.count})`);
        }
    }
}
