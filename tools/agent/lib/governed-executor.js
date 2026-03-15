'use strict';

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
    runtimeSpec: '/api/v1/runtime/spec',
    llmHealth: '/api/v1/llm/health',
    llmModels: '/api/v1/llm/models',
    llmChat: '/api/v1/llm/chat',
};

const RISK_LEVEL_LABELS = ['Low', 'Medium', 'High', 'Critical'];
const GRANT_STATUS_LABELS = ['Active', 'Expired', 'Revoked', 'Exhausted'];

class GovernedExecutor {
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
        this.runtimeSpec = null;
        this.promptContext = null;
        this._heartbeatTimer = null;
    }

    get name() {
        return 'broker-governed';
    }

    get baseUrl() {
        return this.brokerUrl;
    }

    async init() {
        this.client = this.clientFactory(this.brokerUrl, this.brokerPubKey);

        if (this.verbose) {
            logInfo(`[Governed] broker=${this.brokerUrl}`);
            logInfo(`[Governed] principal=${this.principalId} task=${this.taskId} role=${this.roleId}`);
        }

        try {
            this.sessionInfo = await this.client.registerSession(
                this.principalId,
                this.taskId,
                this.roleId
            );

            await this._loadGovernanceSnapshot();
            await this._loadRuntimeSpec();

            this.promptContext = this._buildPromptContext();

            logInfo(`[Governed] session=${this.sessionInfo.sessionId}`);
            if (this.verbose) {
                logInfo(`[Governed] session_expires_at=${this.sessionInfo.expiresAt}`);
                logInfo(`[Governed] granted_capabilities=${this.allowedCapabilities.length}`);
                if (this.runtimeSpec) {
                    logInfo(`[Governed] llm_provider=${this.runtimeSpec.provider} model=${this.resolveModel()}`);
                }
            }

            this._startHeartbeat();
            return this.sessionInfo;
        } catch (e) {
            logError(`[Governed] init failed: ${e.message}`);
            throw new Error(`Broker session init failed: ${e.message}`);
        }
    }

    async executeTool(toolName, toolArgs, context) {
        const capabilityId = capabilityIdForTool(toolName);
        if (!capabilityId) {
            return `[Governed] unsupported tool: ${toolName}`;
        }

        const grantedCapabilityIds = this.getAllowedCapabilityIds();
        if (this.promptContext && grantedCapabilityIds.length === 0) {
            return '[Governed] session has no granted capabilities.';
        }
        if (!grantedCapabilityIds.includes(capabilityId)) {
            return `[Governed] capability denied: ${capabilityId}\n` +
                `tool: ${toolName}\n` +
                `granted: ${grantedCapabilityIds.join(', ')}`;
        }

        this.requestCounter++;
        const idempotencyKey = `${this.sessionInfo.sessionId}-${this.requestCounter}-${Date.now()}`;
        const payload = {
            route: toolName,
            args: toolArgs,
            project_root: context.projectRoot || '',
        };
        const intent = this._describeIntent(toolName, toolArgs);

        if (this.verbose) {
            console.log(colorize(`  [Governed] ${capabilityId} -> broker`, 'yellow'));
        }

        try {
            const result = await this.client.submitRequest(
                capabilityId,
                payload,
                idempotencyKey,
                intent
            );

            if (result.success === false || result.data?.execution_state === 'denied') {
                const reason = result.data?.policy_reason || result.message || 'Request denied';
                console.log(colorize(`  [Governed] denied: ${reason}`, 'red'));
                return `[Governed] request denied: ${reason}\ncapability: ${capabilityId}\ntool: ${toolName}`;
            }

            if (result.data?.result_payload) {
                const resultPayload = typeof result.data.result_payload === 'string'
                    ? result.data.result_payload
                    : JSON.stringify(result.data.result_payload);

                if (this.verbose) {
                    console.log(colorize('  [Governed] tool request succeeded', 'green'));
                }
                return resultPayload;
            }

            if (result.data?.execution_state === 'dispatched') {
                return `[Governed] request dispatched (request_id: ${result.data?.request_id})`;
            }

            return result.data
                ? JSON.stringify(result.data)
                : '[Governed] request completed';
        } catch (e) {
            logError(`[Governed] tool request failed: ${e.message}`);
            return `[Governed] broker error: ${e.message}`;
        }
    }

    async healthCheck() {
        try {
            const response = await this.client.llmHealth();
            const healthy = response?.data?.healthy;
            return healthy === true;
        } catch (_) {
            return false;
        }
    }

    async listModels() {
        const response = await this.client.llmModels();
        const models = Array.isArray(response?.data) ? response.data : [];
        return models.map((model) => ({
            name: readField(model, ['name'], ''),
            size: readField(model, ['size'], null),
        })).filter((model) => model.name);
    }

    supportsToolCalling() {
        return !!this.runtimeSpec?.supportsToolCalling;
    }

    resolveModel(requestedModel) {
        if (!this.runtimeSpec) {
            return requestedModel || '';
        }
        if (this.runtimeSpec.allowModelOverride && requestedModel) {
            return requestedModel;
        }
        return this.runtimeSpec.defaultModel || requestedModel || '';
    }

    async chat(params) {
        const model = this.resolveModel(params.model);
        const payload = {
            model,
            messages: params.messages,
            tools: params.tools || [],
            stream: false,
        };

        const startTime = Date.now();
        if (params.onHeartbeat) {
            params.onHeartbeat({
                elapsed: 0,
                sinceLastToken: 0,
                status: 'thinking',
                noDataCount: 0,
                runningModels: null,
            });
        }

        const response = await this.client.llmChat(payload);
        const data = response?.data || {};
        const content = readField(data, ['content'], '');
        const toolCalls = normalizeToolCalls(readField(data, ['tool_calls', 'toolCalls'], []));
        const result = {
            content,
            toolCalls,
            thinking: readField(data, ['thinking'], ''),
            done: readField(data, ['done'], true),
            model: readField(data, ['model'], model),
            totalDuration: readField(data, ['total_duration', 'totalDuration'], 0),
            evalCount: readField(data, ['eval_count', 'evalCount'], 0),
        };

        if (params.onToken && content) {
            params.onToken(content);
        }
        if (params.onHeartbeat) {
            params.onHeartbeat({
                elapsed: Date.now() - startTime,
                sinceLastToken: 0,
                status: 'done',
            });
        }

        return result;
    }

    async close() {
        this._stopHeartbeat();

        if (this.client && this.client.sessionId) {
            try {
                await this.client.closeSession('Agent session ending');
                logInfo('[Governed] session closed');
            } catch (e) {
                logWarn(`[Governed] close failed: ${e.message}`);
            }
        }
    }

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

    async _loadGovernanceSnapshot() {
        const capabilitiesResponse = await this.client.listCapabilities();
        const grantsResponse = await this.client.listGrants();

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
    }

    async _loadRuntimeSpec() {
        const response = await this.client.getRuntimeSpec();
        const data = response?.data || {};
        this.runtimeSpec = {
            source: readField(data, ['source'], 'broker_default'),
            provider: readField(data, ['provider'], 'ollama'),
            apiFormat: readField(data, ['api_format', 'apiFormat'], 'chat'),
            defaultModel: readField(data, ['default_model', 'defaultModel'], ''),
            allowModelOverride: !!readField(data, ['allow_model_override', 'allowModelOverride'], false),
            supportsToolCalling: !!readField(data, ['supports_tool_calling', 'supportsToolCalling'], true),
            streamingEnabled: !!readField(data, ['streaming_enabled', 'streamingEnabled'], false),
            taskId: readField(data, ['task_id', 'taskId'], ''),
            taskType: readField(data, ['task_type', 'taskType'], ''),
            assignedRoleId: readField(data, ['assigned_role_id', 'assignedRoleId'], ''),
            scopeDescriptor: readField(data, ['scope_descriptor', 'scopeDescriptor'], '{}'),
            capabilityIds: readField(data, ['capability_ids', 'capabilityIds'], []),
            llmRoutes: readField(data, ['llm_routes', 'llmRoutes'], {}),
            requestBodies: readField(data, ['request_bodies', 'requestBodies'], {}),
        };
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
                runtimeSpec: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.runtimeSpec),
                llmHealth: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.llmHealth),
                llmModels: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.llmModels),
                llmChat: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.llmChat),
            },
            session: {
                principalId: this.principalId,
                taskId: this.taskId,
                roleId: this.roleId,
                sessionId: this.sessionInfo?.sessionId || '',
                expiresAt: this.sessionInfo?.expiresAt || '',
            },
            runtimeSpec: this.runtimeSpec ? {
                source: this.runtimeSpec.source,
                provider: this.runtimeSpec.provider,
                apiFormat: this.runtimeSpec.apiFormat,
                defaultModel: this.runtimeSpec.defaultModel,
                resolvedModel: this.resolveModel(),
                allowModelOverride: this.runtimeSpec.allowModelOverride,
                supportsToolCalling: this.runtimeSpec.supportsToolCalling,
                streamingEnabled: this.runtimeSpec.streamingEnabled,
                taskId: this.runtimeSpec.taskId,
                taskType: this.runtimeSpec.taskType,
                assignedRoleId: this.runtimeSpec.assignedRoleId,
                scopeDescriptor: this.runtimeSpec.scopeDescriptor,
                capabilityIds: this.runtimeSpec.capabilityIds,
                llmRoutes: this.runtimeSpec.llmRoutes,
            } : null,
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
                            route: this.allowedCapabilities[0]?.toolName || '<route>',
                            args: this._sampleToolArgs(this.allowedCapabilities[0]),
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
                runtimeSpecPlaintext: {
                    method: 'POST',
                    url: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.runtimeSpec),
                    body: { scoped_token: '<scoped token issued for this session>' },
                },
                llmHealthPlaintext: this.runtimeSpec?.requestBodies?.health || {
                    method: 'POST',
                    url: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.llmHealth),
                    body: { scoped_token: '<scoped token issued for this session>' },
                },
                llmModelsPlaintext: this.runtimeSpec?.requestBodies?.models || {
                    method: 'POST',
                    url: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.llmModels),
                    body: { scoped_token: '<scoped token issued for this session>' },
                },
                llmChatPlaintext: this.runtimeSpec?.requestBodies?.chat || {
                    method: 'POST',
                    url: buildRouteUrl(this.brokerUrl, BROKER_ROUTES.llmChat),
                    body: {
                        scoped_token: '<scoped token issued for this session>',
                        model: this.resolveModel(),
                        messages: [
                            { role: 'system', content: 'You are a governed agent.' },
                            { role: 'user', content: '<prompt>' },
                        ],
                        tools: [],
                        stream: false,
                    },
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

    _startHeartbeat() {
        this._stopHeartbeat();
        this._heartbeatTimer = setInterval(async () => {
            try {
                await this.client.heartbeat();
                if (this.verbose) {
                    logInfo('[Governed] heartbeat ok');
                }
            } catch (e) {
                logWarn(`[Governed] heartbeat failed: ${e.message}`);
            }
        }, 5 * 60 * 1000);

        if (this._heartbeatTimer.unref) {
            this._heartbeatTimer.unref();
        }
    }

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

function normalizeToolCalls(toolCalls) {
    if (!Array.isArray(toolCalls)) {
        return [];
    }

    return toolCalls.map((toolCall) => {
        const fn = toolCall.function || {};
        let args = fn.arguments || {};
        if (typeof args === 'string') {
            try {
                args = JSON.parse(args);
            } catch (_) {
                args = { _raw: args };
            }
        }

        const normalized = {
            function: {
                name: fn.name || '',
                arguments: args,
            },
        };
        if (toolCall.id) {
            normalized.id = toolCall.id;
        }
        return normalized;
    });
}

function buildRouteUrl(baseUrl, routePath) {
    return `${baseUrl.replace(/\/$/, '')}${routePath}`;
}

module.exports = { GovernedExecutor, TOOL_TO_CAPABILITY };
