'use strict';

/**
 * Governed Executor — 受控執行模式
 *
 * 將工具呼叫路由到 Broker 中介核心，透過加密通道提交執行請求。
 * Broker 負責政策裁決（PEP 16 步流程），只有被允許的操作才會執行。
 *
 * 生命週期：
 *   1. init()        → ECDH 金鑰交換、建立加密 session
 *   2. executeTool()  → 每次工具呼叫 → 加密提交 → 裁決 → 回傳結果
 *   3. close()       → 優雅關閉 session、清零金鑰
 *
 * 能力映射：JS 工具名稱 → Broker capability_id
 */
const crypto = require('crypto');
const { BrokerClient } = require('./broker-client');
const { logInfo, logWarn, logError, colorize } = require('./utils');

// ── 工具 → 能力映射 ──

const TOOL_TO_CAPABILITY = {
    'read_file':       'file.read',
    'write_file':      'file.write',
    'list_directory':  'file.list',
    'search_files':    'file.search_name',
    'search_content':  'file.search_content',
    'run_command':     'command.execute',
};

class GovernedExecutor {
    /**
     * @param {Object} options
     * @param {string} options.brokerUrl       - Broker API URL (e.g., 'http://localhost:5000')
     * @param {string} options.brokerPubKey    - Broker ECDH P-256 公鑰 (Base64 SPKI)
     * @param {string} options.principalId     - 主體 ID (e.g., 'prn_xxx')
     * @param {string} options.taskId          - 任務 ID (e.g., 'task_xxx')
     * @param {string} options.roleId          - 角色 ID (e.g., 'role_reader')
     * @param {boolean} options.verbose        - 是否顯示除錯訊息
     */
    constructor(options) {
        this.brokerUrl = options.brokerUrl;
        this.brokerPubKey = options.brokerPubKey;
        this.principalId = options.principalId;
        this.taskId = options.taskId;
        this.roleId = options.roleId;
        this.verbose = options.verbose || false;

        this.client = null;
        this.sessionInfo = null;
        this.requestCounter = 0;
        this._heartbeatTimer = null;
    }

    /**
     * 初始化：建立 BrokerClient、ECDH 交握、註冊 session
     */
    async init() {
        this.client = new BrokerClient(this.brokerUrl, this.brokerPubKey);

        if (this.verbose) {
            logInfo(`[Governed] 連線到 Broker: ${this.brokerUrl}`);
            logInfo(`[Governed] 主體: ${this.principalId}, 任務: ${this.taskId}, 角色: ${this.roleId}`);
        }

        try {
            this.sessionInfo = await this.client.registerSession(
                this.principalId,
                this.taskId,
                this.roleId
            );

            logInfo(`[Governed] Session 建立成功: ${this.sessionInfo.sessionId}`);
            if (this.verbose) {
                logInfo(`[Governed] Session 到期: ${this.sessionInfo.expiresAt}`);
            }

            // 啟動心跳（每 5 分鐘）
            this._startHeartbeat();

            return this.sessionInfo;
        } catch (e) {
            logError(`[Governed] Session 註冊失敗: ${e.message}`);
            throw new Error(`Broker session 註冊失敗: ${e.message}`);
        }
    }

    /**
     * 透過 Broker 執行工具呼叫（替代直接 executeTool）
     *
     * @param {string} toolName  - JS 工具名稱 (e.g., 'read_file')
     * @param {Object} toolArgs  - 工具參數
     * @param {Object} context   - 執行上下文 { projectRoot, noConfirm, verbose }
     * @returns {Promise<string>} 執行結果（或拒絕訊息）
     */
    async executeTool(toolName, toolArgs, context) {
        // 映射工具名稱 → 能力 ID
        const capabilityId = TOOL_TO_CAPABILITY[toolName];
        if (!capabilityId) {
            return `❌ [Governed] 未知工具: ${toolName}（無對應能力映射）`;
        }

        // 生成唯一冪等鍵
        this.requestCounter++;
        const idempotencyKey = `${this.sessionInfo.sessionId}-${this.requestCounter}-${Date.now()}`;

        // 組裝 payload（保留原始工具參數，加上上下文資訊）
        const payload = {
            tool_name: toolName,
            tool_args: toolArgs,
            project_root: context.projectRoot || '',
        };

        // 描述意圖（用於稽核）
        const intent = this._describeIntent(toolName, toolArgs);

        if (this.verbose) {
            console.log(colorize(`  🔒 [Governed] ${capabilityId} → Broker 裁決中...`, 'yellow'));
        }

        try {
            const result = await this.client.submitRequest(
                capabilityId,
                payload,
                idempotencyKey,
                intent
            );

            // 檢查裁決結果
            if (result.success === false || result.data?.execution_state === 'denied') {
                const reason = result.data?.policy_reason || result.message || '政策拒絕';
                console.log(colorize(`  🚫 [Governed] 裁決拒絕: ${reason}`, 'red'));
                return `❌ [Governed] 操作被拒絕: ${reason}\n` +
                       `   能力: ${capabilityId}\n` +
                       `   工具: ${toolName}`;
            }

            // 成功 — 取得執行結果
            if (result.data?.result_payload) {
                const resultPayload = typeof result.data.result_payload === 'string'
                    ? result.data.result_payload
                    : JSON.stringify(result.data.result_payload);

                if (this.verbose) {
                    console.log(colorize(`  ✅ [Governed] 裁決通過，執行完成`, 'green'));
                }
                return resultPayload;
            }

            // 已排程但結果尚未返回（Phase 2 非同步模式）
            if (result.data?.execution_state === 'dispatched') {
                return `⏳ [Governed] 請求已提交，等待執行結果 (request_id: ${result.data?.request_id})`;
            }

            // 其他成功情況
            return result.data
                ? JSON.stringify(result.data)
                : '✅ [Governed] 操作完成';

        } catch (e) {
            logError(`[Governed] 執行請求失敗: ${e.message}`);
            return `❌ [Governed] Broker 通訊錯誤: ${e.message}`;
        }
    }

    /**
     * 優雅關閉 session
     */
    async close() {
        this._stopHeartbeat();

        if (this.client && this.client.sessionId) {
            try {
                await this.client.closeSession('Agent session ending');
                logInfo('[Governed] Session 已優雅關閉');
            } catch (e) {
                logWarn(`[Governed] Session 關閉失敗（非致命）: ${e.message}`);
            }
        }
    }

    /**
     * 是否已建立 session
     */
    get isActive() {
        return this.client && this.client.sessionId !== null;
    }

    // ── 內部方法 ──

    /**
     * 描述工具呼叫意圖（用於稽核）
     */
    _describeIntent(toolName, args) {
        switch (toolName) {
            case 'read_file':
                return `Read file: ${args.path || '(unknown)'}`;
            case 'write_file':
                return `Write file: ${args.path || '(unknown)'} (mode: ${args.mode || 'rewrite'})`;
            case 'list_directory':
                return `List directory: ${args.path || '(root)'}`;
            case 'search_files':
                return `Search files: pattern=${args.pattern || '(none)'}`;
            case 'search_content':
                return `Search content: pattern=${args.pattern || '(none)'}`;
            case 'run_command':
                return `Execute command: ${(args.command || '').substring(0, 100)}`;
            default:
                return `Tool call: ${toolName}`;
        }
    }

    /**
     * 啟動定期心跳（每 5 分鐘）
     */
    _startHeartbeat() {
        this._stopHeartbeat();
        this._heartbeatTimer = setInterval(async () => {
            try {
                await this.client.heartbeat();
                if (this.verbose) {
                    logInfo('[Governed] 心跳成功');
                }
            } catch (e) {
                logWarn(`[Governed] 心跳失敗: ${e.message}`);
            }
        }, 5 * 60 * 1000); // 5 分鐘

        // 允許 Node.js 在只剩心跳 timer 時正常退出
        if (this._heartbeatTimer.unref) {
            this._heartbeatTimer.unref();
        }
    }

    /**
     * 停止心跳
     */
    _stopHeartbeat() {
        if (this._heartbeatTimer) {
            clearInterval(this._heartbeatTimer);
            this._heartbeatTimer = null;
        }
    }
}

module.exports = { GovernedExecutor, TOOL_TO_CAPABILITY };
