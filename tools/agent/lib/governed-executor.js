'use strict';

/**
 * Governed Executor — 受控執行模式
 *
 * 將工具呼叫路由到 Broker 中介核心，透過加密通道提交執行請求。
 * Broker 負責政策裁決（PEP 16 步流程），只有被允許的操作才會執行。
 *
 * 生命週期：
 *   1. init()        → ECDH 金鑰交換、建立加密 session
 *   2. executeTool() → 每次工具呼叫 → 加密提交 → 裁決 → 回傳結果
 *   3. close()       → 優雅關閉 session、清零金鑰
 */
const { BrokerClient } = require('./broker-client');
const {
    TOOL_TO_CAPABILITY,
    capabilityIdForTool,
    getToolDefinitions,
    getToolDescriptions,
    getToolNamesForCapabilities,
} = require('./tool-registry');
const { logInfo, logWarn, logError, colorize } = require('./utils');

const BROKER_ROUTES = {
    register: '/api/v1/sessions/register',
    submit: '/api/v1/execution-requests/submit',
    heartbeat: '/api/v1/sessions/heartbeat',
    close: '/api/v1/sessions/close',
    capabilitiesList: '/api/v1/capabilities/list',
    grantsList: '/api/v1/grants/list',
};

const RISK_LEVEL_LABELS = ['Low', 'Medium', 'High', 'Critical'];
const GRANT_STATUS_LABELS = ['Active', 'Expired', 'Revoked', 'Exhausted'];

class GovernedExecutor {
    /**
     * @param {Object} options
     * @param {string} options.brokerUrl       - Broker API URL (e.g., 'http://localhost:5000')
     * @param {string} options.brokerPubKey    - Broker ECDH P-256 公鑰 (Base64 SPKI)
     * @param {string} options.principalId     - 主體 ID (e.g., 'prn_xxx')
     * @param {string} options.taskId          - 任務 ID (e.g., 'task_xxx')
     * @param {string} options.roleId          - 角色 ID (e.g., 'role_reader')
     * @param {boolean} options.verbose        - 是否顯示除錯訊息
     * @param {(brokerUrl: string, brokerPubKey: string) => BrokerClient} [options.clientFactory]
     */
    constructor(options) {
        this.brokerUrl = options.brokerUrl;
        this.brokerPubKey = options.brokerPubKey;
        this.principalId = options.principalId;
        this.taskId = options.taskId;
        this.roleId = options.roleId;
        this.verbose = options.verbose || false;
        this.clientFactory = options.clientFactory || ((brokerUrl, brokerPubKey) => new BrokerClient(brokerUrl, brokerPubKey));

        this.client = null;
        this.sessionInfo = null;
        this.requestCounter = 0;
        this.capabilities = [];
        this.grants = [];
        this.allowedCapabilities = [];
        this.promptContext = null;
        this._heartbeatTimer = null;
    }

    /**
     * 初始化：建立 BrokerClient、ECDH 交握、註冊 session，並載入目前 session 的能力範圍
     */
    async init() {
        this.client = this.clientFactory(this.brokerUrl, this.brokerPubKey);

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

            await this._loadGovernanceSnapshot();

            logInfo(`[Governed] Session 建立成功: ${this.sessionInfo.sessionId}`);
            if (this.verbose) {
                logInfo(`[Governed] Session 到期: ${this.sessionInfo.expiresAt}`);
                logInfo(`[Governed] 已載入 ${this.allowedCapabilities.length} 個可請求 capability`);
            }

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
        const capabilityId = capabilityIdForTool(toolName);
        if (!capabilityId) {
            return `❌ [Governed] 未知工具: ${toolName}（無對應 capability 映射）`;
        }

        const grantedCapabilityIds = this.getAllowedCapabilityIds();
        if (this.promptContext && grantedCapabilityIds.length === 0) {
            return '❌ [Governed] 目前這個 session 沒有任何可請求 capability。';
        }
        if (!grantedCapabilityIds.includes(capabilityId)) {
            return `❌ [Governed] Capability 未授權: ${capabilityId}\n` +
                   `   工具: ${toolName}\n` +
                   `   目前可請求: ${grantedCapabilityIds.join(', ')}`;
        }

        this.requestCounter++;
        const idempotencyKey = `${this.sessionInfo.sessionId}-${this.requestCounter}-${Date.now()}`;

        const payload = {
            tool_name: toolName,
            tool_args: toolArgs,
            project_root: context.projectRoot || '',
        };

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

            if (result.success === false || result.data?.execution_state === 'denied') {
                const reason = result.data?.policy_reason || result.message || '政策拒絕';
                console.log(colorize(`  🚫 [Governed] 裁決拒絕: ${reason}`, 'red'));
                return `❌ [Governed] 操作被拒絕: ${reason}\n` +
                       `   能力: ${capabilityId}\n` +
                       `   工具: ${toolName}`;
            }

            if (result.data?.result_payload) {
                const resultPayload = typeof result.data.result_payload === 'string'
                    ? result.data.result_payload
                    : JSON.stringify(result.data.result_payload);

                if (this.verbose) {
                    console.log(colorize('  ✅ [Governed] 裁決通過，執行完成', 'green'));
                }
                return resultPayload;
            }

            if (result.data?.execution_state === 'dispatched') {
                return `⏳ [Governed] 請求已提交，等待執行結果 (request_id: ${result.data?.request_id})`;
            }

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
        return !!(this.client && this.client.sessionId !== null);
    }

    getAllowedCapabilityIds() {
        return this.allowedCapabilities.map((item) => item.capabilityId);
    }

    getAllowedToolDefinitions() {
        return getToolDefinitions({ capabilityIds: this.getAllowedCapabilityIds() });
    }

    getAllowedToolDescriptions() {
        return getToolDescriptions({ capabilityIds: this.getAllowedCapabilityIds() });
    }

    getPromptContext() {
        return this.promptContext;
    }

    // ── 內部方法 ──

    async _loadGovernanceSnapshot() {
        const [capabilitiesResponse, grantsResponse] = await Promise.all([
            this.client.listCapabilities(),
            this.client.listGrants(),
        ]);

        this.capabilities = toArray(capabilitiesResponse)
            .map(normalizeCapability)
            .filter((capability) => capability.capabilityId);
        this.grants = toArray(grantsResponse)
            .map(normalizeGrant)
            .filter((grant) => grant.capabilityId);

        const capabilityMap = new Map(this.capabilities.map((capability) => [capability.capabilityId, capability]));
        this.allowedCapabilities = this.grants.map((grant) => {
            const capability = capabilityMap.get(grant.capabilityId) || {
                capabilityId: grant.capabilityId,
                route: getToolNamesForCapabilities([grant.capabilityId])[0] || '',
                approvalPolicy: 'unknown',
                riskLevel: 'Unknown',
                resourceType: 'unknown',
                paramSchema: {},
            };

            return {
                capabilityId: grant.capabilityId,
                toolName: capability.route || getToolNamesForCapabilities([grant.capabilityId])[0] || '',
                route: capability.route || '',
                resourceType: capability.resourceType || '',
                approvalPolicy: capability.approvalPolicy || 'unknown',
                riskLevel: capability.riskLevel || 'Unknown',
                ttlSeconds: capability.ttlSeconds ?? null,
                paramSchema: capability.paramSchema || {},
                scopeOverride: grant.scopeOverrideObject || {},
                scopeOverrideRaw: grant.scopeOverrideRaw,
                remainingQuota: grant.remainingQuota,
                expiresAt: grant.expiresAt,
                grantStatus: grant.status,
            };
        });

        this.promptContext = this._buildPromptContext();
    }

    _buildPromptContext() {
        return {
            brokerUrl: this.brokerUrl,
            brokerRoutes: {
                register: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.register),
                submit: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.submit),
                heartbeat: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.heartbeat),
                close: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.close),
                capabilitiesList: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.capabilitiesList),
                grantsList: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.grantsList),
            },
            session: {
                principalId: this.principalId,
                taskId: this.taskId,
                roleId: this.roleId,
                sessionId: this.sessionInfo?.sessionId || '',
                expiresAt: this.sessionInfo?.expiresAt || '',
            },
            allowedCapabilities: this.allowedCapabilities.map((item) => ({
                capabilityId: item.capabilityId,
                toolName: item.toolName,
                route: item.route,
                approvalPolicy: item.approvalPolicy,
                riskLevel: item.riskLevel,
                resourceType: item.resourceType,
                scopeOverride: item.scopeOverride,
                remainingQuota: item.remainingQuota,
                expiresAt: item.expiresAt,
                grantStatus: item.grantStatus,
                paramSchema: item.paramSchema,
            })),
            requestBodies: {
                registerOuter: {
                    method: 'POST',
                    url: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.register),
                    body: {
                        v: 1,
                        client_ephemeral_pub: '<base64-spki>',
                        envelope: {
                            alg: 'ECDH-ES+A256GCM',
                            seq: 0,
                            nonce: '<base64>',
                            ciphertext: '<base64>',
                            tag: '<base64>',
                        },
                    },
                    plaintext: {
                        principal_id: this.principalId,
                        task_id: this.taskId,
                        role_id: this.roleId,
                    },
                },
                submitOuter: {
                    method: 'POST',
                    url: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.submit),
                    body: {
                        v: 1,
                        session_id: this.sessionInfo?.sessionId || '<session_id>',
                        envelope: {
                            alg: 'A256GCM',
                            seq: '<monotonic-sequence>',
                            nonce: '<base64>',
                            ciphertext: '<base64>',
                            tag: '<base64>',
                        },
                    },
                    plaintext: {
                        scoped_token: '<scoped token issued for this session>',
                        capability_id: this.allowedCapabilities[0]?.capabilityId || '<capability_id>',
                        intent: 'Describe the specific action requested',
                        payload: {
                            tool_name: this.allowedCapabilities[0]?.toolName || '<tool_name>',
                            tool_args: this._sampleToolArgs(this.allowedCapabilities[0]),
                            project_root: '<workspace-root>',
                        },
                        idempotency_key: '<unique-request-key>',
                    },
                },
                heartbeatPlaintext: {
                    method: 'POST',
                    url: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.heartbeat),
                    body: { scoped_token: '<scoped token issued for this session>' },
                },
                grantsListPlaintext: {
                    method: 'POST',
                    url: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.grantsList),
                    body: { scoped_token: '<scoped token issued for this session>' },
                },
                capabilitiesListPlaintext: {
                    method: 'POST',
                    url: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.capabilitiesList),
                    body: { scoped_token: '<scoped token issued for this session>', filter: null },
                },
                closePlaintext: {
                    method: 'POST',
                    url: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.close),
                    body: {
                        scoped_token: '<scoped token issued for this session>',
                        reason: 'Agent session ending',
                    },
                },
            },
        };
    }

    _sampleToolArgs(capability) {
        const properties = capability?.paramSchema?.properties || {};
        const args = {};

        for (const [key, schema] of Object.entries(properties)) {
            switch (schema?.type) {
                case 'number':
                case 'integer':
                    args[key] = 0;
                    break;
                case 'boolean':
                    args[key] = false;
                    break;
                case 'array':
                    args[key] = [];
                    break;
                case 'object':
                    args[key] = {};
                    break;
                default:
                    args[key] = `<${key}>`;
                    break;
            }
        }

        return args;
    }

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
        }, 5 * 60 * 1000);

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

function toArray(response) {
    if (Array.isArray(response)) {
        return response;
    }
    if (Array.isArray(response?.data)) {
        return response.data;
    }
    return [];
}

function readField(source, names, fallback = null) {
    for (const name of names) {
        if (source && Object.prototype.hasOwnProperty.call(source, name) && source[name] !== undefined) {
            return source[name];
        }
    }
    return fallback;
}

function parseJson(value, fallback) {
    if (value == null || value === '') {
        return fallback;
    }
    if (typeof value === 'object') {
        return value;
    }
    try {
        return JSON.parse(value);
    } catch (_) {
        return fallback;
    }
}

function normalizeCapability(capability) {
    const capabilityId = readField(capability, ['capabilityId', 'capability_id'], '');
    const route = readField(capability, ['route'], '');
    const approvalPolicy = readField(capability, ['approvalPolicy', 'approval_policy'], 'unknown');
    const riskLevelValue = Number(readField(capability, ['riskLevelValue', 'risk_level_value'], -1));

    return {
        capabilityId,
        route,
        approvalPolicy,
        resourceType: readField(capability, ['resourceType', 'resource_type'], ''),
        ttlSeconds: readField(capability, ['ttlSeconds', 'ttl_seconds'], null),
        riskLevel: RISK_LEVEL_LABELS[riskLevelValue] || 'Unknown',
        paramSchema: parseJson(readField(capability, ['paramSchema', 'param_schema'], {}), {}),
    };
}

function normalizeGrant(grant) {
    const statusValue = Number(readField(grant, ['statusValue', 'status_value'], -1));
    const scopeOverrideRaw = readField(grant, ['scopeOverride', 'scope_override'], '{}');

    return {
        capabilityId: readField(grant, ['capabilityId', 'capability_id'], ''),
        scopeOverrideRaw,
        scopeOverrideObject: parseJson(scopeOverrideRaw, {}),
        remainingQuota: readField(grant, ['remainingQuota', 'remaining_quota'], null),
        expiresAt: readField(grant, ['expiresAt', 'expires_at'], ''),
        status: GRANT_STATUS_LABELS[statusValue] || 'Unknown',
    };
}

function buildRouteUrl(baseUrl, routePath) {
    return `${baseUrl.replace(/\/$/, '')}${routePath}`;
}

module.exports = { GovernedExecutor, TOOL_TO_CAPABILITY };
