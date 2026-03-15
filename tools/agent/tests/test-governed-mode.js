#!/usr/bin/env node
'use strict';

const assert = require('assert');
const path = require('path');

const { AgentLoop } = require('../lib/agent-loop');

const ROOT = path.resolve(__dirname, '..', '..', '..');

function createFakeProvider() {
    return {
        name: 'fake',
        baseUrl: 'http://fake-provider.local',
        async healthCheck() {
            return true;
        },
        supportsToolCalling() {
            return true;
        },
        async chat() {
            throw new Error('chat() should not be called in this unit test');
        },
    };
}

function createFakeClient() {
    let submitCalls = 0;

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
    };
}

async function main() {
    const fakeClient = createFakeClient();
    const agent = new AgentLoop({
        model: 'fake-model',
        provider: createFakeProvider(),
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

    const prompt = agent.messages[0].content;
    const toolNames = agent.getAvailableToolDefinitions().map((def) => def.function.name);
    const promptContext = agent.getGovernedPromptContext();

    assert.deepStrictEqual(toolNames, ['read_file']);
    assert(agent.getAvailableToolDescriptions().includes('read_file'));
    assert(!agent.getAvailableToolDescriptions().includes('run_command'));

    assert(prompt.includes('Governed Broker Contract'));
    assert(prompt.includes('POST http://broker.local:5000/api/v1/execution-requests/submit'));
    assert(prompt.includes('"capability_id": "file.read"'));
    assert(prompt.includes('"route": "read_file"'));
    assert(prompt.includes('"args"'));
    assert(prompt.includes('"paths"'));
    assert(prompt.includes('"routes"'));
    assert(prompt.includes('只可請求目前 grants 允許的 capability_id'));

    assert.strictEqual(promptContext.session.sessionId, 'sess_test_001');
    assert.deepStrictEqual(promptContext.allowedCapabilities.map((item) => item.capabilityId), ['file.read']);
    assert.deepStrictEqual(promptContext.allowedCapabilities[0].scopeOverride, { paths: [ROOT], routes: ['read_file'] });

    const denied = await agent.governedExecutor.executeTool('run_command', { command: 'dir' }, {
        projectRoot: ROOT,
        noConfirm: true,
        verbose: false,
    });
    assert(denied.includes('Capability 未授權'));
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
