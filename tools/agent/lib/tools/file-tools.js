'use strict';

const fs = require('fs');
const path = require('path');
const { resolveSafePath, needsConfirmation, promptConfirmation } = require('../safety');
const { formatBytes, logTool } = require('../utils');

/**
 * 讀取檔案內容
 */
async function readFile(args, context) {
    const { path: filePath, offset = 0, limit = 500 } = args;
    const safePath = resolveSafePath(filePath, context.projectRoot);

    logTool('read_file', safePath);

    const stat = fs.statSync(safePath);
    if (stat.isDirectory()) {
        return `錯誤: ${filePath} 是目錄，請使用 list_directory`;
    }

    const content = fs.readFileSync(safePath, 'utf8');
    const lines = content.split('\n');
    const totalLines = lines.length;

    const start = Math.max(0, offset);
    const end = Math.min(totalLines, start + limit);
    const selected = lines.slice(start, end);

    // 加行號
    const numbered = selected.map((line, i) => `${start + i + 1}: ${line}`).join('\n');

    let header = `[${path.basename(safePath)}] ${formatBytes(stat.size)}, ${totalLines} 行`;
    if (start > 0 || end < totalLines) {
        header += ` (顯示 ${start + 1}-${end} 行)`;
    }

    return `${header}\n${numbered}`;
}

/**
 * 寫入檔案
 */
async function writeFile(args, context) {
    const { path: filePath, content, mode = 'rewrite' } = args;
    const safePath = resolveSafePath(filePath, context.projectRoot);

    logTool('write_file', `${safePath} (${mode})`);

    // 確認提示
    if (!context.noConfirm) {
        const confirmMsg = needsConfirmation('write_file', { path: safePath });
        if (confirmMsg) {
            const ok = await promptConfirmation(confirmMsg);
            if (!ok) return '操作已取消';
        }
    }

    // 建立目錄
    const dir = path.dirname(safePath);
    fs.mkdirSync(dir, { recursive: true });

    if (mode === 'append') {
        fs.appendFileSync(safePath, content, 'utf8');
    } else {
        fs.writeFileSync(safePath, content, 'utf8');
    }

    const stat = fs.statSync(safePath);
    return `✓ 已寫入 ${safePath} (${formatBytes(stat.size)})`;
}

/**
 * 搜尋檔名
 */
async function searchFiles(args, context) {
    const { pattern, directory = '.' } = args;
    const safePath = resolveSafePath(directory, context.projectRoot);

    logTool('search_files', `"${pattern}" in ${safePath}`);

    const results = [];
    const MAX_RESULTS = 100;
    const SKIP_DIRS = new Set(['node_modules', '.git', 'bin', 'obj', 'dist', 'test-results', '.claude']);

    // 將 glob 轉為正規表達式
    const regexStr = pattern
        .replace(/\./g, '\\.')
        .replace(/\*\*/g, '{{GLOBSTAR}}')
        .replace(/\*/g, '[^/\\\\]*')
        .replace(/\?/g, '.')
        .replace(/\{\{GLOBSTAR\}\}/g, '.*');
    const regex = new RegExp(regexStr, 'i');

    function walk(dir, depth = 0) {
        if (results.length >= MAX_RESULTS || depth > 10) return;

        let entries;
        try { entries = fs.readdirSync(dir, { withFileTypes: true }); }
        catch (_) { return; }

        for (const entry of entries) {
            if (results.length >= MAX_RESULTS) break;
            if (SKIP_DIRS.has(entry.name)) continue;

            const fullPath = path.join(dir, entry.name);
            if (entry.isDirectory()) {
                walk(fullPath, depth + 1);
            } else if (regex.test(entry.name)) {
                results.push(path.relative(context.projectRoot, fullPath));
            }
        }
    }

    walk(safePath);

    if (results.length === 0) return `未找到符合 "${pattern}" 的檔案`;
    let output = `找到 ${results.length} 個檔案${results.length >= MAX_RESULTS ? '（已達上限）' : ''}:\n`;
    output += results.join('\n');
    return output;
}

/**
 * 搜尋檔案內容
 */
async function searchContent(args, context) {
    const { pattern, directory = '.', file_pattern } = args;
    const safePath = resolveSafePath(directory, context.projectRoot);

    logTool('search_content', `"${pattern}" in ${safePath}`);

    const results = [];
    const MAX_RESULTS = 50;
    const MAX_LINES_PER_FILE = 10000;
    const SKIP_DIRS = new Set(['node_modules', '.git', 'bin', 'obj', 'dist', 'test-results', '.claude']);

    let regex;
    try { regex = new RegExp(pattern, 'i'); }
    catch (_) { regex = new RegExp(pattern.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'i'); }

    let fileRegex = null;
    if (file_pattern) {
        const fp = file_pattern.replace(/\./g, '\\.').replace(/\*/g, '.*');
        fileRegex = new RegExp(fp, 'i');
    }

    function walk(dir, depth = 0) {
        if (results.length >= MAX_RESULTS || depth > 8) return;

        let entries;
        try { entries = fs.readdirSync(dir, { withFileTypes: true }); }
        catch (_) { return; }

        for (const entry of entries) {
            if (results.length >= MAX_RESULTS) break;
            if (SKIP_DIRS.has(entry.name)) continue;

            const fullPath = path.join(dir, entry.name);
            if (entry.isDirectory()) {
                walk(fullPath, depth + 1);
            } else {
                if (fileRegex && !fileRegex.test(entry.name)) continue;

                // 跳過二進位檔
                try {
                    const buf = Buffer.alloc(512);
                    const fd = fs.openSync(fullPath, 'r');
                    const bytesRead = fs.readSync(fd, buf, 0, 512, 0);
                    fs.closeSync(fd);
                    if (buf.slice(0, bytesRead).includes(0)) continue; // 含 null byte → 二進位
                } catch (_) { continue; }

                try {
                    const content = fs.readFileSync(fullPath, 'utf8');
                    const lines = content.split('\n');
                    const lineCount = Math.min(lines.length, MAX_LINES_PER_FILE);

                    for (let i = 0; i < lineCount; i++) {
                        if (results.length >= MAX_RESULTS) break;
                        if (regex.test(lines[i])) {
                            const relPath = path.relative(context.projectRoot, fullPath);
                            results.push(`${relPath}:${i + 1}: ${lines[i].trim().slice(0, 200)}`);
                        }
                    }
                } catch (_) { /* 無法讀取 */ }
            }
        }
    }

    walk(safePath);

    if (results.length === 0) return `未找到符合 "${pattern}" 的內容`;
    let output = `找到 ${results.length} 個結果${results.length >= MAX_RESULTS ? '（已達上限）' : ''}:\n`;
    output += results.join('\n');
    return output;
}

module.exports = {
    readFile,
    writeFile,
    searchFiles,
    searchContent,
};
