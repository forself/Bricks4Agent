'use strict';

/**
 * NDJSON 串流解析器
 * Ollama 的串流回應為 newline-delimited JSON，每行一個 JSON 物件
 */
class StreamParser {
    constructor() {
        this.buffer = '';
        this.content = '';
        this.toolCalls = [];
        this.thinking = '';
        this.done = false;
        this.doneReason = '';
        this.model = '';
        this.totalDuration = 0;
        this.evalCount = 0;
    }

    /**
     * 處理收到的資料片段
     * @param {string} chunk - 原始資料片段
     * @param {function} [onToken] - 每個 token 的回呼 (token: string)
     * @returns {Object[]} 解析出的完整 JSON 物件陣列
     */
    feed(chunk, onToken) {
        this.buffer += chunk;
        const lines = this.buffer.split('\n');
        // 最後一行可能不完整，保留在 buffer
        this.buffer = lines.pop() || '';

        const parsed = [];
        for (const line of lines) {
            const trimmed = line.trim();
            if (!trimmed) continue;

            try {
                const obj = JSON.parse(trimmed);
                parsed.push(obj);

                if (obj.message) {
                    // 累積 content
                    if (obj.message.content) {
                        this.content += obj.message.content;
                        if (onToken) onToken(obj.message.content);
                    }

                    // 累積 thinking
                    if (obj.message.thinking) {
                        this.thinking += obj.message.thinking;
                    }

                    // 累積 tool_calls
                    if (obj.message.tool_calls && obj.message.tool_calls.length > 0) {
                        this.toolCalls.push(...obj.message.tool_calls);
                    }
                }

                if (obj.model) this.model = obj.model;

                if (obj.done) {
                    this.done = true;
                    this.doneReason = obj.done_reason || '';
                    this.totalDuration = obj.total_duration || 0;
                    this.evalCount = obj.eval_count || 0;
                }
            } catch (e) {
                // JSON 解析失敗，跳過此行
            }
        }

        return parsed;
    }

    /**
     * 取得累積的完整回應
     * @returns {Object}
     */
    getResult() {
        return {
            content: this.content,
            toolCalls: this.toolCalls,
            thinking: this.thinking,
            done: this.done,
            doneReason: this.doneReason,
            model: this.model,
            totalDuration: this.totalDuration,
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
        this.doneReason = '';
        this.totalDuration = 0;
        this.evalCount = 0;
    }
}

module.exports = { StreamParser };
