'use strict';

const http = require('http');
const https = require('https');
const { URL } = require('url');
const { BaseProvider } = require('./base-provider');
const { StreamParser } = require('../streaming');

// ─── 模型工具呼叫能力偵測 ───

const TOOL_CAPABLE_FAMILIES = [
    'llama3.1', 'llama3.2', 'llama3.3', 'llama4',
    'qwen2.5', 'qwen3',
    'mistral-nemo', 'mistral-small', 'mistral-large',
    'command-r', 'command-r-plus',
    'granite4', 'granite-4',
    'devstral',
    'deepseek-v2.5', 'deepseek-v3',
    'firefunction',
    'nemotron',
    'glm-4',
];

/**
 * Ollama Provider — 本地端 Ollama 伺服器
 *
 * 功能：
 * - 原生 tool calling + ReAct XML 回退
 * - NDJSON 串流
 * - 雙層心跳：L1 資源監控（20s），L2 主動驗證（3 次無回應）
 * - 自動重試（3 次，指數退避）
 */
class OllamaProvider extends BaseProvider {
    constructor(options = {}) {
        super(options);
        this.baseUrl = options.host || 'http://localhost:11434';
        this.timeout = options.timeout || 120000;
        const parsed = new URL(this.baseUrl);
        this.httpModule = parsed.protocol === 'https:' ? https : http;
    }

    get name() { return 'ollama'; }

    // ─── HTTP 請求 ───

    async _request(method, endpoint, body = null, options = {}) {
        const url = new URL(endpoint, this.baseUrl);
        return new Promise((resolve, reject) => {
            const reqOptions = {
                hostname: url.hostname,
                port: url.port,
                path: url.pathname + url.search,
                method,
                headers: { 'Content-Type': 'application/json' },
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
                req.write(JSON.stringify(body));
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
                    e.message.includes('EOF') ||
                    e.message.includes('ECONNRESET') ||
                    e.message.includes('串流無回應') ||
                    e.message.includes('伺服器無回應')
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

    /** @private 單次 chat 請求（含雙層心跳） */
    async _chatOnce(params) {
        const { model, messages, tools, stream, onToken, onHeartbeat } = params;
        const HEARTBEAT_INTERVAL = 20000;  // Layer 1: 每 20 秒
        const L2_THRESHOLD = 3;            // Layer 2: 3 次無資料

        // 非串流模式
        if (!stream) {
            const body = { model, messages, stream: false };
            if (tools && tools.length > 0) body.tools = tools;
            const res = await this._request('POST', '/api/chat', body);
            if (res.status !== 200) {
                throw new Error(`Ollama 回傳 HTTP ${res.status}: ${JSON.stringify(res.data)}`);
            }
            return {
                content: res.data.message?.content || '',
                toolCalls: res.data.message?.tool_calls || [],
                thinking: res.data.message?.thinking || '',
                done: true,
                model: res.data.model,
                totalDuration: res.data.total_duration || 0,
                evalCount: res.data.eval_count || 0,
            };
        }

        // 串流模式（含雙層心跳）
        return new Promise((resolve, reject) => {
            const url = new URL('/api/chat', this.baseUrl);
            const body = { model, messages, stream: true };
            if (tools && tools.length > 0) body.tools = tools;
            const payload = JSON.stringify(body);

            const reqOptions = {
                hostname: url.hostname,
                port: url.port,
                path: url.pathname,
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Content-Length': Buffer.byteLength(payload),
                },
                timeout: this.timeout,
            };

            const parser = new StreamParser();
            const startTime = Date.now();
            let lastDataTime = Date.now();
            let lastDataTimeAtHeartbeat = lastDataTime;
            let hasReceivedToken = false;
            let noDataCount = 0;
            let settled = false;

            // ─── 雙層心跳計時器 ───
            const heartbeatTimer = setInterval(async () => {
                if (settled) return;
                const now = Date.now();
                const elapsed = now - startTime;
                const sinceLastToken = now - lastDataTime;

                // 檢查是否有新資料
                if (lastDataTime === lastDataTimeAtHeartbeat) {
                    noDataCount++;
                } else {
                    noDataCount = 0;
                }
                lastDataTimeAtHeartbeat = lastDataTime;

                // Layer 1：資源監控
                let runningModels = null;
                try {
                    runningModels = await this.checkRunningModels();
                } catch (_) { /* 非阻塞，忽略錯誤 */ }

                if (settled) return; // async 後再次檢查

                const status = hasReceivedToken ? 'generating' : 'thinking';

                if (onHeartbeat) {
                    onHeartbeat({ elapsed, sinceLastToken, status, noDataCount, runningModels });
                }

                // Layer 2：主動驗證（3 次無資料 = 60 秒）
                if (noDataCount >= L2_THRESHOLD) {
                    if (onHeartbeat) {
                        onHeartbeat({ elapsed, sinceLastToken, status: 'verifying', noDataCount });
                    }

                    let alive = false;
                    try {
                        alive = await this.healthCheck();
                    } catch (_) { /* 健康檢查失敗 = 不在線 */ }

                    if (settled) return;

                    if (!alive) {
                        settled = true;
                        clearInterval(heartbeatTimer);
                        req.destroy();
                        if (onHeartbeat) {
                            onHeartbeat({ elapsed, sinceLastToken, status: 'server_down', noDataCount });
                        }
                        reject(new Error('Ollama 伺服器無回應，連線中斷'));
                        return;
                    }

                    // 伺服器在線但無資料 → stalled
                    settled = true;
                    clearInterval(heartbeatTimer);
                    req.destroy();
                    if (onHeartbeat) {
                        onHeartbeat({ elapsed, sinceLastToken, status: 'stalled', noDataCount });
                    }
                    reject(new Error(
                        `串流無回應已超過 ${noDataCount} 次心跳` +
                        `（${Math.round(sinceLastToken / 1000)}s），伺服器仍在線但未傳送資料`
                    ));
                }
            }, HEARTBEAT_INTERVAL);

            const cleanup = () => { clearInterval(heartbeatTimer); };

            const req = this.httpModule.request(reqOptions, (res) => {
                if (res.statusCode !== 200) {
                    let errData = '';
                    res.on('data', (c) => { errData += c; });
                    res.on('end', () => {
                        if (!settled) {
                            settled = true;
                            cleanup();
                            reject(new Error(`Ollama 回傳 HTTP ${res.statusCode}: ${errData}`));
                        }
                    });
                    return;
                }

                res.setEncoding('utf8');
                res.on('data', (chunk) => {
                    lastDataTime = Date.now();
                    const parsed = parser.feed(chunk, onToken);
                    if (parsed.some(p => p.message && p.message.content)) {
                        hasReceivedToken = true;
                    }
                });
                res.on('end', () => {
                    if (!settled) {
                        settled = true;
                        cleanup();
                        if (onHeartbeat) {
                            onHeartbeat({ elapsed: Date.now() - startTime, sinceLastToken: 0, status: 'done' });
                        }
                        resolve(parser.getResult());
                    }
                });
            });

            req.on('error', (e) => {
                if (!settled) { settled = true; cleanup(); reject(e); }
            });
            req.on('timeout', () => {
                if (!settled) {
                    settled = true;
                    cleanup();
                    req.destroy();
                    reject(new Error(`串流逾時 (${this.timeout}ms)`));
                }
            });

            req.write(payload);
            req.end();
        });
    }

    // ─── 模型管理 ───

    async listModels() {
        const res = await this._request('GET', '/api/tags', null, { timeout: 5000 });
        if (res.status !== 200) {
            throw new Error(`無法列出模型: HTTP ${res.status}`);
        }
        return (res.data.models || []).map(m => ({
            name: m.name,
            size: m.size || null,
        }));
    }

    async showModel(modelName) {
        const res = await this._request('POST', '/api/show', { name: modelName }, { timeout: 5000 });
        if (res.status !== 200) {
            throw new Error(`無法取得模型資訊 '${modelName}': HTTP ${res.status}`);
        }
        return res.data;
    }

    async healthCheck() {
        try {
            const url = new URL('/', this.baseUrl);
            return new Promise((resolve) => {
                const req = this.httpModule.request({
                    hostname: url.hostname,
                    port: url.port,
                    path: '/',
                    method: 'GET',
                    timeout: 5000,
                }, (res) => {
                    res.resume();
                    resolve(res.statusCode === 200);
                });
                req.on('error', () => resolve(false));
                req.on('timeout', () => { req.destroy(); resolve(false); });
                req.end();
            });
        } catch (_) {
            return false;
        }
    }

    async checkRunningModels() {
        const res = await this._request('GET', '/api/ps', null, { timeout: 5000 });
        if (res.status !== 200) return null;
        return res.data;
    }

    supportsToolCalling(modelName) {
        const lower = modelName.toLowerCase().replace(/:.*$/, '');
        return TOOL_CAPABLE_FAMILIES.some((family) => lower.startsWith(family));
    }
}

module.exports = { OllamaProvider };
