'use strict';

const path = require('path');
const fs = require('fs');
const readline = require('readline');
const { logWarn } = require('./utils');

// ─── 路徑沙箱 ───

/**
 * 解析並驗證路徑在專案根目錄內
 * @param {string} requestedPath - 使用者請求的路徑
 * @param {string} projectRoot - 專案根目錄
 * @returns {string} 安全的絕對路徑
 * @throws {Error} 路徑超出沙箱
 */
function resolveSafePath(requestedPath, projectRoot) {
    const resolved = path.resolve(projectRoot, requestedPath);
    const normalized = path.normalize(resolved);

    // 檢查是否在專案根目錄內
    if (!normalized.startsWith(projectRoot + path.sep) && normalized !== projectRoot) {
        throw new Error(`路徑超出專案範圍: ${requestedPath} → ${normalized}`);
    }

    // Symlink 逃脫檢查
    try {
        const real = fs.realpathSync(normalized);
        if (!real.startsWith(projectRoot + path.sep) && real !== projectRoot) {
            throw new Error(`Symlink 指向專案外: ${requestedPath} → ${real}`);
        }
    } catch (e) {
        // 檔案不存在時 realpathSync 會失敗，這是允許的（write_file 建立新檔）
        if (e.code !== 'ENOENT') throw e;
        // 確認父目錄在沙箱內
        const parentDir = path.dirname(normalized);
        try {
            const realParent = fs.realpathSync(parentDir);
            if (!realParent.startsWith(projectRoot + path.sep) && realParent !== projectRoot) {
                throw new Error(`父目錄的 symlink 指向專案外: ${parentDir}`);
            }
        } catch (e2) {
            if (e2.code !== 'ENOENT') throw e2;
        }
    }

    return normalized;
}

// ─── 指令封鎖 ───

const BLOCKED_PATTERNS = [
    /\brm\s+(-[rR]f?|--recursive)\s+[\/\\]/i,
    /\brmdir\s+[\/\\]/i,
    /\bformat\b/i,
    /\bdel\s+[\/\\]/i,
    /\bshutdown\b/i,
    /\breboot\b/i,
    /\bmkfs\b/i,
    /\bdd\s+if=/i,
    /:\(\)\s*\{\s*:\|:/,                    // fork bomb
    /\bcurl\b.*\|\s*(ba)?sh/i,
    /\bwget\b.*\|\s*(ba)?sh/i,
    /\bnpm\s+(publish|adduser|login)/i,
    /\bgit\s+push\s+.*--force/i,
    /\bchmod\s+777/i,
    /\bchown\b.*[\/\\]$/i,
    /\bRemove-Item\s+.*-Recurse.*[\/\\]$/i,  // PowerShell
    /\brd\s+\/s\s+\/q\s+[a-z]:\\/i,          // Windows rd
];

/**
 * 檢查指令是否被封鎖
 * @param {string} command
 * @returns {{blocked: boolean, reason?: string}}
 */
function checkCommand(command) {
    for (const pattern of BLOCKED_PATTERNS) {
        if (pattern.test(command)) {
            return { blocked: true, reason: `指令被封鎖（匹配安全規則）: ${command}` };
        }
    }
    return { blocked: false };
}

// ─── 破壞性操作偵測 ───

const DESTRUCTIVE_PATTERNS = [
    /\brm\b/i,
    /\bdel\b/i,
    /\bgit\s+reset/i,
    /\bgit\s+checkout\s+--/i,
    /\bnpm\s+uninstall/i,
    /\bRemove-Item\b/i,
];

const CONFIG_EXTENSIONS = ['.json', '.yml', '.yaml', '.env', '.config', '.toml', '.ini'];

/**
 * 判斷操作是否需要使用者確認
 * @param {string} operation - 操作類型 ('write_file' | 'run_command')
 * @param {Object} params - 操作參數
 * @returns {string|null} 需要確認時回傳確認訊息，否則 null
 */
function needsConfirmation(operation, params) {
    if (operation === 'write_file') {
        const filePath = params.path;
        // 覆寫既有檔案
        try {
            fs.accessSync(filePath);
            const stat = fs.statSync(filePath);
            const ext = path.extname(filePath).toLowerCase();
            const isConfig = CONFIG_EXTENSIONS.includes(ext);
            const prefix = isConfig ? '即將覆寫設定檔' : '即將覆寫';
            return `${prefix}: ${filePath} (${(stat.size / 1024).toFixed(1)} KB)`;
        } catch (_) {
            return null; // 檔案不存在，新建不需確認
        }
    }

    if (operation === 'run_command') {
        const cmd = params.command;
        for (const pattern of DESTRUCTIVE_PATTERNS) {
            if (pattern.test(cmd)) {
                return `即將執行可能具破壞性的指令: ${cmd}`;
            }
        }
    }

    return null;
}

/**
 * 在終端詢問使用者確認（同步等待）
 * @param {string} message - 確認訊息
 * @returns {Promise<boolean>}
 */
async function promptConfirmation(message) {
    const rl = readline.createInterface({
        input: process.stdin,
        output: process.stderr,
    });

    return new Promise((resolve) => {
        logWarn(message);
        rl.question('  繼續？(y/N): ', (answer) => {
            rl.close();
            resolve(answer.trim().toLowerCase() === 'y' || answer.trim().toLowerCase() === 'yes');
        });
    });
}

module.exports = {
    resolveSafePath,
    checkCommand,
    needsConfirmation,
    promptConfirmation,
};
