'use strict';

/**
 * SSE (Server-Sent Events) 串流解析器
 *
 * OpenAI 相容 API 的串流格式：
 *   data: {"id":"...","choices":[{"delta":{"content":"token"}}]}
 *   data: [DONE]
 *
 * 此解析器將 SSE 格式正規化為與 StreamParser (NDJSON) 相同的輸出格式，
 * 讓 AgentLoop 無需關心底層串流協議差異。
 */
class SSEParser {
    constructor() {
        this.buffer = '';
        this.content = '';
        this.toolCalls = [];
        this.thinking = '';
        this.done = false;
        this.model = '';
        // 追蹤串流中的 tool call 組裝（OpenAI 分多個 chunk 傳送）
        this._pendingToolCalls = new Map(); // index → { id, name, argStr }
    }

    /**
     * 處理收到的資料片段
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

            // 空行（SSE 事件分隔）
            if (!trimmed) continue;

            // 非 data: 開頭的行跳過（如 event:、id:、retry:）
            if (!trimmed.startsWith('data:')) continue;

            const payload = trimmed.slice(5).trim();

            // 結束信號
            if (payload === '[DONE]') {
                this.done = true;
                continue;
            }

            try {
                const obj = JSON.parse(payload);
                parsed.push(obj);

                if (obj.model) this.model = obj.model;

                const choice = obj.choices && obj.choices[0];
                if (!choice) continue;

                const delta = choice.delta || {};

                // 累積 content
                if (delta.content) {
                    this.content += delta.content;
                    if (onToken) onToken(delta.content);
                }

                // 累積 tool_calls（串流中分段傳送）
                if (delta.tool_calls) {
                    for (const tc of delta.tool_calls) {
                        const idx = tc.index != null ? tc.index : 0;
                        if (!this._pendingToolCalls.has(idx)) {
                            this._pendingToolCalls.set(idx, {
                                id: tc.id || '',
                                name: '',
                                argStr: '',
                            });
                        }
                        const pending = this._pendingToolCalls.get(idx);
                        if (tc.id) pending.id = tc.id;
                        if (tc.function) {
                            if (tc.function.name) pending.name = tc.function.name;
                            if (tc.function.arguments) pending.argStr += tc.function.arguments;
                        }
                    }
                }

                // 完成原因
                if (choice.finish_reason) {
                    this.done = true;
                    this._finalizePendingToolCalls();
                }
            } catch (_) {
                // JSON 解析失敗，跳過
            }
        }

        return parsed;
    }

    /**
     * 取得累積的完整回應（與 StreamParser.getResult() 格式一致）
     * @returns {Object}
     */
    getResult() {
        this._finalizePendingToolCalls();
        return {
            content: this.content,
            toolCalls: this.toolCalls,
            thinking: this.thinking,
            done: this.done,
            model: this.model,
            totalDuration: 0,
            evalCount: 0,
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
        this._pendingToolCalls.clear();
    }

    /**
     * 將累積的 pending tool calls 轉為最終格式
     * OpenAI: arguments 是 JSON 字串 → 解析為物件（與 Ollama 格式一致）
     */
    _finalizePendingToolCalls() {
        if (this._pendingToolCalls.size === 0) return;

        for (const [, pending] of this._pendingToolCalls) {
            if (!pending.name) continue; // 不完整的 tool call

            let args = {};
            if (pending.argStr) {
                try {
                    args = JSON.parse(pending.argStr);
                } catch (_) {
                    args = { _raw: pending.argStr }; // 容錯：保留原始字串
                }
            }

            const tc = { function: { name: pending.name, arguments: args } };
            if (pending.id) tc.id = pending.id;
            this.toolCalls.push(tc);
        }

        this._pendingToolCalls.clear();
    }
}

module.exports = { SSEParser };
