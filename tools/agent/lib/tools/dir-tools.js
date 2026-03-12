'use strict';

const fs = require('fs');
const path = require('path');
const { resolveSafePath } = require('../safety');
const { logTool } = require('../utils');

const SKIP_DIRS = new Set(['node_modules', '.git', 'bin', 'obj', 'dist', 'test-results', '.next', '__pycache__']);

/**
 * 列出目錄內容
 */
async function listDirectory(args, context) {
    const { path: dirPath = '.', depth = 2 } = args;
    const safePath = resolveSafePath(dirPath, context.projectRoot);

    logTool('list_directory', `${safePath} (depth=${depth})`);

    const results = [];
    const MAX_ITEMS_PER_DIR = 100;

    function walk(dir, currentDepth, prefix) {
        if (currentDepth > depth) return;

        let entries;
        try {
            entries = fs.readdirSync(dir, { withFileTypes: true });
        } catch (e) {
            results.push(`${prefix}[DENIED] ${path.basename(dir)}`);
            return;
        }

        // 排序：目錄在前，檔案在後，各自按名稱排序
        entries.sort((a, b) => {
            if (a.isDirectory() && !b.isDirectory()) return -1;
            if (!a.isDirectory() && b.isDirectory()) return 1;
            return a.name.localeCompare(b.name);
        });

        let count = 0;
        const total = entries.length;

        for (const entry of entries) {
            if (count >= MAX_ITEMS_PER_DIR && currentDepth > 0) {
                results.push(`${prefix}  ... 還有 ${total - count} 項`);
                break;
            }

            if (SKIP_DIRS.has(entry.name) && entry.isDirectory()) {
                results.push(`${prefix}[DIR]  ${entry.name}/ (已跳過)`);
                count++;
                continue;
            }

            const relPath = path.relative(context.projectRoot, path.join(dir, entry.name));

            if (entry.isDirectory()) {
                results.push(`[DIR]  ${relPath}/`);
                walk(path.join(dir, entry.name), currentDepth + 1, prefix);
            } else {
                results.push(`[FILE] ${relPath}`);
            }
            count++;
        }
    }

    walk(safePath, 0, '');

    if (results.length === 0) return `目錄為空: ${dirPath}`;
    return results.join('\n');
}

module.exports = { listDirectory };
