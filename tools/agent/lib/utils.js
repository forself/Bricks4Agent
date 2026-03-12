'use strict';

const path = require('path');
const fs = require('fs');

// ─── ANSI 顏色（零依賴替代 chalk） ───

const COLORS = {
    reset: '\x1b[0m',
    bold: '\x1b[1m',
    dim: '\x1b[2m',
    red: '\x1b[31m',
    green: '\x1b[32m',
    yellow: '\x1b[33m',
    blue: '\x1b[34m',
    magenta: '\x1b[35m',
    cyan: '\x1b[36m',
    gray: '\x1b[90m',
    white: '\x1b[37m',
};

/**
 * 為文字加上 ANSI 顏色
 * @param {string} text
 * @param {string} color - COLORS 中的顏色名稱
 * @returns {string}
 */
function colorize(text, color) {
    const code = COLORS[color];
    if (!code) return text;
    return `${code}${text}${COLORS.reset}`;
}

/** 粗體文字 */
function bold(text) {
    return `${COLORS.bold}${text}${COLORS.reset}`;
}

// ─── 路徑工具 ───

/**
 * 從 cwd 向上搜尋專案根目錄
 * 尋找標記檔案：.git, package.json, AGENT.md
 * @param {string} [startDir] - 起始目錄，預設 process.cwd()
 * @returns {string} 專案根目錄
 */
function resolveProjectRoot(startDir) {
    let dir = startDir || process.cwd();
    const markers = ['.git', 'package.json', 'AGENT.md'];
    const root = path.parse(dir).root;

    for (let i = 0; i < 10; i++) {
        for (const marker of markers) {
            const candidate = path.join(dir, marker);
            try {
                fs.accessSync(candidate);
                return dir;
            } catch (_) {
                // 繼續搜尋
            }
        }
        const parent = path.dirname(dir);
        if (parent === dir || parent === root) break;
        dir = parent;
    }

    // 找不到標記，用 cwd
    return startDir || process.cwd();
}

// ─── 格式化工具 ───

/**
 * 格式化位元組大小
 * @param {number} bytes
 * @returns {string}
 */
function formatBytes(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/**
 * 格式化毫秒為可讀時間
 * @param {number} ms
 * @returns {string}
 */
function formatDuration(ms) {
    if (ms < 1000) return `${ms}ms`;
    if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`;
    const m = Math.floor(ms / 60000);
    const s = Math.round((ms % 60000) / 1000);
    return `${m}m${s}s`;
}

/**
 * 截斷字串
 * @param {string} str
 * @param {number} max
 * @returns {string}
 */
function truncateString(str, max) {
    if (!str || str.length <= max) return str;
    return str.slice(0, max - 3) + '...';
}

/**
 * 印出帶前綴的訊息
 */
function log(msg, color = 'reset') {
    console.log(colorize(msg, color));
}

function logInfo(msg) { log(`ℹ ${msg}`, 'cyan'); }
function logSuccess(msg) { log(`✓ ${msg}`, 'green'); }
function logWarn(msg) { log(`⚠ ${msg}`, 'yellow'); }
function logError(msg) { log(`✗ ${msg}`, 'red'); }
function logTool(name, msg) { log(`  🔧 [${name}] ${msg}`, 'gray'); }

module.exports = {
    COLORS,
    colorize,
    bold,
    resolveProjectRoot,
    formatBytes,
    formatDuration,
    truncateString,
    log,
    logInfo,
    logSuccess,
    logWarn,
    logError,
    logTool,
};
