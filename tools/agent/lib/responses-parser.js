'use strict';

/**
 * OpenAI Responses API (/v1/responses) SSE 串流解析器
 *
 * 與 Chat Completions 的 SSE 格式不同，Responses API 使用語義化事件：
 *
 *   event: response.output_text.delta
 *   data: {"type":"response.output_text.delta","delta":"token"}
 *
 * 關鍵事件：
 *   response.output_text.delta              — 文字串流（累加 delta）
 *   response.output_text.done               — 完整文字（權威值）
 *   response.output_item.added              — 新輸出項（message / function_call）
 *   response.function_call_arguments.delta   — 函式引數串流
 *   response.function_call_arguments.done    — 完整函式引數
 *   response.output_item.done               — 輸出項完成
 *   response.completed                      — 回應完成（含 usage）
 *   response.failed                         — 回應失敗
 *   response.reasoning_summary_text.delta    — 推理摘要串流
 *
 * getResult() 回傳格式與 SSEParser / StreamParser 一致，
 * 讓 AgentLoop 無需關心底層 API 差異。
 */
class ResponsesParser {
    constructor() {
        this.buffer = '';
        this.content = '';
        this.toolCalls = [];
        this.thinking = '';
        this.done = false;
        this.model = '';
        this.evalCount = 0;
        this.error = null; // 串流中的錯誤事件
        // 追蹤串流中的 function call（by item_id）
        this._pendingCalls = new Map();
        this._finalizedIds = new Set(); // 已完成的 item_id，避免重複
    }

    /**
     * 處理收到的 SSE 資料片段
     * @param {string} chunk - 原始 SSE 資料
     * @param {function} [onToken] - (token: string) => void
     * @returns {Object[]} 解析出的事件物件
     */
    feed(chunk, onToken) {
        this.buffer += chunk;
        const lines = this.buffer.split('\n');
        this.buffer = lines.pop() || '';

        const parsed = [];
        for (const line of lines) {
            const trimmed = line.trim();
            if (!trimmed) continue;

            // 跳過 event: 行（type 已在 data JSON 中）
            if (trimmed.startsWith('event:')) continue;

            if (!trimmed.startsWith('data:')) continue;

            const payload = trimmed.slice(5).trim();
            if (!payload || payload === '[DONE]') {
                this.done = true;
                continue;
            }

            try {
                const obj = JSON.parse(payload);
                parsed.push(obj);

                switch (obj.type) {
                    // ─── 文字內容 ───
                    case 'response.output_text.delta':
                        if (obj.delta) {
                            this.content += obj.delta;
                            if (onToken) onToken(obj.delta);
                        }
                        break;

                    case 'response.output_text.done':
                        if (obj.text != null) {
                            this.content = obj.text; // 權威值，覆蓋累加結果
                        }
                        break;

                    // ─── 新輸出項 ───
                    case 'response.output_item.added':
                        if (obj.item && obj.item.type === 'function_call') {
                            this._pendingCalls.set(obj.item.id, {
                                callId: obj.item.call_id || '',
                                name: obj.item.name || '',
                                argStr: '',
                            });
                        }
                        break;

                    // ─── 函式引數串流 ───
                    case 'response.function_call_arguments.delta':
                        if (obj.item_id && this._pendingCalls.has(obj.item_id)) {
                            this._pendingCalls.get(obj.item_id).argStr += (obj.delta || '');
                        }
                        break;

                    case 'response.function_call_arguments.done':
                        if (obj.item_id && this._pendingCalls.has(obj.item_id)) {
                            const pending = this._pendingCalls.get(obj.item_id);
                            if (obj.arguments) pending.argStr = obj.arguments;
                            this._finalizeCall(obj.item_id);
                        }
                        break;

                    // ─── 輸出項完成 ───
                    case 'response.output_item.done':
                        if (obj.item && obj.item.type === 'function_call') {
                            const id = obj.item.id;
                            // 跳過已經由 function_call_arguments.done 完成的項目
                            if (this._finalizedIds.has(id)) break;
                            if (this._pendingCalls.has(id)) {
                                const pending = this._pendingCalls.get(id);
                                if (obj.item.arguments) pending.argStr = obj.item.arguments;
                                if (obj.item.name) pending.name = obj.item.name;
                                if (obj.item.call_id) pending.callId = obj.item.call_id;
                                this._finalizeCall(id);
                            } else {
                                // 可能沒收到 added 事件，直接從 done 建立
                                this._pendingCalls.set(id, {
                                    callId: obj.item.call_id || '',
                                    name: obj.item.name || '',
                                    argStr: obj.item.arguments || '',
                                });
                                this._finalizeCall(id);
                            }
                        }
                        break;

                    // ─── 回應完成 / 失敗 ───
                    case 'response.completed':
                        this.done = true;
                        if (obj.response) {
                            if (obj.response.model) this.model = obj.response.model;
                            if (obj.response.usage) {
                                this.evalCount = obj.response.usage.output_tokens || 0;
                            }
                        }
                        break;

                    case 'response.failed':
                        this.done = true;
                        if (obj.response && obj.response.error) {
                            this.error = obj.response.error;
                        }
                        break;

                    // ─── 串流錯誤 ───
                    case 'error':
                        this.done = true;
                        if (obj.error) {
                            this.error = obj.error;
                        }
                        break;

                    // ─── 推理摘要 ───
                    case 'response.reasoning_summary_text.delta':
                        if (obj.delta) this.thinking += obj.delta;
                        break;

                    case 'response.reasoning_summary_text.done':
                        if (obj.text != null) this.thinking = obj.text;
                        break;

                    // 其餘事件忽略
                    default:
                        break;
                }
            } catch (_) {
                // JSON 解析失敗，跳過
            }
        }

        return parsed;
    }

    /**
     * 將 pending call 轉為最終格式
     * @private
     */
    _finalizeCall(itemId) {
        const pending = this._pendingCalls.get(itemId);
        if (!pending || !pending.name) return;

        let args = {};
        if (pending.argStr) {
            try { args = JSON.parse(pending.argStr); }
            catch (_) { args = { _raw: pending.argStr }; }
        }

        const tc = { function: { name: pending.name, arguments: args } };
        if (pending.callId) tc.id = pending.callId;
        this.toolCalls.push(tc);
        this._pendingCalls.delete(itemId);
        this._finalizedIds.add(itemId);
    }

    /**
     * 取得累積的完整回應（與 SSEParser.getResult() 格式一致）
     * @returns {Object}
     */
    getResult() {
        // 清算剩餘 pending calls
        for (const itemId of [...this._pendingCalls.keys()]) {
            this._finalizeCall(itemId);
        }
        return {
            content: this.content,
            toolCalls: this.toolCalls,
            thinking: this.thinking,
            done: this.done,
            model: this.model,
            totalDuration: 0,
            evalCount: this.evalCount,
        };
    }

    /** 重置狀態 */
    reset() {
        this.buffer = '';
        this.content = '';
        this.toolCalls = [];
        this.thinking = '';
        this.done = false;
        this.model = '';
        this.evalCount = 0;
        this.error = null;
        this._pendingCalls.clear();
        this._finalizedIds.clear();
    }
}

module.exports = { ResponsesParser };
