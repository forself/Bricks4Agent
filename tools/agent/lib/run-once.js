'use strict';

const { logError } = require('./utils');

/**
 * `--run` 的單次執行：送出工作項，結束時一律關閉代理，回傳程序的結束碼（成功 0，例外 1）。
 *
 * 例外時不在 catch 中直接結束程序：close 要先關閉 broker session。受治理生成的 watchdog 以「代理已結束
 * （沒有有效的 session）卻沒有產物」立即收掉任務；session 若留在 Active，任務要等到期限才以錯誤的原因失敗，
 * 容器的 on-failure 重啟也會再跑同一個工作項。
 */
async function runOnce(agent, prompt, options = {}) {
    const verbose = Boolean(options.verbose);
    try {
        await agent.send(prompt);
        return 0;
    } catch (error) {
        logError(error?.message || String(error));
        if (verbose && error?.stack) console.error(error.stack);
        return 1;
    } finally {
        await agent.close();
    }
}

module.exports = { runOnce };
