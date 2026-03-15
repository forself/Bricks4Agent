#!/usr/bin/env node
'use strict';

const assert = require('assert');
const path = require('path');

const { AgentLoop } = require('../lib/agent-loop');

const ROOT = path.resolve(__dirname, '..', '..', '..');

function createForbiddenDirectProvider() {
    return {
        name: 'direct-provider-should-not-run',
        baseUrl: 'http://direct-provider.invalid',
        async healthCheck() {
            throw new Error('direct provider healthCheck() must not be called in governed mode');
        },
        supportsToolCalling() {
            throw new Error('direct provider supportsToolCalling() must not be called in governed mode');
        },
        async chat() {
            throw new Error('direct provider chat() must not be called in governed mode');
        },
        async listModels() {
            throw new Error('direct provider listModels() must not be called in governed mode');
        },
    };
}

function createFakeClient() {
    let submitCalls = 0;
    let llmChatCalls = 0;

    return {
        async registerSession() {
            return {
                sessionId: 'sess_test_001',
                scopedToken: 'scoped_token_test_001',
                expiresAt: '2030-01-01T00:00:00Z',
            };
        },
        async listCapabilities() {
            return {
                success: true,
                data: [
                    {
                        capabilityId: 'file.read',
                        route: 'read_file',
                        approvalPolicy: 'auto',
                        riskLevelValue: 0,
                        resourceType: 'file',
                        paramSchema: JSON.stringify({
                            type: 'object',
                            properties: {
                                path: { type: 'string' },
                            },
                            required: ['path'],
                        }),
                    },
                    {
                        capabilityId: 'command.execute',
                        route: 'run_command',
                        approvalPolicy: 'deny',
                        riskLevelValue: 2,
                        resourceType: 'command',
                        paramSchema: JSON.stringify({
                            type: 'object',
                            properties: {
                                command: { type: 'string' },
                            },
                            required: ['command'],
                        }),
                    },
                ],
            };
        },
        async listGrants() {
            return {
                success: true,
                data: [
                    {
                        capabilityId: 'file.read',
                        scopeOverride: JSON.stringify({ paths: [ROOT], routes: ['read_file'] }),
                        remainingQuota: 3,
                        expiresAt: '2030-01-01T00:00:00Z',
                        statusValue: 0,
                    },
                ],
            };
        },
        async getRuntimeSpec() {
            return {
                success: true,
                data: {
                    provider: 'ollama',
                    api_format: 'chat',
                    default_model: 'broker-model',
                    allow_model_override: false,
                    supports_tool_calling: true,
                    streaming_enabled: false,
                    llm_routes: {
                        health: 'http://broker.local:5000/api/v1/llm/health',
                        models: 'http://broker.local:5000/api/v1/llm/models',
                        chat: 'http://broker.local:5000/api/v1/llm/chat',
                    },
                    request_bodies: {
                        health: {
                            method: 'POST',
                            url: 'http://broker.local:5000/api/v1/llm/health',
                            body: { scoped_token: '<scoped token issued for this session>' },
                        },
                        models: {
                            method: 'POST',
                            url: 'http://broker.local:5000/api/v1/llm/models',
                            body: { scoped_token: '<scoped token issued for this session>' },
                        },
                        chat: {
                            method: 'POST',
                            url: 'http://broker.local:5000/api/v1/llm/chat',
                            body: {
                                scoped_token: '<scoped token issued for this session>',
                                model: 'broker-model',
                                messages: [{ role: 'user', content: '<prompt>' }],
                                tools: [],
                                stream: false,
                            },
                        },
                    },
                },
            };
        },
        async llmHealth() {
            return {
                success: true,
                data: { healthy: true },
            };
        },
        async llmModels() {
            return {
                success: true,
                data: [
                    { name: 'broker-model', size: 123456 },
                    { name: 'broker-model-2', size: 654321 },
                ],
            };
        },
        async llmChat(body) {
            llmChatCalls += 1;
            assert.strictEqual(body.model, 'broker-model');
            return {
                success: true,
                data: {
                    content: 'broker chat ok',
                    tool_calls: [],
                    thinking: '',
                    done: true,
                    model: 'broker-model',
                    total_duration: 0,
                    eval_count: 12,
                },
            };
        },
        async submitRequest() {
            submitCalls += 1;
            return {
                success: true,
                data: {
                    execution_state: 'Succeeded',
                    result_payload: 'ok',
                },
            };
        },
        async heartbeat() {
            return { success: true };
        },
        async closeSession() {
            return { success: true };
        },
        getSubmitCalls() {
            return submitCalls;
        },
        getLlmChatCalls() {
            return llmChatCalls;
        },
    };
}

async function main() {
    const fakeClient = createFakeClient();
    const agent = new AgentLoop({
        model: 'user-requested-model',
        provider: createForbiddenDirectProvider(),
        projectRoot: ROOT,
        stream: false,
        governed: {
            brokerUrl: 'http://broker.local:5000',
            brokerPubKey: 'fake-pub-key',
            principalId: 'prn_test',
            taskId: 'task_test',
            roleId: 'role_reader',
            clientFactory: () => fakeClient,
        },
    });

    await agent.init();

    assert.strictEqual(agent.provider.name, 'broker-governed');
    assert.strictEqual(agent.model, 'broker-model');

    const prompt = agent.messages[0].content;
    const toolNames = agent.getAvailableToolDefinitions().map((def) => def.function.name);
    const promptContext = agent.getGovernedPromptContext();

    assert.deepStrictEqual(toolNames, ['read_file']);
    assert(agent.getAvailableToolDescriptions().includes('read_file'));
    assert(!agent.getAvailableToolDescriptions().includes('run_command'));

    assert(prompt.includes('Governed Broker Contract'));
    assert(prompt.includes('LLM Runtime Contract'));
    assert(prompt.includes('POST http://broker.local:5000/api/v1/execution-requests/submit'));
    assert(prompt.includes('POST http://broker.local:5000/api/v1/runtime/spec'));
    assert(prompt.includes('POST http://broker.local:5000/api/v1/llm/chat'));
    assert(prompt.includes('"capability_id": "file.read"'));
    assert(prompt.includes('"route": "read_file"'));
    assert(prompt.includes('"paths"'));
    assert(prompt.includes('"routes"'));
    assert(prompt.includes('"model": "broker-model"'));

    assert.strictEqual(promptContext.session.sessionId, 'sess_test_001');
    assert.deepStrictEqual(promptContext.allowedCapabilities.map((item) => item.capabilityId), ['file.read']);
    assert.deepStrictEqual(promptContext.allowedCapabilities[0].scopeOverride, { paths: [ROOT], routes: ['read_file'] });
    assert.strictEqual(promptContext.runtimeSpec.defaultModel, 'broker-model');
    assert.strictEqual(promptContext.runtimeSpec.resolvedModel, 'broker-model');
    assert.strictEqual(promptContext.runtimeSpec.allowModelOverride, false);

    const models = await agent.provider.listModels();
    assert.deepStrictEqual(models.map((model) => model.name), ['broker-model', 'broker-model-2']);

    const chatResult = await agent.provider.chat({
        model: 'user-requested-model',
        messages: [{ role: 'user', content: 'hello' }],
        tools: [],
        stream: false,
    });
    assert.strictEqual(chatResult.content, 'broker chat ok');
    assert.strictEqual(fakeClient.getLlmChatCalls(), 1);

    const denied = await agent.governedExecutor.executeTool('run_command', { command: 'dir' }, {
        projectRoot: ROOT,
        noConfirm: true,
        verbose: false,
    });
    assert(denied.includes('capability denied'));
    assert.strictEqual(fakeClient.getSubmitCalls(), 0);

    const allowed = await agent.governedExecutor.executeTool('read_file', { path: './README.md' }, {
        projectRoot: ROOT,
        noConfirm: true,
        verbose: false,
    });
    assert.strictEqual(allowed, 'ok');
    assert.strictEqual(fakeClient.getSubmitCalls(), 1);

    await agent.close();
    console.log('Governed mode tests passed.');
}

main().catch((error) => {
    console.error(error);
    process.exit(1);
});
