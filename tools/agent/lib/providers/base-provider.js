'use strict';

/**
 * LLM Provider 抽象介面
 *
 * 所有 provider（Ollama、OpenAI 等）必須實作此介面。
 * 確保 AgentLoop 可透過統一介面與任何後端互動。
 *
 * HeartbeatInfo 格式：
 *   { elapsed, sinceLastToken, status, noDataCount?, runningModels?, attempt? }
 *
 * status 值：
 *   'thinking'    - 連線已建立，尚未收到 token
 *   'generating'  - token 持續流入中
 *   'verifying'   - Layer 2：正在主動驗證伺服器狀態
 *   'stalled'     - 驗證通過但無串流資料
 *   'server_down' - 伺服器無回應
 *   'retry'       - 正在重試（含 attempt 次數）
 *   'done'        - 串流正常結束
 *
 * ChatResult 格式：
 *   { content, toolCalls, thinking, done, model, totalDuration?, evalCount? }
 */
class BaseProvider {
    constructor(options = {}) {
        this.baseUrl = options.host || options.baseUrl || '';
        this.timeout = options.timeout || 120000;
    }

    /**
     * 發送 chat 請求
     * @param {Object} params
     * @param {string} params.model
     * @param {Object[]} params.messages
     * @param {Object[]} [params.tools]
     * @param {boolean} [params.stream]
     * @param {function} [params.onToken] - (token: string) => void
     * @param {function} [params.onHeartbeat] - (info: HeartbeatInfo) => void
     * @param {number} [params.stallTimeout] - 無資料逾時（ms）
     * @returns {Promise<ChatResult>}
     */
    async chat(params) {
        throw new Error('BaseProvider.chat() must be implemented');
    }

    /**
     * 列出可用模型
     * @returns {Promise<{name: string, size?: number}[]>}
     */
    async listModels() {
        throw new Error('BaseProvider.listModels() must be implemented');
    }

    /**
     * 健康檢查：確認伺服器可連線
     * @returns {Promise<boolean>}
     */
    async healthCheck() {
        throw new Error('BaseProvider.healthCheck() must be implemented');
    }

    /**
     * 檢查運行中的模型（Ollama 專用）
     * 雲端 provider 回傳 null
     * @returns {Promise<{models: Object[]}|null>}
     */
    async checkRunningModels() {
        return null;
    }

    /**
     * 偵測模型是否支援原生 tool calling
     * @param {string} modelName
     * @returns {boolean}
     */
    supportsToolCalling(modelName) {
        return false;
    }

    /** Provider 名稱（供顯示用） */
    get name() { return 'base'; }
}

module.exports = { BaseProvider };
