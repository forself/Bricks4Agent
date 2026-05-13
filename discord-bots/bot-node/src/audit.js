// W13: LLM reasoning audit — 在 bot-node dispatch tool 前同步把 LLM 完整 response
// 推給 broker，補齊 Ch 6.3.5 灰區二（client-side claude --print subprocess 沒被 audit）
//
// hybrid 設計：LLM 仍跑在 bot-node container（保留 Max 訂閱成本優勢）、broker 多收
// 一筆 audit record、不影響 dispatch control flow。
//
// fire-and-forget：push 失敗只 log warning、不阻斷主流程；broker 暫時不可用時 audit
// 會掉一筆、但 user 對話體驗不會卡。

import { callBroker } from './broker.js';

/**
 * 推一筆 LLM reasoning audit 到 broker。
 * 不丟 exception、failure 只 log（避免影響 user 對話）。
 *
 * @param {object} entry
 * @param {string} entry.source       'discord' | 'line'
 * @param {string} entry.userId
 * @param {string} entry.channelId
 * @param {number} entry.turn         multi-turn 第幾輪（0-indexed）
 * @param {string} entry.llmReasoning LLM 完整 response text（含 reasoning + tool_call JSON）
 * @param {string} entry.toolName     解析出的 capability name（e.g. trading.perpetual/place_order）
 * @param {object} entry.toolArgs     tool 參數（會 JSON.stringify）
 * @param {boolean} entry.aclAllowed  該 user 對該 tool 是否有 ACL allowance
 * @param {object} [deps]             unit test 注入用、prod 端永遠走 module-level callBroker
 * @returns {Promise<void>}
 */
export async function pushLlmReasoning(entry, deps = {}) {
  // ESM namespace 不能用 t.mock.method 攔截、用 DI 讓 test 可換 fake callBroker
  const _callBroker = deps.callBroker || callBroker;
  try {
    const payload = {
      source:        entry.source        || 'discord',
      user_id:       entry.userId        || '',
      channel_id:    entry.channelId     || '',
      turn:          entry.turn          ?? 0,
      llm_reasoning: entry.llmReasoning  || '',
      tool_name:     entry.toolName      || '',
      tool_args:     entry.toolArgs      ?? {},
      acl_allowed:   !!entry.aclAllowed,
      dispatch_result: 'pending',
    };
    const r = await _callBroker('POST', '/api/v1/audit/llm-reasoning', payload);
    if (!r.ok) {
      console.warn(`[audit] push reasoning failed: ${r.error}`);
    }
  } catch (e) {
    // 不可能到這裡（callBroker 內部已 catch）、但保險
    console.warn(`[audit] push reasoning exception: ${e.message}`);
  }
}
