'use strict';

const http = require('http');
const https = require('https');
const { URL } = require('url');
const { BaseProvider } = require('./base-provider');
const { SSEParser } = require('../sse-parser');
const { ResponsesParser } = require('../responses-parser');

/**
 * OpenAI 相容 Provider
 *
 * 支援兩種 API 格式：
 *   - 'chat'       — /v1/chat/completions（OpenAI 相容：Gemini、DeepSeek、Groq 等）
 *   - 'responses'  — /v1/responses（OpenAI Responses API，GPT-5 系列）
 *
 * 功能：
 * - SSE 串流
 * - Bearer 認證
 * - tool_calls 正規化
 * - 雙層心跳 + 安全終止
 * - 自動重試
 */
class OpenAIProvider extends BaseProvider {
    constructor(options = {}) {
        super(options);
        this.baseUrl = options.host || 'https://api.openai.com';
        this.apiKey = options.apiKey || '';
        this.apiFormat = options.apiFormat || 'chat'; // 'chat' | 'responses'
        this.timeout = options.timeout || 120000;
        const parsed = new URL(this.baseUrl);
        this.httpModule = parsed.protocol === 'https:' ? https : http;
    }

    get name() {
        return this.apiFormat === 'responses' ? 'openai' : 'openai-compat';
    }

    // ─── HTTP 請求 ───

    async _request(method, endpoint, body = null, options = {}) {
        const url = new URL(endpoint, this.baseUrl);
        return new Promise((resolve, reject) => {
            const headers = { 'Content-Type': 'application/json' };
            if (this.apiKey) {
                headers['Authorization'] = `Bearer ${this.apiKey}`;
            }

            const reqOptions = {
                hostname: url.hostname,
                port: url.port || (url.protocol === 'https:' ? 443 : 80),
                path: url.pathname + url.search,
                method,
                headers,
                timeout: options.timeout || this.timeout,
            };

            const req = this.httpModule.request(reqOptions, (res) => {
                let data = '';
                res.on('data', (chunk) => { data += chunk; });
                res.on('end', () => {
                    try {
                        resolve({ status: res.statusCode, data: JSON.parse(data) });
                    } catch (_) {
                        resolve({ status: res.statusCode, data: data });
                    }
                });
            });

            req.on('error', reject);
            req.on('timeout', () => {
                req.destroy();
                reject(new Error(`連線逾時 (${reqOptions.timeout}ms)`));
            });

            if (body) {
                const payload = JSON.stringify(body);
                req.setHeader('Content-Length', Buffer.byteLength(payload));
                req.write(payload);
            }
            req.end();
        });
    }

    // ─── Chat（含重試）───

    async chat(params) {
        const { model, messages, tools, stream = true, onToken, onHeartbeat, stallTimeout } = params;
        const maxRetries = 3;

        for (let attempt = 1; attempt <= maxRetries; attempt++) {
            try {
                return await this._chatOnce({ model, messages, tools, stream, onToken, onHeartbeat, stallTimeout });
            } catch (e) {
                const isRetryable = e.message && (
                    e.message.includes('ECONNRESET') ||
                    e.message.includes('串流無回應') ||
                    e.message.includes('server unreachable') ||
                    (e.message.includes('HTTP 429') || e.message.includes('HTTP 500') ||
                     e.message.includes('HTTP 502') || e.message.includes('HTTP 503'))
                );
                if (isRetryable && attempt < maxRetries) {
                    if (onHeartbeat) {
                        onHeartbeat({ elapsed: 0, sinceLastToken: 0, status: 'retry', attempt });
                    }
                    await new Promise(r => setTimeout(r, 1000 * attempt));
                    continue;
                }
                throw e;
            }
        }
    }

    /** 分派到對應的 API 格式 */
    async _chatOnce(params) {
        if (this.apiFormat === 'responses') {
            return this._chatOnceResponses(params);
        }
        return this._chatOnceChat(params);
    }

    // ══════════════════════════════════════════════════
    // Chat Completions API (/v1/chat/completions)
    // ══════════════════════════════════════════════════

    async _chatOnceChat(params) {
        const { model, messages, tools, stream, onToken, onHeartbeat } = params;
        const HEARTBEAT_INTERVAL = 20000;
        const L2_THRESHOLD = 3;

        // 非串流
        if (!stream) {
            const body = { model, messages, stream: false };
            if (tools && tools.length > 0) {
                body.tools = tools.map(t => ({ type: 'function', ...t }));
            }
            const res = await this._request('POST', '/v1/chat/completions', body);
            if (res.status !== 200) {
                throw new Error(`API 回傳 HTTP ${res.status}: ${JSON.stringify(res.data)}`);
            }
            const choice = res.data.choices && res.data.choices[0];
            const msg = choice && choice.message || {};
            return {
                content: msg.content || '',
                toolCalls: this._normalizeToolCalls(msg.tool_calls || []),
                thinking: '',
                done: true,
                model: res.data.model || model,
                totalDuration: 0,
                evalCount: res.data.usage ? res.data.usage.completion_tokens : 0,
            };
        }

        // 串流
        return new Promise((resolve, reject) => {
            const url = new URL('/v1/chat/completions', this.baseUrl);
            const body = { model, messages, stream: true };
            if (tools && tools.length > 0) {
                body.tools = tools.map(t => ({ type: 'function', ...t }));
            }
            const payload = JSON.stringify(body);

            const headers = {
                'Content-Type': 'application/json',
                'Content-Length': Buffer.byteLength(payload),
            };
            if (this.apiKey) {
                headers['Authorization'] = `Bearer ${this.apiKey}`;
            }

            const reqOptions = {
                hostname: url.hostname,
                port: url.port || (url.protocol === 'https:' ? 443 : 80),
                path: url.pathname,
                method: 'POST',
                headers,
                timeout: this.timeout,
            };

            const parser = new SSEParser();
            const startTime = Date.now();
            let lastDataTime = Date.now();
            let lastDataTimeAtHeartbeat = lastDataTime;
            let hasReceivedToken = false;
            let noDataCount = 0;
            let settled = false;

            // 心跳計時器
            const heartbeatTimer = setInterval(async () => {
                if (settled) return;
                const now = Date.now();
                const elapsed = now - startTime;
                const sinceLastToken = now - lastDataTime;

                if (lastDataTime === lastDataTimeAtHeartbeat) {
                    noDataCount++;
                } else {
                    noDataCount = 0;
                }
                lastDataTimeAtHeartbeat = lastDataTime;

                const status = hasReceivedToken ? 'generating' : 'thinking';
                if (onHeartbeat) {
                    onHeartbeat({ elapsed, sinceLastToken, status, noDataCount, runningModels: null });
                }

                // L2：3 次無資料 → 驗證
                if (noDataCount >= L2_THRESHOLD) {
                    if (onHeartbeat) {
                        onHeartbeat({ elapsed, sinceLastToken, status: 'verifying', noDataCount });
                    }
                    let alive = false;
                    try { alive = await this.healthCheck(); } catch (_) {}
                    if (settled) return;

                    if (!alive) {
                        settled = true;
                        clearInterval(heartbeatTimer);
                        clearTimeout(safetyTimer);
                        req.destroy();
                        if (onHeartbeat) onHeartbeat({ elapsed, sinceLastToken, status: 'server_down', noDataCount });
                        reject(new Error('API server unreachable'));
                        return;
                    }
                    settled = true;
                    clearInterval(heartbeatTimer);
                    clearTimeout(safetyTimer);
                    req.destroy();
                    if (onHeartbeat) onHeartbeat({ elapsed, sinceLastToken, status: 'stalled', noDataCount });
                    reject(new Error(`串流無回應已超過 ${noDataCount} 次心跳（${Math.round(sinceLastToken / 1000)}s）`));
                }
            }, HEARTBEAT_INTERVAL);

            // 安全計時器：確保心跳絕不洩漏
            const safetyTimer = setTimeout(() => {
                if (!settled) {
                    settled = true;
                    clearInterval(heartbeatTimer);
                    req.destroy();
                    reject(new Error(`請求逾時 (${this.timeout}ms)`));
                }
            }, this.timeout + 5000);

            const cleanup = () => { clearInterval(heartbeatTimer); clearTimeout(safetyTimer); };

            const req = this.httpModule.request(reqOptions, (res) => {
                if (res.statusCode !== 200) {
                    let errData = '';
                    res.on('data', (c) => { errData += c; });
                    res.on('end', () => {
                        if (!settled) { settled = true; cleanup(); reject(new Error(`API 回傳 HTTP ${res.statusCode}: ${errData}`)); }
                    });
                    return;
                }
                res.setEncoding('utf8');
                res.on('data', (chunk) => {
                    lastDataTime = Date.now();
                    const events = parser.feed(chunk, onToken);
                    if (events.some(p => { const c = p.choices && p.choices[0]; return c && c.delta && c.delta.content; })) {
                        hasReceivedToken = true;
                    }
                });
                res.on('end', () => {
                    if (!settled) {
                        settled = true;
                        cleanup();
                        if (onHeartbeat) onHeartbeat({ elapsed: Date.now() - startTime, sinceLastToken: 0, status: 'done' });
                        resolve(parser.getResult());
                    }
                });
            });

            req.on('error', (e) => { if (!settled) { settled = true; cleanup(); reject(e); } });
            req.on('timeout', () => { if (!settled) { settled = true; cleanup(); req.destroy(); reject(new Error(`串流逾時 (${this.timeout}ms)`)); } });

            req.write(payload);
            req.end();
        });
    }

    // ══════════════════════════════════════════════════
    // Responses API (/v1/responses)
    // ══════════════════════════════════════════════════

    async _chatOnceResponses(params) {
        const { model, messages, tools, stream, onToken, onHeartbeat } = params;
        const HEARTBEAT_INTERVAL = 20000;
        const L2_THRESHOLD = 3;

        // 轉換訊息格式
        const { instructions, input } = this._convertToResponsesInput(messages);

        // 構建 request body
        const body = { model, input };
        if (instructions) body.instructions = instructions;
        if (tools && tools.length > 0) {
            body.tools = this._convertToolsForResponses(tools);
        }

        // 非串流
        if (!stream) {
            body.stream = false;
            const res = await this._request('POST', '/v1/responses', body);
            if (res.status !== 200) {
                throw new Error(`API 回傳 HTTP ${res.status}: ${JSON.stringify(res.data)}`);
            }
            return this._parseResponsesResult(res.data, model);
        }

        // 串流
        body.stream = true;
        const payload = JSON.stringify(body);

        return new Promise((resolve, reject) => {
            const url = new URL('/v1/responses', this.baseUrl);

            const headers = {
                'Content-Type': 'application/json',
                'Content-Length': Buffer.byteLength(payload),
            };
            if (this.apiKey) {
                headers['Authorization'] = `Bearer ${this.apiKey}`;
            }

            const reqOptions = {
                hostname: url.hostname,
                port: url.port || (url.protocol === 'https:' ? 443 : 80),
                path: url.pathname,
                method: 'POST',
                headers,
                timeout: this.timeout,
            };

            const parser = new ResponsesParser();
            const startTime = Date.now();
            let lastDataTime = Date.now();
            let lastDataTimeAtHeartbeat = lastDataTime;
            let hasReceivedToken = false;
            let noDataCount = 0;
            let settled = false;

            // 心跳計時器
            const heartbeatTimer = setInterval(async () => {
                if (settled) return;
                const now = Date.now();
                const elapsed = now - startTime;
                const sinceLastToken = now - lastDataTime;

                if (lastDataTime === lastDataTimeAtHeartbeat) {
                    noDataCount++;
                } else {
                    noDataCount = 0;
                }
                lastDataTimeAtHeartbeat = lastDataTime;

                const status = hasReceivedToken ? 'generating' : 'thinking';
                if (onHeartbeat) {
                    onHeartbeat({ elapsed, sinceLastToken, status, noDataCount, runningModels: null });
                }

                // L2
                if (noDataCount >= L2_THRESHOLD) {
                    if (onHeartbeat) {
                        onHeartbeat({ elapsed, sinceLastToken, status: 'verifying', noDataCount });
                    }
                    let alive = false;
                    try { alive = await this.healthCheck(); } catch (_) {}
                    if (settled) return;

                    if (!alive) {
                        settled = true;
                        clearInterval(heartbeatTimer);
                        clearTimeout(safetyTimer);
                        req.destroy();
                        if (onHeartbeat) onHeartbeat({ elapsed, sinceLastToken, status: 'server_down', noDataCount });
                        reject(new Error('API server unreachable'));
                        return;
                    }
                    settled = true;
                    clearInterval(heartbeatTimer);
                    clearTimeout(safetyTimer);
                    req.destroy();
                    if (onHeartbeat) onHeartbeat({ elapsed, sinceLastToken, status: 'stalled', noDataCount });
                    reject(new Error(`串流無回應已超過 ${noDataCount} 次心跳（${Math.round(sinceLastToken / 1000)}s）`));
                }
            }, HEARTBEAT_INTERVAL);

            // 安全計時器
            const safetyTimer = setTimeout(() => {
                if (!settled) {
                    settled = true;
                    clearInterval(heartbeatTimer);
                    req.destroy();
                    reject(new Error(`請求逾時 (${this.timeout}ms)`));
                }
            }, this.timeout + 5000);

            const cleanup = () => { clearInterval(heartbeatTimer); clearTimeout(safetyTimer); };

            const req = this.httpModule.request(reqOptions, (res) => {
                if (res.statusCode !== 200) {
                    let errData = '';
                    res.on('data', (c) => { errData += c; });
                    res.on('end', () => {
                        if (!settled) { settled = true; cleanup(); reject(new Error(`API 回傳 HTTP ${res.statusCode}: ${errData}`)); }
                    });
                    return;
                }
                res.setEncoding('utf8');
                res.on('data', (chunk) => {
                    lastDataTime = Date.now();
                    const events = parser.feed(chunk, onToken);
                    if (events.some(e => e.type === 'response.output_text.delta' && e.delta)) {
                        hasReceivedToken = true;
                    }
                });
                res.on('end', () => {
                    if (!settled) {
                        settled = true;
                        cleanup();
                        if (onHeartbeat) onHeartbeat({ elapsed: Date.now() - startTime, sinceLastToken: 0, status: 'done' });
                        // 檢查串流中的錯誤事件
                        if (parser.error) {
                            const errMsg = parser.error.message || parser.error.code || 'Unknown stream error';
                            reject(new Error(`API 串流錯誤: ${errMsg}`));
                            return;
                        }
                        resolve(parser.getResult());
                    }
                });
            });

            req.on('error', (e) => { if (!settled) { settled = true; cleanup(); reject(e); } });
            req.on('timeout', () => { if (!settled) { settled = true; cleanup(); req.destroy(); reject(new Error(`串流逾時 (${this.timeout}ms)`)); } });

            req.write(payload);
            req.end();
        });
    }

    // ─── 訊息格式轉換 ───

    /**
     * 將內部 messages 陣列轉換為 Responses API 格式
     * @param {Object[]} messages
     * @returns {{ instructions: string, input: Object[] }}
     */
    _convertToResponsesInput(messages) {
        let instructions = '';
        const input = [];

        for (const msg of messages) {
            if (msg.role === 'system') {
                instructions = msg.content || '';
                continue;
            }

            if (msg.role === 'user') {
                input.push({ role: 'user', content: msg.content || '' });
                continue;
            }

            if (msg.role === 'assistant') {
                // 先推入文字訊息
                if (msg.content) {
                    input.push({
                        type: 'message',
                        role: 'assistant',
                        content: [{ type: 'output_text', text: msg.content }],
                    });
                }
                // 再推入 function_call 項
                if (msg.tool_calls && msg.tool_calls.length > 0) {
                    for (const tc of msg.tool_calls) {
                        const fn = tc.function || {};
                        const args = typeof fn.arguments === 'string'
                            ? fn.arguments
                            : JSON.stringify(fn.arguments || {});
                        input.push({
                            type: 'function_call',
                            call_id: tc.id || `call_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`,
                            name: fn.name || '',
                            arguments: args,
                            status: 'completed',
                        });
                    }
                }
                continue;
            }

            if (msg.role === 'tool') {
                input.push({
                    type: 'function_call_output',
                    call_id: msg.tool_call_id || '',
                    output: typeof msg.content === 'string' ? msg.content : JSON.stringify(msg.content || ''),
                });
                continue;
            }
        }

        return { instructions, input };
    }

    /**
     * 將 tool definitions 轉換為 Responses API 格式
     * Chat Completions: { function: { name, description, parameters } }
     * Responses API:    { type: 'function', name, description, parameters }
     */
    _convertToolsForResponses(tools) {
        return tools.map(t => {
            const fn = t.function || t;
            return {
                type: 'function',
                name: fn.name,
                description: fn.description || '',
                parameters: fn.parameters || {},
            };
        });
    }

    /**
     * 解析非串流 Responses API 回應
     * @param {Object} data - 原始回應
     * @param {string} fallbackModel - 備用模型名稱
     * @returns {Object} 標準化結果
     */
    _parseResponsesResult(data, fallbackModel) {
        const textContent = data.output_text || '';
        const toolCalls = [];

        if (data.output && Array.isArray(data.output)) {
            for (const item of data.output) {
                if (item.type === 'function_call') {
                    let args = {};
                    if (item.arguments) {
                        if (typeof item.arguments === 'string') {
                            try { args = JSON.parse(item.arguments); }
                            catch (_) { args = { _raw: item.arguments }; }
                        } else {
                            args = item.arguments;
                        }
                    }
                    const tc = { function: { name: item.name, arguments: args } };
                    if (item.call_id) tc.id = item.call_id;
                    toolCalls.push(tc);
                }
            }
        }

        return {
            content: textContent,
            toolCalls,
            thinking: '',
            done: true,
            model: data.model || fallbackModel,
            totalDuration: 0,
            evalCount: data.usage ? (data.usage.output_tokens || 0) : 0,
        };
    }

    /**
     * 正規化 tool_calls（Chat Completions 格式）
     */
    _normalizeToolCalls(toolCalls) {
        return toolCalls.map(tc => {
            const fn = tc.function || {};
            let args = fn.arguments || {};
            if (typeof args === 'string') {
                try { args = JSON.parse(args); } catch (_) { args = { _raw: args }; }
            }
            const result = { function: { name: fn.name, arguments: args } };
            if (tc.id) result.id = tc.id;
            return result;
        });
    }

    // ─── 模型管理 ───

    async listModels() {
        const res = await this._request('GET', '/v1/models', null, { timeout: 10000 });
        if (res.status !== 200) {
            throw new Error(`無法列出模型: HTTP ${res.status}`);
        }
        const models = res.data.data || res.data.models || [];
        return models.map(m => ({
            name: m.id || m.name,
            size: null,
        }));
    }

    async healthCheck() {
        try {
            const res = await this._request('GET', '/v1/models', null, { timeout: 5000 });
            return res.status === 200;
        } catch (_) {
            return false;
        }
    }

    supportsToolCalling(_modelName) {
        return true; // 雲端 API 皆支援
    }
}

module.exports = { OpenAIProvider };
