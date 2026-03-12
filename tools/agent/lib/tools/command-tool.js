'use strict';

const { execSync } = require('child_process');
const path = require('path');
const { resolveSafePath, checkCommand, needsConfirmation, promptConfirmation } = require('../safety');
const { logTool, truncateString } = require('../utils');

const MAX_OUTPUT = 10000;
const DEFAULT_TIMEOUT = 30000;

/**
 * 執行 shell 指令
 */
async function runCommand(args, context) {
    const { command, cwd } = args;

    // 安全檢查
    const check = checkCommand(command);
    if (check.blocked) {
        return `❌ ${check.reason}`;
    }

    // 工作目錄
    let workDir = context.projectRoot;
    if (cwd) {
        workDir = resolveSafePath(cwd, context.projectRoot);
    }

    logTool('run_command', `$ ${command}`);

    // 確認提示
    if (!context.noConfirm) {
        const confirmMsg = needsConfirmation('run_command', { command });
        if (confirmMsg) {
            const ok = await promptConfirmation(confirmMsg);
            if (!ok) return '操作已取消';
        }
    }

    try {
        const output = execSync(command, {
            cwd: workDir,
            timeout: DEFAULT_TIMEOUT,
            encoding: 'utf8',
            maxBuffer: 1024 * 1024, // 1MB
            stdio: ['pipe', 'pipe', 'pipe'],
            shell: process.platform === 'win32' ? 'cmd.exe' : '/bin/sh',
        });

        let result = output || '(無輸出)';
        if (result.length > MAX_OUTPUT) {
            const truncated = result.length - MAX_OUTPUT;
            result = result.slice(0, MAX_OUTPUT) + `\n... (已截斷 ${truncated} 字元)`;
        }

        return `$ ${command}\n${result}`;
    } catch (e) {
        let errOutput = '';
        if (e.stdout) errOutput += e.stdout;
        if (e.stderr) errOutput += (errOutput ? '\n' : '') + e.stderr;
        errOutput = errOutput || e.message;

        if (errOutput.length > MAX_OUTPUT) {
            errOutput = errOutput.slice(0, MAX_OUTPUT) + '\n... (已截斷)';
        }

        if (e.killed) {
            return `❌ 指令逾時 (${DEFAULT_TIMEOUT / 1000}秒): ${command}\n${errOutput}`;
        }

        return `❌ 指令失敗 (exit ${e.status || 'unknown'}): ${command}\n${errOutput}`;
    }
}

module.exports = { runCommand };
