'use strict';

const { logError } = require('./utils');
const { parseGovernedResponse, serializeGovernedResponse } = require('./governed-response');

/**
 * `--run` 的單次執行：送出工作項，結束時一律關閉代理，回傳程序的結束碼。
 *
 * 結束碼只分兩種：
 * - 0：這次執行跑完了。受治理模式下，模型層級的結果（final、refusal、failure，包含模型輸出不合約定的
 *   invalid_response）一律是 0，結果以固定的 JSON 寫在 stdout，讀結果的一方看 JSON，不看結束碼。
 * - 1：基礎設施失敗（例外：broker 連不上、註冊被拒、關閉 session 失敗）。受治理模式仍在 stdout 寫一則
 *   {"type":"failure","code":"tool_failed"}。
 * 代理容器以 `--restart on-failure` 啟動：非零結束碼會讓容器重跑同一個工作項，所以模型拒絕或回覆不合約定
 * 不能用非零結束碼表示（重跑只會得到同樣的結果，還會再消耗一次任務的配額）。
 *
 * 例外時不在 catch 中直接結束程序：close 要先關閉 broker session。受治理生成的 watchdog 以「代理已結束
 * （沒有有效的 session）卻沒有產物」立即收掉任務；session 若留在 Active，任務要等到期限才以錯誤的原因失敗，
 * 容器的 on-failure 重啟也會再跑同一個工作項。
 */
async function runOnce(agent, prompt, options = {}) {
    const verbose = Boolean(options.verbose);
    const governed = Boolean(agent.governedConfig || agent.governedExecutor);
    let governedOutput = null;
    let completed = false;
    let exitCode = 0;
    try {
        const result = await agent.send(prompt);
        if (governed) {
            const parsed = parseGovernedResponse(result);
            const response = parsed.ok ? parsed.response : { type: 'failure', code: 'invalid_response' };
            governedOutput = JSON.stringify(response);
            completed = response.type === 'final';
        }
    } catch (error) {
        logError(error?.message || String(error));
        if (verbose && error?.stack) console.error(error.stack);
        if (governed) governedOutput = serializeGovernedResponse('failure', 'tool_failed');
        exitCode = 1;
    } finally {
        try {
            await agent.close();
        } catch (error) {
            logError(`Agent close failed: ${error?.message || String(error)}`);
            // 已回報完成的執行在關閉失敗時改報失敗；模型層級的 refusal／failure 結果保留。
            if (governed && completed) {
                governedOutput = serializeGovernedResponse('failure', 'tool_failed');
            }
            exitCode = 1;
        }
    }
    if (governed) process.stdout.write(`${governedOutput}\n`);
    return exitCode;
}

module.exports = { runOnce };
