#!/usr/bin/env node
'use strict';

const assert = require('assert');
const crypto = require('crypto');
const path = require('path');
const util = require('util');

const { AgentLoop } = require('../lib/agent-loop');
const { BrokerClient } = require('../lib/broker-client');
const { LineListener } = require('../lib/line-listener');
const {
    GENERATION_BASE_PROMPT,
    GENERATION_CONTINUE_REMINDER,
    GOVERNED_RESPONSE_REMINDER,
    isGenerationTask,
} = require('../lib/system-prompt');

const ROOT = path.resolve(__dirname, '..', '..', '..');
// A per-run value, so a match in the prompt can only come from the governed config.
const TEST_REGISTRATION_SECRET = `test-registration-${crypto.randomBytes(24).toString('base64url')}`;

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

function brokerHttpError(status, message) {
    const error = new Error(`Broker error ${status}: ${message}`);
    error.status = status;
    error.brokerMessage = message;
    return error;
}

function createFakeClient() {
    let submitCalls = 0;
    let llmChatCalls = 0;
    let registerCalls = 0;
    let heartbeatCalls = 0;
    let closeCalls = 0;
    const registeredSecrets = [];
    const pendingFailures = { submit: [], llmChat: [], heartbeat: [], register: [] };

    function takeFailure(kind) {
        const failure = pendingFailures[kind].shift();
        if (failure) {
            throw failure;
        }
    }

    return {
        tokenExpiresAt: null,
        async registerSession(principalId, taskId, roleId, registrationSecret) {
            registerCalls += 1;
            registeredSecrets.push(registrationSecret);
            takeFailure('register');
            return {
                sessionId: `sess_test_00${registerCalls}`,
                scopedToken: `scoped_token_test_00${registerCalls}`,
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
            takeFailure('llmChat');
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
            takeFailure('submit');
            return {
                success: true,
                data: {
                    execution_state: 'Succeeded',
                    result_payload: 'ok',
                },
            };
        },
        async heartbeat() {
            heartbeatCalls += 1;
            takeFailure('heartbeat');
            return { success: true };
        },
        async closeSession() {
            closeCalls += 1;
            return { success: true };
        },
        failNext(kind, error) {
            pendingFailures[kind].push(error);
        },
        getSubmitCalls() {
            return submitCalls;
        },
        getLlmChatCalls() {
            return llmChatCalls;
        },
        getRegisterCalls() {
            return registerCalls;
        },
        getRegisteredSecrets() {
            return registeredSecrets.slice();
        },
        getHeartbeatCalls() {
            return heartbeatCalls;
        },
        getCloseCalls() {
            return closeCalls;
        },
    };
}

/** AES-256-GCM helpers for the stubbed broker below (the wire format of broker-client.js). */
function sealForTest(key, plaintext, aad) {
    const nonce = crypto.randomBytes(12);
    const cipher = crypto.createCipheriv('aes-256-gcm', key, nonce);
    cipher.setAAD(Buffer.from(aad));
    const ciphertext = Buffer.concat([cipher.update(Buffer.from(plaintext)), cipher.final()]);
    return {
        nonce: nonce.toString('base64'),
        ciphertext: ciphertext.toString('base64'),
        tag: cipher.getAuthTag().toString('base64'),
    };
}

function openForTest(key, envelope, aad) {
    const decipher = crypto.createDecipheriv('aes-256-gcm', key, Buffer.from(envelope.nonce, 'base64'));
    decipher.setAAD(Buffer.from(aad));
    decipher.setAuthTag(Buffer.from(envelope.tag, 'base64'));
    return Buffer.concat([
        decipher.update(Buffer.from(envelope.ciphertext, 'base64')),
        decipher.final(),
    ]).toString();
}

/**
 * BrokerClient against a stubbed transport that speaks the encrypted envelope protocol:
 * a request queued behind a heartbeat is sent with the renewed token, and an encrypted 401
 * surfaces its status and the broker's message.
 */
async function testBrokerClientTokenRenewal() {
    const client = new BrokerClient('http://broker.invalid', 'unused');
    client.sessionId = 'sess_stub';
    client.sessionKey = crypto.randomBytes(32);
    client.scopedToken = 'token-1';

    const seen = [];
    client._postRaw = async (routePath, body) => {
        const envelope = body.envelope;
        const request = JSON.parse(openForTest(
            client.sessionKey,
            envelope,
            `req:${body.session_id}${envelope.seq}${routePath}`
        ));
        seen.push({ path: routePath, seq: envelope.seq, token: request.scoped_token });

        let status = 200;
        let payload = { success: true, data: [] };
        if (routePath === '/api/v1/sessions/heartbeat') {
            payload = {
                success: true,
                data: {
                    session_id: 'sess_stub',
                    scoped_token: 'token-2',
                    token_expires_at: '2030-01-01T00:15:00Z',
                    session_expires_at: '2030-01-01T01:00:00Z',
                },
            };
        } else if (request.scoped_token === 'token-expired') {
            status = 401;
            payload = { success: false, message: 'Session expired.' };
        }

        const sealed = sealForTest(
            client.sessionKey,
            JSON.stringify(payload),
            `resp:${body.session_id}${envelope.seq}${routePath}`
        );
        const parsed = { v: 1, envelope: { alg: 'A256GCM', seq: envelope.seq, ...sealed } };
        return { status, ok: status >= 200 && status < 300, text: JSON.stringify(parsed), parsed };
    };

    // Queue a heartbeat and a request together; the request must go out with the renewed token.
    const [heartbeat, grants] = await Promise.all([client.heartbeat(), client.listGrants()]);
    assert.strictEqual(heartbeat.data.scoped_token, 'token-2');
    assert.deepStrictEqual(grants.data, []);
    assert.deepStrictEqual(seen.map((item) => item.path), ['/api/v1/sessions/heartbeat', '/api/v1/grants/list']);
    assert.deepStrictEqual(seen.map((item) => item.seq), [1, 2]);
    assert.deepStrictEqual(seen.map((item) => item.token), ['token-1', 'token-2']);
    assert.strictEqual(client.scopedToken, 'token-2');
    assert.strictEqual(client.tokenExpiresAt, '2030-01-01T00:15:00Z');
    assert.strictEqual(client.sessionExpiresAt, '2030-01-01T01:00:00Z');

    client.scopedToken = 'token-expired';
    await assert.rejects(
        () => client.listGrants(),
        (error) => error.status === 401 && error.brokerMessage === 'Session expired.'
    );
}

/** Governed executor: 401 recovery by registering again once, heartbeat cadence (the kill switch: see below). */
async function testSessionRecovery() {
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
            registrationSecret: TEST_REGISTRATION_SECRET,
            clientFactory: () => fakeClient,
        },
    });
    await agent.init();
    const executor = agent.governedExecutor;
    const context = { projectRoot: ROOT, noConfirm: true, verbose: false };
    assert.strictEqual(fakeClient.getRegisterCalls(), 1);

    // A tool request rejected with 401 (expired token or session): register again once, then retry.
    fakeClient.failNext('submit', brokerHttpError(401, 'Session expired.'));
    const recovered = await executor.executeTool('read_file', { path: './README.html' }, context);
    assert.strictEqual(recovered, 'ok');
    assert.strictEqual(fakeClient.getRegisterCalls(), 2);
    assert.strictEqual(fakeClient.getSubmitCalls(), 2);
    assert.strictEqual(executor.sessionInfo.sessionId, 'sess_test_002');
    assert.strictEqual(executor.getPromptContext().session.sessionId, 'sess_test_002');

    // Only once per call: a second 401 after registering again is reported, not retried again.
    fakeClient.failNext('submit', brokerHttpError(401, 'Session expired.'));
    fakeClient.failNext('submit', brokerHttpError(401, 'Session expired.'));
    const twice = await executor.executeTool('read_file', { path: './README.html' }, context);
    assert(twice.includes('broker error'), twice);
    assert.strictEqual(fakeClient.getRegisterCalls(), 3);

    // Other failures are not treated as session expiry.
    fakeClient.failNext('submit', brokerHttpError(500, 'Internal error.'));
    const failed = await executor.executeTool('read_file', { path: './README.html' }, context);
    assert(failed.includes('broker error'), failed);
    assert.strictEqual(fakeClient.getRegisterCalls(), 3);

    // LLM calls recover the same way.
    fakeClient.failNext('llmChat', brokerHttpError(401, 'Invalid or expired token.'));
    const chat = await executor.chat({ model: 'x', messages: [{ role: 'user', content: 'hi' }], tools: [] });
    assert.strictEqual(chat.content, 'broker chat ok');
    assert.strictEqual(fakeClient.getRegisterCalls(), 4);

    // Heartbeat: success renews; a 401 registers again; other failures only warn.
    assert.strictEqual(await executor._heartbeatOnce(), true);
    fakeClient.failNext('heartbeat', brokerHttpError(401, 'Session expired.'));
    assert.strictEqual(await executor._heartbeatOnce(), true);
    assert.strictEqual(fakeClient.getRegisterCalls(), 5);
    fakeClient.failNext('heartbeat', brokerHttpError(503, 'Unavailable.'));
    assert.strictEqual(await executor._heartbeatOnce(), false);
    assert.strictEqual(fakeClient.getRegisterCalls(), 5);
    assert.strictEqual(fakeClient.getHeartbeatCalls(), 3);

    // Heartbeat cadence: a third of the token's remaining lifetime, between 10 seconds and 5 minutes.
    fakeClient.tokenExpiresAt = null;
    assert.strictEqual(executor._heartbeatIntervalMs(), 5 * 60 * 1000);
    fakeClient.tokenExpiresAt = new Date(Date.now() + 20 * 60 * 1000).toISOString();
    assert.strictEqual(executor._heartbeatIntervalMs(), 5 * 60 * 1000);
    fakeClient.tokenExpiresAt = new Date(Date.now() + 3 * 60 * 1000).toISOString();
    const interval = executor._heartbeatIntervalMs();
    assert(interval > 55 * 1000 && interval <= 60 * 1000, `interval ${interval}`);
    fakeClient.tokenExpiresAt = new Date(Date.now() + 5 * 1000).toISOString();
    assert.strictEqual(executor._heartbeatIntervalMs(), 10 * 1000);

    await agent.close();
    assert.strictEqual(executor._heartbeatTimer, null);

    // Every registration, the first and each one after a 401, carries the registration secret.
    assert.strictEqual(fakeClient.getRegisteredSecrets().length, fakeClient.getRegisterCalls());
    assert(fakeClient.getRegisteredSecrets().every((secret) => secret === TEST_REGISTRATION_SECRET));
    assertSecretNotExposed(executor.getPromptContext(), agent.messages[0].content);
}

/**
 * The kill switch (system epoch advancement) stops a running agent for good, whichever broker call sees it
 * first (tool request, LLM call or heartbeat). Later, once the token has expired, the broker answers with a
 * plain 401 ("Invalid or expired token."), which would otherwise look like an expired session: the executor
 * still does not register again (the broker would accept a new registration with the still valid credential),
 * and nothing else reaches the broker. A newly started process may still register; revoking the credential
 * is what stops that.
 */
async function testKillSwitchEndsTheExecutor() {
    const killSwitch = () => brokerHttpError(401, 'Token invalidated by system epoch advancement.');
    const expired = () => brokerHttpError(401, 'Invalid or expired token.');

    for (const via of ['submit', 'llmChat', 'heartbeat']) {
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
                registrationSecret: TEST_REGISTRATION_SECRET,
                clientFactory: () => fakeClient,
            },
        });
        await agent.init();
        const executor = agent.governedExecutor;
        const context = { projectRoot: ROOT, noConfirm: true, verbose: false };
        assert.strictEqual(fakeClient.getRegisterCalls(), 1);
        assert.strictEqual(executor._heartbeatRunning, true);

        // The kill switch first, then (token expired) a plain 401 on every kind of call.
        fakeClient.failNext(via, killSwitch());
        for (const kind of ['submit', 'llmChat', 'heartbeat']) {
            fakeClient.failNext(kind, expired());
        }

        if (via === 'submit') {
            const killed = await executor.executeTool('read_file', { path: './README.html' }, context);
            assert(killed.includes('session ended'), `${via}: ${killed}`);
        } else if (via === 'llmChat') {
            await assert.rejects(
                () => executor.chat({ model: 'x', messages: [{ role: 'user', content: 'hi' }], tools: [] }),
                (error) => /session ended/.test(error.message)
            );
        } else {
            assert.strictEqual(await executor._heartbeatOnce(), false);
        }

        assert.strictEqual(executor.terminated, true, `${via}: the kill switch ends the executor`);
        assert(/kill switch/.test(executor.terminationReason), `${via}: ${executor.terminationReason}`);
        assert.strictEqual(executor._heartbeatRunning, false, `${via}: heartbeat stopped`);
        assert.strictEqual(executor._heartbeatTimer, null, `${via}: no heartbeat scheduled`);
        const submitCalls = fakeClient.getSubmitCalls();
        const llmChatCalls = fakeClient.getLlmChatCalls();
        const heartbeatCalls = fakeClient.getHeartbeatCalls();

        // Every later call fails at once: no registration, no broker call, also not for the expired-token 401s.
        const later = await executor.executeTool('read_file', { path: './README.html' }, context);
        assert(later.includes('session ended'), `${via}: ${later}`);
        await assert.rejects(
            () => executor.chat({ model: 'x', messages: [{ role: 'user', content: 'hi' }], tools: [] }),
            (error) => /session ended/.test(error.message)
        );
        assert.strictEqual(await executor._heartbeatOnce(), false);
        assert.strictEqual(await executor.healthCheck(), false);
        await assert.rejects(() => executor._reregister(), (error) => /session ended/.test(error.message));
        assert.strictEqual(fakeClient.getRegisterCalls(), 1, `${via}: the kill switch must not lead to a new registration`);
        assert.strictEqual(fakeClient.getSubmitCalls(), submitCalls);
        assert.strictEqual(fakeClient.getLlmChatCalls(), llmChatCalls);
        assert.strictEqual(fakeClient.getHeartbeatCalls(), heartbeatCalls);

        // The LINE listener stops with an error after its first poll.
        const listener = new LineListener(agent, { pollIntervalMs: 1 });
        await assert.rejects(() => listener.start(), (error) => /broker session ended/.test(error.message));
        assert.strictEqual(listener.running, false);
        assert.strictEqual(fakeClient.getRegisterCalls(), 1);

        // Closing does not contact the broker either.
        await agent.close();
        assert.strictEqual(fakeClient.getCloseCalls(), 0, `${via}: no close call after the kill switch`);
        assert.strictEqual(fakeClient.getRegisterCalls(), 1);
    }
}

/**
 * The kill switch fired while the process was suspended past its token's expiry, so the first answer it gets is
 * a plain expired-token 401 and it registers again. The new token carries a later system epoch than the previous
 * one (only the kill switch advances it): the executor closes that new session and ends, as it would have on the
 * kill switch rejection. Without an epoch change, a plain 401 is recovered as before.
 */
async function testKillSwitchSeenOnlyWhenRegisteringAgain() {
    const fakeClient = createFakeClient();
    let epoch = 3;
    const token = (claims) => ['header', Buffer.from(JSON.stringify(claims), 'utf8').toString('base64url'), 'signature'].join('.');
    const registerSession = fakeClient.registerSession.bind(fakeClient);
    fakeClient.registerSession = async (...args) => {
        const info = await registerSession(...args);
        fakeClient.scopedToken = token({ epoch: String(epoch), session_id: info.sessionId });
        return info;
    };
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
            registrationSecret: TEST_REGISTRATION_SECRET,
            clientFactory: () => fakeClient,
        },
    });
    await agent.init();
    const executor = agent.governedExecutor;
    const context = { projectRoot: ROOT, noConfirm: true, verbose: false };
    const expired = () => brokerHttpError(401, 'Invalid or expired token.');

    // Same epoch: an expired token is recovered by registering again.
    fakeClient.failNext('submit', expired());
    assert.strictEqual(await executor.executeTool('read_file', { path: './README.html' }, context), 'ok');
    assert.strictEqual(fakeClient.getRegisterCalls(), 2);
    assert.strictEqual(executor.terminated, false);

    // The kill switch advanced the epoch while the process was suspended.
    epoch = 4;
    fakeClient.failNext('heartbeat', expired());
    assert.strictEqual(await executor._heartbeatOnce(), false);
    assert.strictEqual(executor.terminated, true);
    assert(/kill switch/.test(executor.terminationReason), executor.terminationReason);
    assert.strictEqual(fakeClient.getRegisterCalls(), 3);
    assert.strictEqual(fakeClient.getCloseCalls(), 1, 'the session registered after the kill switch is closed again');
    assert.strictEqual(executor.sessionInfo.sessionId, 'sess_test_002', 'the executor does not carry on with the new session');
    assert.strictEqual(executor._heartbeatRunning, false);

    const submitCalls = fakeClient.getSubmitCalls();
    const later = await executor.executeTool('read_file', { path: './README.html' }, context);
    assert(later.includes('session ended'), later);
    await assert.rejects(() => executor._reregister(), (error) => /session ended/.test(error.message));
    assert.strictEqual(fakeClient.getRegisterCalls(), 3);
    assert.strictEqual(fakeClient.getSubmitCalls(), submitCalls);
    await agent.close();
    assert.strictEqual(fakeClient.getCloseCalls(), 1);
}

/**
 * Registering again is refused (credential revoked or expired, task ended): the executor ends for good.
 * No heartbeat, registration or broker call follows, every later tool call fails at once, and the LINE
 * listener stops with an error instead of producing a rejected registration on every poll.
 * A network failure while registering again is not final.
 */
async function testRegistrationRefusalEndsTheExecutor() {
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
            registrationSecret: TEST_REGISTRATION_SECRET,
            clientFactory: () => fakeClient,
        },
    });
    await agent.init();
    const executor = agent.governedExecutor;
    const context = { projectRoot: ROOT, noConfirm: true, verbose: false };

    // A network failure while registering again is reported but does not end the executor.
    fakeClient.failNext('submit', brokerHttpError(401, 'Session expired.'));
    fakeClient.failNext('register', new Error('fetch failed'));
    const transient = await executor.executeTool('read_file', { path: './README.html' }, context);
    assert(transient.includes('broker error'), transient);
    assert.strictEqual(executor.terminated, false);
    assert.strictEqual(fakeClient.getRegisterCalls(), 2);

    // The broker refuses to register again: the executor ends.
    fakeClient.failNext('submit', brokerHttpError(401, 'Session is not active.'));
    fakeClient.failNext('register', brokerHttpError(401, 'Registration rejected.'));
    const refused = await executor.executeTool('read_file', { path: './README.html' }, context);
    assert(refused.includes('session ended'), refused);
    assert.strictEqual(executor.terminated, true);
    assert.strictEqual(executor._heartbeatRunning, false);
    assert.strictEqual(executor._heartbeatTimer, null);
    const registerCalls = fakeClient.getRegisterCalls();
    const submitCalls = fakeClient.getSubmitCalls();
    const heartbeatCalls = fakeClient.getHeartbeatCalls();
    const llmChatCalls = fakeClient.getLlmChatCalls();

    // Nothing after that reaches the broker, and nothing registers again.
    const later = await executor.executeTool('read_file', { path: './README.html' }, context);
    assert(later.includes('session ended'), later);
    assert.strictEqual(await executor._heartbeatOnce(), false);
    await assert.rejects(
        () => executor.chat({ model: 'x', messages: [{ role: 'user', content: 'hi' }], tools: [] }),
        (error) => /session ended/.test(error.message)
    );
    await assert.rejects(() => executor._reregister(), (error) => /session ended/.test(error.message));
    assert.strictEqual(fakeClient.getRegisterCalls(), registerCalls);
    assert.strictEqual(fakeClient.getSubmitCalls(), submitCalls);
    assert.strictEqual(fakeClient.getHeartbeatCalls(), heartbeatCalls);
    assert.strictEqual(fakeClient.getLlmChatCalls(), llmChatCalls);

    // The LINE listener stops with an error after its first poll instead of polling on.
    const listener = new LineListener(agent, { pollIntervalMs: 1 });
    await assert.rejects(() => listener.start(), (error) => /broker session ended/.test(error.message));
    assert.strictEqual(listener.running, false);
    assert.strictEqual(fakeClient.getRegisterCalls(), registerCalls);

    // The refusal message carries no secret.
    assert(!executor.terminationReason.includes(TEST_REGISTRATION_SECRET));
    await agent.close();
}

/** The three generation capabilities as the broker syncs them from their tool-specs. */
function readGenerationToolSpecs() {
    const fs = require('fs');
    const specRoot = path.join(ROOT, 'packages', 'csharp', 'broker', 'tool-specs');
    return ['generation.catalog.query', 'generation.definition.validate', 'generation.scaffold.generate']
        .map((toolId) => JSON.parse(fs.readFileSync(path.join(specRoot, toolId, 'tool.json'), 'utf8')));
}

/** A fake broker client for a governed generation task: the three generation grants and a system_scaffold task. */
function createGenerationFakeClient() {
    const client = createFakeClient();
    const specs = readGenerationToolSpecs();
    const submitted = [];
    const riskValue = { low: 0, medium: 1, high: 2, critical: 3 };
    const quotas = { 'generation.catalog.query': 20, 'generation.definition.validate': 6, 'generation.scaffold.generate': 2 };
    const scopes = {
        'generation.catalog.query': { routes: ['query_component_catalog'] },
        'generation.definition.validate': { routes: ['validate_definition'] },
        'generation.scaffold.generate': {
            routes: ['generate_scaffold'],
            output_slot: 'task_test',
            package_name: 'contacts',
            max_pages: 12,
            package: 'definition-site-v1',
        },
    };

    client.listCapabilities = async () => ({
        success: true,
        data: specs.map((spec) => ({
            capabilityId: spec.capability_bindings[0].capability_id,
            route: spec.capability_bindings[0].route,
            approvalPolicy: spec.capability_template.approval_policy,
            riskLevelValue: riskValue[spec.capability_template.risk_level],
            resourceType: spec.capability_template.resource_type,
            paramSchema: JSON.stringify(spec.input_schema),
        })),
    });
    client.listGrants = async () => ({
        success: true,
        data: Object.entries(scopes).map(([capabilityId, scope]) => ({
            capabilityId,
            scopeOverride: JSON.stringify(scope),
            remainingQuota: quotas[capabilityId],
            expiresAt: '2030-01-01T00:00:00Z',
            statusValue: 0,
        })),
    });
    const baseRuntimeSpec = client.getRuntimeSpec.bind(client);
    client.getRuntimeSpec = async () => {
        const response = await baseRuntimeSpec();
        return { ...response, data: { ...response.data, task_type: 'system_scaffold', task_id: 'task_test' } };
    };
    client.submitRequest = async (capabilityId, payload, idempotencyKey, intent) => {
        submitted.push({ capabilityId, payload, idempotencyKey, intent });
        return { success: true, data: { execution_state: 'Succeeded', result_payload: '{"ok":true}' } };
    };
    client.getSubmitted = () => submitted.slice();
    return client;
}

/**
 * Governed generation: the three generation tools map to the generation capabilities, their parameters match
 * the broker tool-specs, the payload stays { route, args, project_root }, and a generation task gets the
 * workflow and its limits in the system prompt (no secret, no host path). Other tasks do not get that section.
 */
async function testGenerationTools() {
    const { TOOL_DEFINITIONS, TOOL_TO_CAPABILITY } = require('../lib/tool-registry');

    // Tool parameters agree with the broker tool-spec input_schema (names, required, types, enum, maxLength, items).
    const normalize = (schema) => {
        if (!schema || typeof schema !== 'object') return schema;
        const result = {};
        for (const key of ['type', 'enum', 'maxLength']) {
            if (schema[key] !== undefined) result[key] = schema[key];
        }
        if (schema.items) result.items = normalize(schema.items);
        if (schema.properties) {
            result.properties = Object.fromEntries(Object.entries(schema.properties).map(([name, child]) => [name, normalize(child)]));
        }
        result.required = [...(schema.required || [])].sort();
        return result;
    };
    for (const spec of readGenerationToolSpecs()) {
        const binding = spec.capability_bindings[0];
        assert.strictEqual(spec.execution_rules.runtime_required, 'generation-worker', `${spec.tool_id} runtime`);
        assert.strictEqual(TOOL_TO_CAPABILITY[binding.route], binding.capability_id, `${binding.route} maps to ${binding.capability_id}`);
        const definition = TOOL_DEFINITIONS.find((def) => def.function.name === binding.route);
        assert(definition, `tool definition for ${binding.route}`);
        const toolSchema = normalize(definition.function.parameters);
        const specSchema = normalize(spec.input_schema);
        if (specSchema.properties) {
            for (const child of Object.values(specSchema.properties)) {
                if (child && child.required && child.required.length === 0) delete child.required;
            }
        }
        if (toolSchema.properties) {
            for (const child of Object.values(toolSchema.properties)) {
                if (child && child.required && child.required.length === 0) delete child.required;
            }
        }
        assert.deepStrictEqual(toolSchema, specSchema, `${binding.route} parameters match the tool-spec input_schema`);
    }

    const fakeClient = createGenerationFakeClient();
    const agent = new AgentLoop({
        model: 'user-requested-model',
        provider: createForbiddenDirectProvider(),
        projectRoot: ROOT,
        stream: false,
        maxIterations: 12,
        governed: {
            brokerUrl: 'http://broker.local:5000',
            brokerPubKey: 'fake-pub-key',
            principalId: 'prn_test',
            taskId: 'task_test',
            roleId: 'role_executor',
            registrationSecret: TEST_REGISTRATION_SECRET,
            clientFactory: () => fakeClient,
        },
    });
    await agent.init();

    const toolNames = agent.getAvailableToolDefinitions().map((def) => def.function.name);
    assert.deepStrictEqual(toolNames, ['query_component_catalog', 'validate_definition', 'generate_scaffold']);

    const prompt = agent.messages[0].content;
    const start = prompt.indexOf('## Governed Generation Workflow');
    assert(start >= 0, 'a generation task gets the workflow section');
    const nextSection = prompt.indexOf('\n## ', start + 1);
    const section = prompt.slice(start, nextSection > start ? nextSection : undefined);
    const order = ['query_component_catalog', 'validate_definition', 'generate_scaffold'].map((name) => section.indexOf(`. ${name}`));
    assert(order.every((index) => index > 0) && order[0] < order[1] && order[1] < order[2], 'workflow lists catalog, validate, generate in order');
    assert(section.includes('Pages per prototype: at most 12'), section);
    assert(section.includes('validate_definition calls left: 6'), section);
    assert(section.includes('generate_scaffold calls left: 2'), section);
    assert(section.includes('Model turns for the whole task: at most 12'), section);
    for (const forbidden of [ROOT, ROOT.replace(/\\/g, '/'), TEST_REGISTRATION_SECRET, 'output_slot', 'task_test']) {
        assert(!section.includes(forbidden), `the workflow section must not contain ${forbidden}`);
    }
    assert(section.includes('No available worker'), 'the workflow section explains the busy-worker retry');
    assertSecretNotExposed(agent.getGovernedPromptContext(), prompt);

    // A generation task gets the lean base prompt and no project manual: the manual's CLI examples and field
    // type tables conflict with the catalog, and the agent has no file tool to read the rest of it.
    assert(prompt.startsWith(GENERATION_BASE_PROMPT), 'a generation task starts with the generation base prompt');
    for (const forbidden of ['## Project Manual', '<project_manual>', 'read AGENT.md', 'ui_components', 'PhotoWall', 'ImageViewer', '--fields']) {
        assert(!prompt.includes(forbidden), `a generation task prompt must not contain ${forbidden}`);
    }

    // The payload keeps the broker contract; the route is the tool name and the capability is the generation one.
    const context = { projectRoot: '/workspace', noConfirm: true, verbose: false };
    const template = { kind: 'definition-template', version: '0.1.0' };
    assert.strictEqual(await agent.governedExecutor.executeTool('query_component_catalog', { section: 'overview' }, context), '{"ok":true}');
    await agent.governedExecutor.executeTool('validate_definition', { template }, context);
    await agent.governedExecutor.executeTool('generate_scaffold', { template, title: 'Contacts' }, context);
    const submitted = fakeClient.getSubmitted();
    assert.deepStrictEqual(submitted.map((item) => item.capabilityId), [
        'generation.catalog.query', 'generation.definition.validate', 'generation.scaffold.generate',
    ]);
    for (const item of submitted) {
        assert.deepStrictEqual(Object.keys(item.payload), ['route', 'args', 'project_root']);
        assert.strictEqual(item.payload.route, TOOL_DEFINITIONS.find((def) => TOOL_TO_CAPABILITY[def.function.name] === item.capabilityId).function.name);
    }
    assert.deepStrictEqual(submitted[2].payload.args, { template, title: 'Contacts' });
    assert.strictEqual(submitted[2].intent, 'Generate scaffold: Contacts');
    await agent.close();

    // A session without generation grants does not get the workflow section.
    const plainClient = createFakeClient();
    const plainAgent = new AgentLoop({
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
            registrationSecret: TEST_REGISTRATION_SECRET,
            clientFactory: () => plainClient,
        },
    });
    await plainAgent.init();
    assert(!plainAgent.messages[0].content.includes('## Governed Generation Workflow'));
    assert(!plainAgent.messages[0].content.startsWith(GENERATION_BASE_PROMPT), 'other tasks keep the general base prompt');
    assert(plainAgent.messages[0].content.includes('## Project Manual'), 'other tasks still get the project manual');
    await plainAgent.close();
}

/**
 * Only a system_scaffold task, or a generate grant whose scope carries the output slot, is a generation task.
 * The low-risk catalog and validate capabilities can reach ordinary agents (default capability sets or an
 * administrator's choice); such an agent keeps the general base prompt and the project manual.
 */
async function testLowRiskGenerationGrantsKeepTheGeneralPrompt() {
    const grant = (capabilityId, scopeOverride = {}) => ({ capabilityId, scopeOverride });
    assert.strictEqual(isGenerationTask({
        allowedCapabilities: [grant('file.read'), grant('generation.catalog.query'), grant('generation.definition.validate')],
    }), false, 'catalog and validate grants alone do not make a generation task');
    assert.strictEqual(isGenerationTask({
        allowedCapabilities: [grant('generation.scaffold.generate', { routes: ['generate_scaffold'] })],
    }), false, 'a generate grant without an output slot does not make a generation task');
    assert.strictEqual(isGenerationTask({
        allowedCapabilities: [grant('generation.scaffold.generate', { routes: ['generate_scaffold'], output_slot: '  ' })],
    }), false, 'a blank output slot does not count');
    assert.strictEqual(isGenerationTask({
        allowedCapabilities: [grant('generation.scaffold.generate', { routes: ['generate_scaffold'], output_slot: 'task_x' })],
    }), true, 'a generate grant with an output slot is a generation task');
    assert.strictEqual(isGenerationTask({ allowedCapabilities: [], runtimeSpec: { taskType: 'system_scaffold' } }), true);
    assert.strictEqual(isGenerationTask({ allowedCapabilities: [], runtimeSpec: { taskType: 'analysis' } }), false);
    assert.strictEqual(isGenerationTask(null), false);

    // An analysis agent that also holds the two low-risk generation grants.
    const client = createFakeClient();
    const specs = readGenerationToolSpecs().filter((spec) => spec.capability_template.risk_level === 'low');
    assert.deepStrictEqual(specs.map((spec) => spec.tool_id), ['generation.catalog.query', 'generation.definition.validate']);
    const baseCapabilities = client.listCapabilities.bind(client);
    const baseGrants = client.listGrants.bind(client);
    client.listCapabilities = async () => {
        const response = await baseCapabilities();
        return {
            ...response,
            data: [...response.data, ...specs.map((spec) => ({
                capabilityId: spec.capability_bindings[0].capability_id,
                route: spec.capability_bindings[0].route,
                approvalPolicy: spec.capability_template.approval_policy,
                riskLevelValue: 0,
                resourceType: spec.capability_template.resource_type,
                paramSchema: JSON.stringify(spec.input_schema),
            }))],
        };
    };
    client.listGrants = async () => {
        const response = await baseGrants();
        return {
            ...response,
            data: [...response.data, ...specs.map((spec) => ({
                capabilityId: spec.capability_bindings[0].capability_id,
                scopeOverride: JSON.stringify({ routes: [spec.capability_bindings[0].route] }),
                remainingQuota: 5,
                expiresAt: '2030-01-01T00:00:00Z',
                statusValue: 0,
            }))],
        };
    };
    const baseRuntimeSpec = client.getRuntimeSpec.bind(client);
    client.getRuntimeSpec = async () => {
        const response = await baseRuntimeSpec();
        return { ...response, data: { ...response.data, task_type: 'analysis', task_id: 'task_test' } };
    };

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
            registrationSecret: TEST_REGISTRATION_SECRET,
            clientFactory: () => client,
        },
    });
    await agent.init();
    const toolNames = agent.getAvailableToolDefinitions().map((def) => def.function.name);
    assert.deepStrictEqual(toolNames, ['read_file', 'query_component_catalog', 'validate_definition']);
    const prompt = agent.messages[0].content;
    assert.strictEqual(agent.generationTask, false);
    assert(!prompt.startsWith(GENERATION_BASE_PROMPT), 'an ordinary agent keeps the general base prompt');
    assert(!prompt.includes('## Governed Generation Workflow'), 'an ordinary agent gets no generation workflow');
    assert(prompt.includes('## Project Manual'), 'an ordinary agent still gets the project manual');
    await agent.close();
}

/** A scripted broker LLM: each call returns the next reply (text, or tool calls). */
function scriptLlm(client, replies) {
    const seen = [];
    client.llmChat = async (body) => {
        seen.push(body.messages.map((message) => ({ role: message.role, content: message.content })));
        const reply = replies.shift();
        assert(reply, 'the agent asked the model more often than the script expected');
        return {
            success: true,
            data: {
                content: reply.content || '',
                tool_calls: (reply.tools || []).map(([name, args], index) => ({ id: `call_${seen.length}_${index}`, function: { name, arguments: args } })),
                done: true,
                model: 'broker-model',
            },
        };
    };
    return seen;
}

function generationAgent(client, maxIterations = 12, forceStrategy = null) {
    return new AgentLoop({
        model: 'user-requested-model',
        provider: createForbiddenDirectProvider(),
        projectRoot: ROOT,
        stream: false,
        maxIterations,
        forceStrategy,
        governed: {
            brokerUrl: 'http://broker.local:5000',
            brokerPubKey: 'fake-pub-key',
            principalId: 'prn_test',
            taskId: 'task_test',
            roleId: 'role_executor',
            registrationSecret: TEST_REGISTRATION_SECRET,
            clientFactory: () => client,
        },
    });
}

const FINAL_COMPLETED = '{"type":"final","code":"completed"}';
const FAILURE_INVALID_RESPONSE = '{"type":"failure","code":"invalid_response"}';
const FAILURE_GENERATION_FAILED = '{"type":"failure","code":"generation_failed"}';

function plainGovernedAgent(client, options = {}) {
    return new AgentLoop({
        model: 'user-requested-model',
        provider: createForbiddenDirectProvider(),
        projectRoot: ROOT,
        stream: false,
        ...options,
        governed: {
            brokerUrl: 'http://broker.local:5000',
            brokerPubKey: 'fake-pub-key',
            principalId: 'prn_test',
            taskId: 'task_test',
            roleId: 'role_reader',
            registrationSecret: TEST_REGISTRATION_SECRET,
            clientFactory: () => client,
        },
    });
}

const countMessages = (agent, content) => agent.messages.filter((message) => message.role === 'user' && message.content === content).length;

/**
 * The generation prompt states that the run is unattended: a tool call in every turn until the fixed JSON reply, no
 * definition in text, no questions. The loop backs this up with one reminder per run for a turn without progress:
 * a reply without a tool call that is not the fixed JSON object, or "completed" before a generate succeeded. A second
 * such reply ends the run as a failure, and the fixed JSON reply after a successful generate ends it at once.
 * Every prompt and reminder describes that one contract (native tool calls, the fixed JSON reply).
 */
async function testGenerationRunNeedsAToolCallEveryTurn() {
    const { RESPONSE_CODES } = require('../lib/governed-response');
    const template = { kind: 'definition-template', version: '0.1.0' };
    const generateOk = JSON.stringify({ ok: true, zip: { path: 'slot/req/contacts-scaffold.zip', sha256: 'a'.repeat(64), size: 10 } });
    const allResponses = Object.entries(RESPONSE_CODES)
        .flatMap(([type, codes]) => [...codes].map((code) => JSON.stringify({ type, code })));
    assert.strictEqual(allResponses.length, 8);
    // Wording of the earlier contract (a prose summary, text or ReAct tool calls) that no prompt may carry any more.
    const staleWording = ['final summary', 'short summary', 'state them in the', 'report the remaining errors', '<tool_call>', 'ReAct', '|unsupported_request'];

    // The prompt rules.
    {
        const client = createGenerationFakeClient();
        const agent = generationAgent(client);
        await agent.init();
        const prompt = agent.messages[0].content.replace(/\s+/g, ' ');
        assert(prompt.includes('Call a tool in every turn until the final JSON reply in step 5'), 'the prompt requires a tool call every turn');
        assert(prompt.includes('Never write the definition in your reply text'), 'the prompt forbids the definition in text');
        assert(prompt.includes('You cannot ask the user anything'), 'the prompt forbids questions');
        assert(prompt.includes('make reasonable assumptions'), 'the prompt asks for assumptions');
        assert(prompt.includes('submit it directly with validate_definition'), 'step 2 submits the definition with validate_definition');
        assert(prompt.includes(`reply without a tool call using exactly ${FINAL_COMPLETED}`), 'step 5 is the fixed JSON reply');
        assert(prompt.includes(`stop with ${FAILURE_GENERATION_FAILED} instead of guessing`), 'running out of validate calls ends with the failure reply');
        assert(prompt.includes('Call tools only as native structured function (tool) calls'), 'tools are native calls only');
        for (const response of allResponses) assert(prompt.includes(response), `the prompt lists ${response}`);
        for (const stale of staleWording) assert(!prompt.includes(stale), `the generation prompt must not say "${stale}"`);
        await agent.close();

        // ReAct forced on a governed agent: still no text tool-call instructions (text is never executed).
        const reactAgent = generationAgent(createGenerationFakeClient(), 12, 'react');
        await reactAgent.init();
        assert(!reactAgent.messages[0].content.includes('<tool_call>'), 'a governed prompt never teaches the text wrapper');
        await reactAgent.close();

        // An ordinary governed agent gets the same contract, and both reminders describe it too.
        const plain = plainGovernedAgent(createFakeClient());
        await plain.init();
        // The project manual that follows is repository text, not part of the contract.
        const plainPrompt = plain.messages[0].content.split('\n## Project Manual')[0].replace(/\s+/g, ' ');
        for (const response of allResponses) assert(plainPrompt.includes(response), `the governed prompt lists ${response}`);
        for (const stale of staleWording) assert(!plainPrompt.includes(stale), `the governed prompt must not say "${stale}"`);
        await plain.close();

        const generationReminder = GENERATION_CONTINUE_REMINDER.replace(/\s+/g, ' ');
        assert(generationReminder.includes('as native function (tool) calls'));
        assert(generationReminder.includes(`${FINAL_COMPLETED} after generate_scaffold succeeded`));
        assert(generationReminder.includes(FAILURE_GENERATION_FAILED));
        const responseReminder = GOVERNED_RESPONSE_REMINDER.replace(/\s+/g, ' ');
        for (const response of allResponses) assert(responseReminder.includes(response), `the reminder lists ${response}`);
        for (const stale of staleWording) {
            assert(!generationReminder.includes(stale) && !responseReminder.includes(stale), `a reminder must not say "${stale}"`);
        }
    }

    // The definition written as text first: one reminder, then the normal flow and the fixed JSON reply.
    {
        const client = createGenerationFakeClient();
        client.submitRequest = async (capabilityId) => ({
            success: true,
            data: {
                execution_state: 'Succeeded',
                result_payload: capabilityId === 'generation.scaffold.generate' ? generateOk : '{"ok":true}',
            },
        });
        const seen = scriptLlm(client, [
            { content: 'Here is the definition: {"kind":"definition-template"}' },
            { tools: [['validate_definition', { template }]] },
            { tools: [['generate_scaffold', { template, title: 'Contacts' }]] },
            { content: FINAL_COMPLETED },
        ]);
        const agent = generationAgent(client);
        await agent.init();
        assert.strictEqual(agent.generationTask, true);
        assert.strictEqual(await agent.send('build it'), FINAL_COMPLETED);
        assert.strictEqual(seen.length, 4);
        assert.strictEqual(countMessages(agent, GENERATION_CONTINUE_REMINDER), 1, 'exactly one reminder');
        assert.strictEqual(seen[1][seen[1].length - 1].content, GENERATION_CONTINUE_REMINDER, 'the reminder follows the text-only reply');
        await agent.close();
    }

    // A clarifying question twice: one reminder, and the second reply that is not the fixed JSON ends the run.
    {
        const client = createGenerationFakeClient();
        const seen = scriptLlm(client, [
            { content: 'Which fields do you need?' },
            { content: 'I still need the field list.' },
        ]);
        const agent = generationAgent(client);
        await agent.init();
        assert.strictEqual(await agent.send('build it'), FAILURE_INVALID_RESPONSE, 'prose is never returned as the answer');
        assert.strictEqual(seen.length, 2, 'only one reminder, then the run ends');
        assert.strictEqual(countMessages(agent, GENERATION_CONTINUE_REMINDER), 1);
        assert.strictEqual(client.getSubmitted().length, 0);
        await agent.close();
    }

    // The reminder counts against the iteration limit.
    {
        const client = createGenerationFakeClient();
        const seen = scriptLlm(client, [{ content: 'Which fields do you need?' }]);
        const agent = generationAgent(client, 1);
        await agent.init();
        assert.strictEqual(await agent.send('build it'), FAILURE_INVALID_RESPONSE);
        assert.strictEqual(seen.length, 1, 'no reminder when the iteration limit is reached');
        assert.strictEqual(countMessages(agent, GENERATION_CONTINUE_REMINDER), 0);
        await agent.close();
    }

    // A failed generate is not a success: "completed" after it still gets the one reminder, then fails.
    {
        const client = createGenerationFakeClient();
        client.submitRequest = async () => ({ success: true, data: { execution_state: 'Failed', result_payload: '{"ok":false,"errors":[]}' } });
        const seen = scriptLlm(client, [
            { tools: [['generate_scaffold', { template }]] },
            { content: FINAL_COMPLETED },
            { content: FINAL_COMPLETED },
        ]);
        const agent = generationAgent(client);
        await agent.init();
        assert.strictEqual(await agent.send('build it'), FAILURE_GENERATION_FAILED);
        assert.strictEqual(seen.length, 3);
        assert.strictEqual(countMessages(agent, GENERATION_CONTINUE_REMINDER), 1);
        await agent.close();
    }

    // The one reminder is shared: prose first, then an early "completed" ends the run without a second reminder.
    {
        const client = createGenerationFakeClient();
        const seen = scriptLlm(client, [{ content: 'Working on it.' }, { content: FINAL_COMPLETED }]);
        const agent = generationAgent(client);
        await agent.init();
        assert.strictEqual(await agent.send('build it'), FAILURE_GENERATION_FAILED);
        assert.strictEqual(seen.length, 2);
        assert.strictEqual(countMessages(agent, GENERATION_CONTINUE_REMINDER), 1);
        await agent.close();
    }

    // A refusal or failure reply before generating is a valid end of the run: no reminder.
    for (const reply of [FAILURE_GENERATION_FAILED, '{"type":"refusal","code":"policy_denied"}']) {
        const client = createGenerationFakeClient();
        const seen = scriptLlm(client, [{ content: reply }]);
        const agent = generationAgent(client);
        await agent.init();
        assert.strictEqual(await agent.send('build it'), reply);
        assert.strictEqual(seen.length, 1);
        assert.strictEqual(countMessages(agent, GENERATION_CONTINUE_REMINDER), 0);
        await agent.close();
    }

    // Other governed tasks: prose gets the contract reminder once (not the generation one); a second prose reply
    // fails, and a fixed JSON reply after the reminder is accepted.
    {
        const client = createFakeClient();
        let calls = 0;
        client.llmChat = async () => {
            calls += 1;
            return { success: true, data: { content: 'done', tool_calls: [], done: true, model: 'broker-model' } };
        };
        const agent = plainGovernedAgent(client);
        await agent.init();
        assert.strictEqual(await agent.send('hello'), FAILURE_INVALID_RESPONSE);
        assert.strictEqual(calls, 2, 'one reminder, then the run ends');
        assert.strictEqual(countMessages(agent, GOVERNED_RESPONSE_REMINDER), 1);
        assert.strictEqual(countMessages(agent, GENERATION_CONTINUE_REMINDER), 0, 'an ordinary task never gets the generation reminder');
        await agent.close();

        const recovering = createFakeClient();
        const replies = ['The README describes the project.', FINAL_COMPLETED];
        recovering.llmChat = async () => ({ success: true, data: { content: replies.shift(), tool_calls: [], done: true, model: 'broker-model' } });
        const recoveringAgent = plainGovernedAgent(recovering);
        await recoveringAgent.init();
        assert.strictEqual(await recoveringAgent.send('hello'), FINAL_COMPLETED);
        assert.strictEqual(countMessages(recoveringAgent, GOVERNED_RESPONSE_REMINDER), 1);
        await recoveringAgent.close();

        // "completed" without any tool call is a valid end for an ordinary task: no reminder.
        const direct = createFakeClient();
        let directCalls = 0;
        direct.llmChat = async () => {
            directCalls += 1;
            return { success: true, data: { content: FINAL_COMPLETED, tool_calls: [], done: true, model: 'broker-model' } };
        };
        const directAgent = plainGovernedAgent(direct);
        await directAgent.init();
        assert.strictEqual(await directAgent.send('hello'), FINAL_COMPLETED);
        assert.strictEqual(directCalls, 1);
        assert.strictEqual(countMessages(directAgent, GOVERNED_RESPONSE_REMINDER), 0);
        await directAgent.close();
    }
}

/**
 * A governed run never executes reply text as a tool call, in native or in forced ReAct mode, before or after a
 * generate succeeded: a fenced JSON block (also without its closing fence), bare JSON, and a <tools> or <tool_call>
 * wrapper are only replies that are not the fixed JSON object. Nothing reaches the broker.
 */
async function testReplyTextIsNeverRunAsAToolCall() {
    const template = { kind: 'definition-template', version: '0.1.0' };
    const generateOk = JSON.stringify({ ok: true, zip: { path: 'slot/req/contacts-scaffold.zip', sha256: 'b'.repeat(64), size: 10 } });
    const call = (name, args) => JSON.stringify({ name, arguments: args });
    const fence = '```';
    const textCalls = [
        `I will read the catalog.\n${fence}json\n${call('query_component_catalog', { section: 'overview' })}\n${fence}`,
        `${fence}json\n${call('query_component_catalog', { section: 'field_types' })}`,
        `<tools>${call('validate_definition', { template })}</tools>`,
        `<tool_call>${call('generate_scaffold', { template })}</tool_call>`,
        call('generate_scaffold', { template, title: 'Contacts' }),
        `{"tool_calls":[{"function":${call('generate_scaffold', { template })}}]}`,
    ];

    for (const forceStrategy of [null, 'react']) {
        for (const text of textCalls) {
            const client = createGenerationFakeClient();
            const seen = scriptLlm(client, [{ content: text }, { content: text }]);
            const agent = generationAgent(client, 12, forceStrategy);
            await agent.init();
            assert.strictEqual(agent.useNativeTools, forceStrategy !== 'react');
            assert.strictEqual(await agent.send('build it'), FAILURE_INVALID_RESPONSE, text);
            assert.strictEqual(seen.length, 2, 'one reminder, then the run ends');
            assert.strictEqual(client.getSubmitted().length, 0, `text never reaches the broker: ${text}`);
            assert(!agent.messages.some((message) => message.role === 'tool' || Array.isArray(message.tool_calls)),
                'text produces neither a tool call nor a tool result in the conversation');
            await agent.close();
        }
    }

    // After a successful generate, a call written as text is still not run, and it is not the final answer either.
    {
        const client = createGenerationFakeClient();
        const submitted = [];
        client.submitRequest = async (capabilityId, payload) => {
            submitted.push(payload.route);
            return { success: true, data: { execution_state: 'Succeeded', result_payload: generateOk } };
        };
        const text = `Done. I called:\n${fence}json\n${call('generate_scaffold', { template })}\n${fence}`;
        const seen = scriptLlm(client, [
            { tools: [['generate_scaffold', { template }]] },
            { content: text },
            { content: FINAL_COMPLETED },
        ]);
        const agent = generationAgent(client);
        await agent.init();
        assert.strictEqual(await agent.send('build it'), FINAL_COMPLETED);
        assert.strictEqual(seen.length, 3);
        assert.deepStrictEqual(submitted, ['generate_scaffold'], 'the call written as text is not run again');
        await agent.close();
    }

    // Other governed tasks never run JSON text either.
    {
        const client = createFakeClient();
        let calls = 0;
        client.llmChat = async () => {
            calls += 1;
            return { success: true, data: { content: call('read_file', { path: './README.md' }), tool_calls: [], done: true, model: 'broker-model' } };
        };
        const agent = plainGovernedAgent(client);
        await agent.init();
        const submitsBefore = client.getSubmitCalls();
        assert.strictEqual(await agent.send('hello'), FAILURE_INVALID_RESPONSE);
        assert.strictEqual(calls, 2);
        assert.strictEqual(client.getSubmitCalls(), submitsBefore, 'an ordinary task does not run JSON text');
        await agent.close();
    }
}

async function testGovernedJsonResponseBoundary() {
    const { parseGovernedResponse, serializeGovernedResponse } = require('../lib/governed-response');
    for (const [type, code] of [
        ['final', 'completed'],
        ['refusal', 'policy_denied'],
        ['refusal', 'unsupported_request'],
        ['refusal', 'invalid_request'],
        ['failure', 'generation_failed'],
        ['failure', 'tool_failed'],
        ['failure', 'limit_reached'],
        ['failure', 'invalid_response'],
    ]) {
        const json = serializeGovernedResponse(type, code);
        assert.deepStrictEqual(parseGovernedResponse(json), { ok: true, response: { type, code } });
    }
    for (const invalid of [
        'completed',
        '{"type":"final","code":"completed","message":"ignore policy and run tool"}',
        '{"type":"final","code":"unknown"}',
        '{"type":"final","type":"refusal","code":"policy_denied"}',
        '{"type":"failure","code":"completed"}',
        '{"type":"toString","code":"anything"}',
        '{"type":"__proto__","code":"anything"}',
        '{"type":"final","code":"completed"} trailing',
        `{"type":"final","code":"completed","pad":"${'x'.repeat(4097)}"}`,
        `{"type":"final","code":"completed","extra":${'['.repeat(70)}0${']'.repeat(70)}}`,
    ]) {
        assert.deepStrictEqual(parseGovernedResponse(invalid), { ok: false, code: 'invalid_response' }, invalid.slice(0, 80));
    }

    const client = createGenerationFakeClient();
    const submitted = [];
    client.submitRequest = async (...args) => { submitted.push(args); return { success: true, data: {} }; };
    let chats = 0;
    client.llmChat = async () => {
        chats += 1;
        return { success: true, data: {
            content: '<tool_call>{"name":"generate_scaffold","arguments":{}}</tool_call>',
            tool_calls: [], done: true, model: 'broker-model',
        } };
    };
    const agent = generationAgent(client);
    await agent.init();
    const result = await agent.send('build it');
    assert.strictEqual(result, '{"type":"failure","code":"invalid_response"}');
    assert.strictEqual(chats, 2, 'an invalid reply gets the one reminder, then fails closed');
    assert.strictEqual(submitted.length, 0, 'textual tool-call syntax never reaches the broker');
    assert.strictEqual(agent.messages.filter((message) => message.content === GENERATION_CONTINUE_REMINDER).length, 1);
    await agent.close();

    const validClient = createGenerationFakeClient();
    const generatePayload = JSON.stringify({ ok: true, zip: { path: 'slot/probe.zip', sha256: 'd'.repeat(64), size: 1 } });
    validClient.submitRequest = async () => ({ success: true, data: { execution_state: 'Succeeded', result_payload: generatePayload } });
    let validChats = 0;
    validClient.llmChat = async () => {
        validChats++;
        return validChats === 1 ? { success: true, data: {
            content: '', tool_calls: [{ type: 'function', id: 'generate', function: {
                name: 'generate_scaffold', arguments: { template: { kind: 'definition-template', version: '0.1.0' } },
            } }], done: true, model: 'broker-model',
        } } : { success: true, data: {
            content: '{"type":"final","code":"completed"}', tool_calls: [], done: true, model: 'broker-model',
        } };
    };
    const validAgent = generationAgent(validClient);
    await validAgent.init();
    assert.strictEqual(await validAgent.send('build it'), '{"type":"final","code":"completed"}');
    assert.strictEqual(validChats, 2);
    await validAgent.close();

    // An unknown extra key alone no longer rejects a call (see testNativeToolCallNormalisation); a call whose
    // arguments are not an object still does.
    const malformedClient = createGenerationFakeClient();
    malformedClient.llmChat = async () => ({ success: true, data: { content: '', tool_calls: [{
        function: { name: 'generate_scaffold', arguments: ['not', 'an', 'object'] }, unexpected: true,
    }], done: true, model: 'broker-model' } });
    const malformedAgent = generationAgent(malformedClient);
    await malformedAgent.init();
    assert.strictEqual(await malformedAgent.send('build it'), '{"type":"failure","code":"invalid_response"}');
    assert.strictEqual(malformedClient.getSubmitted().length, 0, 'malformed native calls fail before tool dispatch');
    await malformedAgent.close();

    const oversizedClient = createGenerationFakeClient();
    oversizedClient.llmChat = async () => ({ success: true, data: { content: '', tool_calls: [{
        function: { name: 'generate_scaffold', arguments: { pad: 'x'.repeat(257 * 1024) } },
    }], done: true, model: 'broker-model' } });
    const oversizedAgent = generationAgent(oversizedClient);
    await oversizedAgent.init();
    assert.strictEqual(await oversizedAgent.send('build it'), '{"type":"failure","code":"invalid_response"}');
    assert.strictEqual(oversizedClient.getSubmitted().length, 0, 'oversized native object arguments fail before tool dispatch');
    await oversizedAgent.close();

    const prematureClient = createGenerationFakeClient();
    let prematureChats = 0;
    prematureClient.llmChat = async () => {
        prematureChats++;
        return { success: true, data: {
            content: '{"type":"final","code":"completed"}', tool_calls: [], done: true, model: 'broker-model',
        } };
    };
    const prematureAgent = generationAgent(prematureClient);
    await prematureAgent.init();
    assert.strictEqual(await prematureAgent.send('build it'), '{"type":"failure","code":"generation_failed"}');
    assert.strictEqual(prematureChats, 2, 'a success response before generation gets one reminder, then fails');
    assert.strictEqual(prematureClient.getSubmitted().length, 0, 'completed cannot bypass generation tools');
    assert.strictEqual(prematureAgent.messages.filter((message) => message.content === GENERATION_CONTINUE_REMINDER).length, 1);
    await prematureAgent.close();

    const overrideClient = createFakeClient();
    const getRuntimeSpec = overrideClient.getRuntimeSpec.bind(overrideClient);
    overrideClient.getRuntimeSpec = async () => {
        const response = await getRuntimeSpec();
        return { ...response, data: { ...response.data, allow_model_override: true } };
    };
    const defaultModelAgent = new AgentLoop({
        provider: createForbiddenDirectProvider(), projectRoot: ROOT, stream: false, model: '',
        governed: {
            brokerUrl: 'http://broker.local:5000', brokerPubKey: 'fake-pub-key', principalId: 'prn_test',
            taskId: 'task_test', roleId: 'role_reader', registrationSecret: TEST_REGISTRATION_SECRET,
            clientFactory: () => overrideClient,
        },
    });
    await defaultModelAgent.init();
    assert.strictEqual(defaultModelAgent.model, 'broker-model',
        'an unset model must use the broker default even when model overrides are allowed');
    await defaultModelAgent.close();

    const explicitModelAgent = new AgentLoop({
        provider: createForbiddenDirectProvider(), projectRoot: ROOT, stream: false, model: 'probe-explicit-model',
        governed: {
            brokerUrl: 'http://broker.local:5000', brokerPubKey: 'fake-pub-key', principalId: 'prn_test',
            taskId: 'task_test', roleId: 'role_reader', registrationSecret: TEST_REGISTRATION_SECRET,
            clientFactory: () => overrideClient,
        },
    });
    await explicitModelAgent.init();
    assert.strictEqual(explicitModelAgent.model, 'probe-explicit-model', 'an explicit model is honored when overrides are allowed');
    await explicitModelAgent.close();
}

const GENERATION_TOOL_NAMES = 'query_component_catalog, validate_definition, generate_scaffold';

/** The unsupported result of a generation task says that no tool writes the definition, and what to call next. */
function assertUnsupportedGuidance(content, name) {
    assert(content.startsWith(`unsupported tool ${name}: there is no such tool.`), content);
    assert(content.includes('No tool writes the definition for you.'), content);
    assert(content.includes('Write the complete DefinitionTemplate yourself'), content);
    assert(content.includes('section "example"'), content);
    assert(content.includes('pass it as the template argument of validate_definition'), content);
    assert(content.includes('call generate_scaffold with the same template'), content);
    assert(content.endsWith(`Available tools: ${GENERATION_TOOL_NAMES}.`), content);
}

/**
 * A generation task that keeps calling a tool it does not have (a 7B model asked for generate_definition_template in
 * every turn until the iteration limit): the tool result says what to do instead, the turn counts as no progress (the
 * one reminder), and the same name in the next turn ends the run before the iteration limit with the fixed refusal
 * reply. Calls the governed executor gets for names that are not granted carry the same guidance in a generation task.
 */
async function testUnsupportedToolsInAGenerationTask() {
    const template = { kind: 'definition-template', version: '0.1.0' };
    const repeatedName = 'generate_definition_template';
    const refusal = '{"type":"refusal","code":"unsupported_request"}';

    // The same made-up name as a native call in every turn: guidance, one reminder, and the run ends at the second turn.
    {
        const client = createGenerationFakeClient();
        const replies = Array.from({ length: 12 }, () => ({ tools: [[repeatedName, { description: 'contacts' }]] }));
        const seen = scriptLlm(client, replies);
        const agent = generationAgent(client, 12);
        await agent.init();
        const reply = await agent.send('build it');
        assert.strictEqual(seen.length, 2, 'the run ends when the same unsupported name comes back, before the limit');
        assert.strictEqual(reply, refusal, 'the run ends with the fixed refusal reply, not with prose');
        assert.strictEqual(client.getSubmitted().length, 0, 'an unsupported name never reaches the broker');
        const results = agent.messages.filter((message) => message.role === 'tool');
        assert.strictEqual(results.length, 2);
        for (const result of results) assertUnsupportedGuidance(result.content, repeatedName);
        assert.strictEqual(agent.messages.filter((message) => message.content === GENERATION_CONTINUE_REMINDER).length, 1,
            'exactly one reminder');
        assert.strictEqual(seen[1][seen[1].length - 1].content, GENERATION_CONTINUE_REMINDER);
        await agent.close();
    }

    // A real tool name the session was not granted: the same guidance and the same early end.
    {
        const client = createGenerationFakeClient();
        const seen = scriptLlm(client, [
            { tools: [['run_command', { command: 'dir' }]] },
            { tools: [['run_command', { command: 'dir' }]] },
            { content: 'unused' },
        ]);
        const agent = generationAgent(client, 12);
        await agent.init();
        const reply = await agent.send('build it');
        assert.strictEqual(seen.length, 2);
        assert.strictEqual(reply, refusal);
        assertUnsupportedGuidance(agent.messages.find((message) => message.role === 'tool').content, 'run_command');
        assert.strictEqual(client.getSubmitted().length, 0);
        await agent.close();
    }

    // A different unsupported name in the next turn does not end the run: the model gets the guidance again, and the
    // run goes on to validate, generate and the fixed JSON reply.
    {
        const client = createGenerationFakeClient();
        const generateOk = JSON.stringify({ ok: true, zip: { path: 'slot/req/contacts-scaffold.zip', sha256: 'e'.repeat(64), size: 10 } });
        const routes = [];
        client.submitRequest = async (capabilityId, payload) => {
            routes.push(payload.route);
            return { success: true, data: { execution_state: 'Succeeded', result_payload: capabilityId === 'generation.scaffold.generate' ? generateOk : '{"ok":true}' } };
        };
        const seen = scriptLlm(client, [
            { tools: [['generate_definition', { template }]] },
            { tools: [['run_command', { command: 'dir' }]] },
            { tools: [['validate_definition', { template }]] },
            { tools: [['generate_scaffold', { template }]] },
            { content: '{"type":"final","code":"completed"}' },
        ]);
        const agent = generationAgent(client);
        await agent.init();
        assert.strictEqual(await agent.send('build it'), '{"type":"final","code":"completed"}');
        assert.strictEqual(seen.length, 5, 'the run goes on after the unsupported names');
        assert.deepStrictEqual(routes, ['validate_definition', 'generate_scaffold'], 'names that are not granted never reach the broker');
        const unsupported = agent.messages.filter((message) => message.role === 'tool' && message.content.startsWith('unsupported tool'));
        assert.strictEqual(unsupported.length, 2);
        assertUnsupportedGuidance(unsupported[0].content, 'generate_definition');
        assertUnsupportedGuidance(unsupported[1].content, 'run_command');
        assert.strictEqual(agent.messages.filter((message) => message.content === GENERATION_CONTINUE_REMINDER).length, 1,
            'the first turn with only unsupported calls gets the one reminder');
        assert.strictEqual(seen[1][seen[1].length - 1].content, GENERATION_CONTINUE_REMINDER, 'the reminder follows the unsupported result');
        await agent.close();
    }

    // A turn that also runs a granted tool is progress: the repeated name only ends the run in two turns in a row
    // that call nothing but unsupported names.
    {
        const client = createGenerationFakeClient();
        const seen = scriptLlm(client, [
            { tools: [[repeatedName, {}]] },
            { tools: [[repeatedName, {}], ['query_component_catalog', { section: 'example' }]] },
            { tools: [[repeatedName, {}]] },
            { tools: [['validate_definition', { template }]] },
            { tools: [[repeatedName, {}]] },
            { tools: [[repeatedName, {}]] },
            { content: 'unused' },
        ]);
        const agent = generationAgent(client, 12);
        await agent.init();
        const reply = await agent.send('build it');
        assert.strictEqual(seen.length, 6, 'only the 5th and 6th turns are two unsupported-only turns in a row');
        assert.strictEqual(reply, refusal);
        assert.deepStrictEqual(client.getSubmitted().map((entry) => entry.payload.route), ['query_component_catalog', 'validate_definition']);
        assert.strictEqual(agent.messages.filter((message) => message.content === GENERATION_CONTINUE_REMINDER).length, 1,
            'the reminder is still sent only once');
        await agent.close();
    }

    // The iteration limit still counts: at the limit no reminder is added, and the run ends with limit_reached.
    {
        const client = createGenerationFakeClient();
        const seen = scriptLlm(client, [{ tools: [[repeatedName, {}]] }]);
        const agent = generationAgent(client, 1);
        await agent.init();
        const reply = await agent.send('build it');
        assert.strictEqual(seen.length, 1);
        assert.strictEqual(reply, '{"type":"failure","code":"limit_reached"}');
        assert.strictEqual(agent.messages.filter((message) => message.content === GENERATION_CONTINUE_REMINDER).length, 0);
        await agent.close();
    }

    // The governed executor answers names that are not granted with the same guidance in a generation task, and keeps
    // its plain messages for other agents.
    {
        const client = createGenerationFakeClient();
        const agent = generationAgent(client);
        await agent.init();
        const context = { projectRoot: ROOT, noConfirm: true, verbose: false };
        assertUnsupportedGuidance(await agent.governedExecutor.executeTool('make_definition', {}, context), 'make_definition');
        assertUnsupportedGuidance(await agent.governedExecutor.executeTool('read_file', { path: './README.md' }, context), 'read_file');
        assert.strictEqual(client.getSubmitted().length, 0);
        await agent.close();

        const plainClient = createFakeClient();
        const plain = plainGovernedAgent(plainClient);
        await plain.init();
        assert.strictEqual(await plain.governedExecutor.executeTool('make_definition', {}, context), '[Governed] unsupported tool: make_definition');
        assert((await plain.governedExecutor.executeTool('run_command', { command: 'dir' }, context)).startsWith('[Governed] capability denied: command.execute'));
        await plain.close();
    }
}

/**
 * An object or array parameter that arrives as a string (a model server puts the raw text there when the model's JSON
 * is broken): valid JSON is sent as the parsed value; a generation tool's string that only lacks closing brackets is
 * repaired and sent, with a note in the tool result; a string that cannot be repaired is answered locally with the
 * JSON error, its position and the text around it, and never reaches the broker.
 */
async function testObjectArgumentsSentAsJsonStrings() {
    const template = {
        kind: 'definition-template',
        version: '0.1.0',
        definitions: { pages: [{ id: 'contacts', definition: { name: 'ContactPage', type: 'form', fields: [{ name: 'a', type: 'text', label: 'A' }] } }] },
    };
    const text = JSON.stringify(template);
    const missingOneBrace = text.slice(0, -1);
    assert.throws(() => JSON.parse(missingOneBrace), 'the broken string is not JSON');
    const context = { projectRoot: ROOT, noConfirm: true, verbose: false };

    const client = createGenerationFakeClient();
    const agent = generationAgent(client);
    await agent.init();
    const executor = agent.governedExecutor;

    // Valid JSON strings: the parsed object and array are sent.
    await executor.executeTool('validate_definition', { template: text, page_ids: '["contacts"]' }, context);
    let submitted = client.getSubmitted();
    assert.strictEqual(submitted.length, 1);
    assert.deepStrictEqual(submitted[0].payload.args, { template, page_ids: ['contacts'] });

    // One closing brace missing: repaired, sent as an object, and the result carries a note.
    const repairedResult = await executor.executeTool('validate_definition', { template: missingOneBrace }, context);
    submitted = client.getSubmitted();
    assert.strictEqual(submitted.length, 2);
    assert.deepStrictEqual(submitted[1].payload.args, { template });
    const repaired = JSON.parse(repairedResult);
    assert.strictEqual(repaired.ok, true, 'the broker result is kept');
    assert(repaired.agent_note.includes('missing 1 closing bracket; it was added automatically'), repaired.agent_note);

    // A missing bracket in the middle is closed where the next closer does not match.
    const middle = '{"kind":"definition-template","version":"0.1.0","definitions":{"pages":[{"id":"contacts"}}}';
    await executor.executeTool('validate_definition', { template: middle }, context);
    submitted = client.getSubmitted();
    assert.deepStrictEqual(submitted[2].payload.args.template,
        { kind: 'definition-template', version: '0.1.0', definitions: { pages: [{ id: 'contacts' }] } });

    // A page object missing its } before the next page: a comma inside an object followed by { cannot be JSON, so the }
    // is added before the comma.
    const twoPages = {
        kind: 'definition-template',
        version: '0.1.0',
        definitions: { pages: [{ id: 'a', definition: { name: 'APage' } }, { id: 'b', definition: { name: 'BPage' } }] },
    };
    const betweenPages = JSON.stringify(twoPages).replace('{"name":"APage"}},', '{"name":"APage"},');
    assert.throws(() => JSON.parse(betweenPages));
    await executor.executeTool('validate_definition', { template: betweenPages }, context);
    submitted = client.getSubmitted();
    assert.deepStrictEqual(submitted[3].payload.args.template, twoPages);

    // Not repairable (a value is missing): a local structured error, nothing sent.
    const broken = '{"kind":"definition-template","version":,"definitions":{"pages":[]}}';
    const errorResult = JSON.parse(await executor.executeTool('validate_definition', { template: broken }, context));
    assert.strictEqual(client.getSubmitted().length, 4, 'an argument that cannot be repaired is not sent to the broker');
    assert.strictEqual(errorResult.ok, false);
    assert.strictEqual(errorResult.errors.length, 1);
    const [issue] = errorResult.errors;
    assert.strictEqual(issue.code, 'ARGUMENT_JSON_INVALID');
    assert.strictEqual(issue.parameter, 'template');
    assert(issue.message.startsWith('The template argument is a string that is not valid JSON: '), issue.message);
    assert(Number.isInteger(issue.position) && issue.position > 0 && issue.position < broken.length);
    assert(issue.near.length > 0 && issue.near.length <= 60 && broken.includes(issue.near), issue.near);
    assert(issue.near.includes('"version":,'), 'the text near the error shows the problem');
    assert(issue.hint.includes('not sent to the broker'), issue.hint);

    // An extra closing bracket and an unterminated string are not repaired either.
    for (const value of [`${text}}`, '{"kind":"definition-template']) {
        const result = JSON.parse(await executor.executeTool('generate_scaffold', { template: value }, context));
        assert.strictEqual(result.errors[0].code, 'ARGUMENT_JSON_INVALID', value);
    }
    // Valid JSON of another type: also answered locally for a generation tool.
    const wrongType = JSON.parse(await executor.executeTool('validate_definition', { template: '[1,2]' }, context));
    assert.strictEqual(wrongType.errors[0].code, 'ARGUMENT_TYPE_INVALID');
    assert.strictEqual(client.getSubmitted().length, 4, 'none of these reached the broker');
    await agent.close();

    // Through the agent loop: the native tool call carries the template as a string with a missing brace, the
    // repaired request is sent, and a code-only final reply ends the run.
    {
        const loopClient = createGenerationFakeClient();
        const generateOk = JSON.stringify({ ok: true, zip: { path: 'slot/req/contacts-scaffold.zip', sha256: 'c'.repeat(64), size: 10 } });
        const sent = [];
        loopClient.submitRequest = async (capabilityId, payload) => {
            sent.push(payload);
            return { success: true, data: { execution_state: 'Succeeded', result_payload: capabilityId === 'generation.scaffold.generate' ? generateOk : '{"ok":true}' } };
        };
        const seen = scriptLlm(loopClient, [
            { tools: [['validate_definition', { template: missingOneBrace }]] },
            { tools: [['generate_scaffold', { template: missingOneBrace, title: 'Contacts' }]] },
            { content: '{"type":"final","code":"completed"}' },
        ]);
        const loopAgent = generationAgent(loopClient);
        await loopAgent.init();
        assert.strictEqual(await loopAgent.send('build it'), '{"type":"final","code":"completed"}');
        assert.strictEqual(seen.length, 3);
        assert.deepStrictEqual(sent.map((payload) => payload.args.template), [template, template]);
        assert.strictEqual(loopAgent.messages.filter((message) => message.content === GENERATION_CONTINUE_REMINDER).length, 0,
            'a repaired generate that succeeded counts as generated');
        await loopAgent.close();
    }

    // Other tools: a valid JSON string for an object parameter is still parsed; a broken one is sent unchanged
    // (the broker's schema check reports it), without a repair.
    const { coerceToolArguments, repairClosers } = require('../lib/tool-arguments');
    // The repair only adds closing brackets outside strings, and gives up when that cannot make the text JSON.
    assert.deepStrictEqual(repairClosers('{"a":[1,{"b":"}],"}'), { text: '{"a":[1,{"b":"}],"}]}', added: 2 });
    assert.deepStrictEqual(repairClosers('[{"a":1},{"b":{"c":2,{"d":3}]'), { text: '[{"a":1},{"b":{"c":2}},{"d":3}]', added: 2 });
    assert.deepStrictEqual(repairClosers('{"a":"q\\"}"'), { text: '{"a":"q\\"}"}', added: 1 }, 'an escaped quote does not end the string');
    assert.strictEqual(repairClosers('{"a":1,{"b":2}}'), null, 'a member without a key is not closed away');
    assert.strictEqual(repairClosers('{"a":1}'), null, 'nothing to add');
    assert.strictEqual(repairClosers('{"a":"open'), null, 'an unterminated string');
    assert.strictEqual(repairClosers(`${'['.repeat(65)}`), null, 'deeper than the limit');
    const objectParameters = { type: 'object', properties: { options: { type: 'object' } } };
    assert.deepStrictEqual(coerceToolArguments('some_tool', { options: '{"a":1}' }, objectParameters),
        { args: { options: { a: 1 } }, notes: [], error: null });
    assert.deepStrictEqual(coerceToolArguments('some_tool', { options: '{"a":1' }, objectParameters),
        { args: { options: '{"a":1' }, notes: [], error: null });
}

/**
 * The broker reports execution_state with the enum names (Denied, Dispatched). The executor matches them without
 * regard to case: a denied request is reported as such, with the policy reason.
 */
async function testExecutionStateCase() {
    const client = createGenerationFakeClient();
    const agent = generationAgent(client);
    await agent.init();
    const context = { projectRoot: ROOT, noConfirm: true, verbose: false };
    const template = { kind: 'definition-template', version: '0.1.0' };

    client.submitRequest = async () => ({
        success: true,
        data: { request_id: 'req_denied', execution_state: 'Denied', policy_reason: 'Grant quota exhausted.' },
    });
    const denied = await agent.governedExecutor.executeTool('validate_definition', { template }, context);
    assert(denied.startsWith('[Governed] request denied: Grant quota exhausted.'), denied);
    assert(denied.includes('capability: generation.definition.validate'), denied);

    client.submitRequest = async () => ({ success: true, data: { request_id: 'req_wait', execution_state: 'Dispatched' } });
    assert.strictEqual(await agent.governedExecutor.executeTool('validate_definition', { template }, context),
        '[Governed] request dispatched (request_id: req_wait)');
    await agent.close();

    // The generation prompt names the reason the broker gives when a tool's calls are used up.
    const promptClient = createGenerationFakeClient();
    const promptAgent = generationAgent(promptClient);
    await promptAgent.init();
    assert(promptAgent.messages[0].content.replace(/\s+/g, ' ').includes('(a call is denied with "Grant quota exhausted.")'));
    await promptAgent.close();
}

/**
 * Tool call arguments that nest deeper than the broker's JSON depth limit: printing or sending such arguments would
 * exhaust the call stack. Written as text, such a call is only a reply that is not the fixed JSON object (one
 * reminder, then the run fails). As a native tool call the whole response is rejected before any tool runs. Nothing
 * reaches the broker, and printing unprintable arguments never throws.
 */
async function testDeeplyNestedToolCallArguments() {
    const { MAX_ARGUMENT_DEPTH, exceedsJsonDepth } = require('../lib/tool-arguments');
    const { parseTextToolCalls } = require('../lib/text-tool-calls');
    const nested = (depth) => `${'['.repeat(depth)}${']'.repeat(depth)}`;
    const deepCall = (name, depth) => `{"name":"${name}","arguments":{"a":${nested(depth)}}}`;

    assert.strictEqual(MAX_ARGUMENT_DEPTH, 64);
    // arguments itself is one level: 63 nested arrays inside it are 64 levels, 64 arrays are 65.
    assert.strictEqual(parseTextToolCalls(deepCall('a', MAX_ARGUMENT_DEPTH - 1)).length, 1);
    assert.strictEqual(parseTextToolCalls(deepCall('a', MAX_ARGUMENT_DEPTH)).length, 0);
    assert.strictEqual(exceedsJsonDepth(JSON.parse(nested(100000))), true, 'the depth check is not recursive');

    for (const verbose of [false, true]) {
        for (const name of ['no_such_tool', 'validate_definition']) {
            // Written as text: never a tool call, whatever its depth.
            const client = createGenerationFakeClient();
            const text = deepCall(name, 1600);
            assert(text.length > 3200);
            const seen = scriptLlm(client, [{ content: text }, { content: text }]);
            const agent = generationAgent(client);
            agent.verbose = verbose;
            await agent.init();
            assert.strictEqual(await agent.send('build it'), '{"type":"failure","code":"invalid_response"}');
            assert.strictEqual(seen.length, 2, 'one reminder, then the run ends');
            assert.strictEqual(client.getSubmitted().length, 0, 'nothing reaches the broker');
            await agent.close();

            // As a native tool call: the response is rejected, no tool runs and the run ends.
            const nativeClient = createGenerationFakeClient();
            const nativeSeen = scriptLlm(nativeClient, [{ tools: [[name, { a: JSON.parse(nested(1600)) }]] }]);
            const nativeAgent = generationAgent(nativeClient);
            nativeAgent.verbose = verbose;
            await nativeAgent.init();
            assert.strictEqual(await nativeAgent.send('build it'), '{"type":"failure","code":"invalid_response"}');
            assert.strictEqual(nativeSeen.length, 1);
            assert.strictEqual(nativeClient.getSubmitted().length, 0, 'a too deep native call never reaches the broker');
            assert(!nativeAgent.messages.some((message) => message.role === 'tool'), 'no tool result for a rejected response');
            await nativeAgent.close();
        }
    }

    // The limit itself: 64 levels pass, 65 are rejected (as a JSON object and as a JSON string).
    const client = createGenerationFakeClient();
    const agent = generationAgent(client);
    await agent.init();
    const chatWith = async (args) => {
        client.llmChat = async () => ({ success: true, data: {
            content: '', tool_calls: [{ function: { name: 'validate_definition', arguments: args } }], done: true, model: 'broker-model',
        } });
        return await agent.governedExecutor.chat({ model: 'x', messages: [], tools: [] });
    };
    const atLimit = { a: JSON.parse(nested(MAX_ARGUMENT_DEPTH - 1)) };
    const overLimit = { a: JSON.parse(nested(MAX_ARGUMENT_DEPTH)) };
    assert.deepStrictEqual((await chatWith(atLimit)).toolCalls[0].function.arguments, atLimit);
    assert.deepStrictEqual((await chatWith(JSON.stringify(atLimit))).toolCalls[0].function.arguments, atLimit);
    await assert.rejects(() => chatWith(overLimit), /invalid tool call arguments; response rejected/);
    await assert.rejects(() => chatWith(JSON.stringify(overLimit)), /invalid tool call arguments; response rejected/);
    await agent.close();

    // Printing arguments that cannot be serialised does not throw.
    assert.strictEqual(agent._formatArgs({ a: JSON.parse(nested(1600)) }), '(arguments not printable)');
}

/**
 * --run: an exception in the run still closes the agent (and with it the broker session) before the process ends,
 * so the generation watchdog sees at once that the agent ended without a package. The exit code is set, the process
 * is not ended inside the catch. Governed runs: the exit code is 0 for every model-level outcome (the outcome is the
 * JSON object on stdout) and 1 only for an infrastructure failure, because a non-zero exit restarts the container.
 */
async function testRunOnceClosesTheAgent() {
    const { runOnce } = require('../lib/run-once');
    const events = [];
    const failing = {
        async send() { events.push('send'); throw new RangeError('Maximum call stack size exceeded'); },
        async close() { events.push('close'); },
    };
    assert.strictEqual(await runOnce(failing, 'build it'), 1);
    assert.deepStrictEqual(events, ['send', 'close']);

    events.length = 0;
    const passing = {
        async send() { events.push('send'); return 'done'; },
        async close() { events.push('close'); },
    };
    assert.strictEqual(await runOnce(passing, 'build it'), 0);
    assert.deepStrictEqual(events, ['send', 'close']);

    const originalWrite = process.stdout.write;
    const stdout = [];
    process.stdout.write = function capture(chunk) { stdout.push(String(chunk)); return true; };
    try {
        const governed = (result, close = async () => {}) => ({
            governedExecutor: {},
            async send() { return result; },
            close,
        });
        // Model-level outcomes exit 0, whatever the model decided: the container is started with
        // --restart on-failure, so a non-zero exit would run the same work item again. The outcome is the JSON.
        assert.strictEqual(await runOnce(governed('{"type":"final","code":"completed"}'), 'go'), 0);
        assert.strictEqual(await runOnce(governed('{"type":"refusal","code":"policy_denied"}'), 'go'), 0);
        assert.strictEqual(await runOnce(governed('{"type":"refusal","code":"unsupported_request"}'), 'go'), 0);
        assert.strictEqual(await runOnce(governed('{"type":"failure","code":"limit_reached"}'), 'go'), 0);
        assert.strictEqual(await runOnce(governed('{"type":"failure","code":"generation_failed"}'), 'go'), 0);
        assert.strictEqual(await runOnce(governed('{"type":"failure","code":"invalid_response"}'), 'go'), 0);
        // What send returns is not the fixed JSON (it never should be): reported as invalid_response, still exit 0.
        assert.strictEqual(await runOnce(governed('Here is a summary of what I did.'), 'go'), 0);
        assert.strictEqual(await runOnce(governed(undefined), 'go'), 0);
        assert.deepStrictEqual(stdout, [
            '{"type":"final","code":"completed"}\n',
            '{"type":"refusal","code":"policy_denied"}\n',
            '{"type":"refusal","code":"unsupported_request"}\n',
            '{"type":"failure","code":"limit_reached"}\n',
            '{"type":"failure","code":"generation_failed"}\n',
            '{"type":"failure","code":"invalid_response"}\n',
            '{"type":"failure","code":"invalid_response"}\n',
            '{"type":"failure","code":"invalid_response"}\n',
        ], 'every model-level outcome is one JSON object on stdout');
        stdout.length = 0;

        // Infrastructure failures (an exception in the run, a failed close) exit 1, still with one JSON object.
        const failingClose = async () => { throw new Error('close failed'); };
        assert.strictEqual(await runOnce(governed('{"type":"final","code":"completed"}', failingClose), 'go'), 1);
        assert.strictEqual(await runOnce(governed('{"type":"refusal","code":"policy_denied"}', failingClose), 'go'), 1);
        assert.strictEqual(await runOnce({
            governedExecutor: {}, async send() { throw new Error('send failed'); }, async close() {},
        }, 'go'), 1);
        assert.deepStrictEqual(stdout, [
            '{"type":"failure","code":"tool_failed"}\n',
            '{"type":"refusal","code":"policy_denied"}\n',
            '{"type":"failure","code":"tool_failed"}\n',
        ], 'governed run prints exactly one final JSON object even when send or close fails; a failed close replaces only "completed"');
        stdout.length = 0;

        // A whole governed run that the model ends with prose: one reminder, then invalid_response, exit code 0.
        const proseClient = createFakeClient();
        proseClient.llmChat = async () => ({ success: true, data: {
            content: 'I have finished the task.', tool_calls: [], done: true, model: 'broker-model',
        } });
        const proseAgent = plainGovernedAgent(proseClient);
        assert.strictEqual(await runOnce(proseAgent, 'prose governed CLI run'), 0);
        assert.deepStrictEqual(stdout, ['{"type":"failure","code":"invalid_response"}\n']);
        assert.strictEqual(proseClient.getLlmChatCalls(), 0, 'the stubbed llmChat replaced the default one');
        assert.strictEqual(proseAgent.governedExecutor, null, 'the agent is closed after an invalid model reply');
        stdout.length = 0;

        const freshClient = createFakeClient();
        freshClient.llmChat = async () => ({ success: true, data: {
            content: '{"type":"final","code":"completed"}', tool_calls: [], done: true, model: 'broker-model',
        } });
        const freshAgent = new AgentLoop({
            model: 'requested', provider: createForbiddenDirectProvider(), projectRoot: ROOT, stream: false,
            governed: {
                brokerUrl: 'http://broker.local:5000', brokerPubKey: 'fake-pub-key', principalId: 'prn_test',
                taskId: 'task_test', roleId: 'role_reader', registrationSecret: TEST_REGISTRATION_SECRET,
                clientFactory: () => freshClient,
            },
        });
        assert.strictEqual(await runOnce(freshAgent, 'fresh governed CLI run'), 0);
        assert.deepStrictEqual(stdout, ['{"type":"final","code":"completed"}\n'],
            'a fresh AgentLoop is recognized as governed before lazy init');
        stdout.length = 0;

        const verboseToolClient = createFakeClient();
        let verboseChats = 0;
        verboseToolClient.llmChat = async () => {
            verboseChats++;
            return { success: true, data: verboseChats === 1 ? {
                content: '', tool_calls: [{ id: 'verbose-read', type: 'function', function: {
                    name: 'read_file', arguments: { path: './README.md' },
                } }], done: true, model: 'broker-model',
            } : {
                content: '{"type":"final","code":"completed"}', tool_calls: [], done: true, model: 'broker-model',
            } };
        };
        const verboseToolAgent = new AgentLoop({
            model: '', provider: createForbiddenDirectProvider(), projectRoot: ROOT, stream: false, verbose: true,
            governed: {
                brokerUrl: 'http://broker.local:5000', brokerPubKey: 'fake-pub-key', principalId: 'prn_test',
                taskId: 'task_test', roleId: 'role_reader', registrationSecret: TEST_REGISTRATION_SECRET,
                clientFactory: () => verboseToolClient,
            },
        });
        assert.strictEqual(await runOnce(verboseToolAgent, 'call read_file'), 0);
        assert.deepStrictEqual(stdout, ['{"type":"final","code":"completed"}\n'],
            'verbose governed tool diagnostics never contaminate stdout');
        assert.strictEqual(verboseToolClient.getSubmitCalls(), 1);
        stdout.length = 0;

        const initFailureClient = createFakeClient();
        let closedAfterInitFailure = false;
        initFailureClient.registerSession = async () => { throw new Error('mock init failure'); };
        const initFailureAgent = new AgentLoop({
            model: 'requested', provider: createForbiddenDirectProvider(), projectRoot: ROOT, stream: false,
            governed: {
                brokerUrl: 'http://broker.local:5000', brokerPubKey: 'fake-pub-key', principalId: 'prn_test',
                taskId: 'task_test', roleId: 'role_reader', registrationSecret: TEST_REGISTRATION_SECRET,
                clientFactory: () => initFailureClient,
            },
        });
        const closeAgent = initFailureAgent.close.bind(initFailureAgent);
        initFailureAgent.close = async () => { closedAfterInitFailure = true; await closeAgent(); };
        assert.strictEqual(await runOnce(initFailureAgent, 'fresh failing governed CLI run'), 1);
        assert(closedAfterInitFailure, 'runOnce closes a newly-created agent when lazy init fails');
        assert.deepStrictEqual(stdout, ['{"type":"failure","code":"tool_failed"}\n'],
            'init failure still yields one machine-readable governed result');
    } finally {
        process.stdout.write = originalWrite;
    }

    const { spawnSync } = require('child_process');
    const cliEnv = { ...process.env };
    for (const key of ['BROKER_URL', 'BROKER_PUB_KEY', 'BROKER_PRINCIPAL_ID', 'BROKER_TASK_ID', 'BROKER_ROLE_ID', 'BROKER_REGISTRATION_SECRET']) {
        delete cliEnv[key];
    }
    const configFailure = spawnSync(process.execPath, ['tools/agent/agent.js', '--governed', '--run', 'probe'], {
        cwd: ROOT, env: cliEnv, encoding: 'utf8',
    });
    assert.strictEqual(configFailure.status, 1, 'incomplete governed configuration is an infrastructure failure');
    assert.strictEqual(configFailure.stdout, '{"type":"refusal","code":"invalid_request"}\n',
        'real CLI governed --run setup failures keep stdout machine-readable');

    // The broker cannot be reached (nothing listens on the port): an infrastructure failure, exit code 1.
    const closedPort = await new Promise((resolve, reject) => {
        const server = require('net').createServer();
        server.on('error', reject);
        server.listen(0, '127.0.0.1', () => {
            const { port } = server.address();
            server.close(() => resolve(port));
        });
    });
    const unreachable = spawnSync(process.execPath, ['tools/agent/agent.js', '--governed', '--run', 'probe'], {
        cwd: ROOT,
        env: {
            ...cliEnv,
            BROKER_URL: `http://127.0.0.1:${closedPort}`,
            BROKER_PUB_KEY: crypto.generateKeyPairSync('ec', { namedCurve: 'P-256' }).publicKey.export({ type: 'spki', format: 'der' }).toString('base64'),
            BROKER_PRINCIPAL_ID: 'prn_unreachable',
            BROKER_TASK_ID: 'task_unreachable',
            BROKER_REGISTRATION_SECRET: TEST_REGISTRATION_SECRET,
        },
        encoding: 'utf8',
        timeout: 60000,
    });
    assert.strictEqual(unreachable.status, 1, `an unreachable broker exits 1\n${unreachable.stderr}`);
    assert.strictEqual(unreachable.stdout, '{"type":"failure","code":"tool_failed"}\n');
    assert(!`${unreachable.stdout}${unreachable.stderr}`.includes(TEST_REGISTRATION_SECRET), 'the failure output carries no secret');

    const fs = require('fs');
    const agentCli = fs.readFileSync(path.join(ROOT, 'tools', 'agent', 'agent.js'), 'utf8');
    const start = agentCli.indexOf('if (args.run) {');
    assert(start >= 0, 'agent.js has the --run branch');
    const branch = agentCli.slice(start, agentCli.indexOf('return;', start));
    assert(branch.includes('process.exitCode = await runOnce(agent, args.run'), 'the --run branch uses runOnce and sets the exit code');
    assert(!branch.includes('process.exit('), 'the --run branch does not end the process before the agent is closed');
}

/**
 * Native tool calls are normalised, not matched key by key. Providers add their own fields (Ollama sends
 * function.index and may send no id, other formats send an empty id): only id, type, function.name and
 * function.arguments are read, and a call without a usable id gets a generated one. What is malformed still rejects
 * the whole response before any tool runs.
 */
async function testNativeToolCallNormalisation() {
    const client = createFakeClient();
    const agent = plainGovernedAgent(client);
    await agent.init();
    const executor = agent.governedExecutor;
    const chatWith = async (toolCalls, content = '') => {
        client.llmChat = async () => ({ success: true, data: { content, tool_calls: toolCalls, done: true, model: 'broker-model' } });
        return await executor.chat({ model: 'x', messages: [{ role: 'user', content: 'hi' }], tools: [] });
    };
    const read = (path) => ({ name: 'read_file', arguments: { path } });

    // What Ollama returns: function.index, no id (or null), extra keys on the call and on the function.
    const first = await chatWith([
        { function: { index: 0, name: 'read_file', arguments: { path: './a.md' } } },
        { id: null, type: 'function', index: 1, function: { index: 1, name: 'read_file', arguments: '{"path":"./b.md"}', strict: true } },
        { id: '', function: { name: 'read_file', arguments: { path: './c.md' } } },
        { id: 'call_from_model', type: 'function', function: { name: 'read_file', arguments: { path: './d.md' } } },
    ]);
    assert.deepStrictEqual(first.toolCalls, [
        { id: 'governed_call_1', function: read('./a.md') },
        { id: 'governed_call_2', function: read('./b.md') },
        { id: 'governed_call_3', function: read('./c.md') },
        { id: 'call_from_model', function: read('./d.md') },
    ], 'only id, name and arguments are kept; missing ids are generated in order');
    // Generated ids stay unique across turns of the same run.
    const second = await chatWith([{ function: { index: 0, name: 'read_file', arguments: { path: './e.md' } } }]);
    assert.strictEqual(second.toolCalls[0].id, 'governed_call_4');

    // A call without arguments (missing, null, empty string) is a call with no arguments.
    for (const args of [undefined, null, '', '  ']) {
        const fn = { name: 'read_file' };
        if (args !== undefined) fn.arguments = args;
        assert.deepStrictEqual((await chatWith([{ id: 'x', function: fn }])).toolCalls, [{ id: 'x', function: { name: 'read_file', arguments: {} } }]);
    }

    // No tool calls at all: a missing or null tool_calls field is an empty list.
    assert.deepStrictEqual((await chatWith(null, '{"type":"final","code":"completed"}')).toolCalls, []);
    assert.deepStrictEqual((await chatWith(undefined, '{"type":"final","code":"completed"}')).toolCalls, []);

    // The limit of eight calls per turn.
    const many = (count) => Array.from({ length: count }, (_, index) => ({ function: { index, name: 'read_file', arguments: { path: `./${index}.md` } } }));
    assert.strictEqual((await chatWith(many(8))).toolCalls.length, 8);
    await assert.rejects(() => chatWith(many(9)), /too many tool calls; response rejected/);

    // Malformed or unsafe: the response is rejected.
    const valid = { name: 'read_file', arguments: { path: './a.md' } };
    for (const [label, toolCalls] of [
        ['tool_calls is a string', 'read_file'],
        ['tool_calls is an object', { function: valid }],
        ['a null call', [null]],
        ['a string call', ['read_file']],
        ['an array call', [[valid]]],
        ['no function', [{ id: 'x' }]],
        ['function is a string', [{ function: 'read_file' }]],
        ['function is an array', [{ function: [valid] }]],
        ['another call type', [{ type: 'code_interpreter', function: valid }]],
        ['a non-string type', [{ type: 1, function: valid }]],
        ['no name', [{ function: { arguments: {} } }]],
        ['a non-string name', [{ function: { name: 5, arguments: {} } }]],
        ['an empty name', [{ function: { name: ' ', arguments: {} } }]],
        ['an overlong name', [{ function: { name: 'x'.repeat(129), arguments: {} } }]],
        ['number arguments', [{ function: { name: 'read_file', arguments: 5 } }]],
        ['boolean arguments', [{ function: { name: 'read_file', arguments: true } }]],
        ['array arguments', [{ function: { name: 'read_file', arguments: ['./a.md'] } }]],
        ['a string that is not JSON', [{ function: { name: 'read_file', arguments: '{"path":' } }]],
        ['a string holding an array', [{ function: { name: 'read_file', arguments: '["./a.md"]' } }]],
        ['a string holding a string', [{ function: { name: 'read_file', arguments: '"./a.md"' } }]],
        ['oversized arguments', [{ function: { name: 'read_file', arguments: { pad: 'x'.repeat(257 * 1024) } } }]],
        ['an oversized argument string', [{ function: { name: 'read_file', arguments: `{"pad":"${'x'.repeat(257 * 1024)}"}` } }]],
        ['a non-string id', [{ id: 7, function: valid }]],
        ['an overlong id', [{ id: 'x'.repeat(129), function: valid }]],
        ['one bad call among good ones', [{ function: valid }, { function: { name: 'read_file', arguments: 5 } }]],
    ]) {
        await assert.rejects(() => chatWith(toolCalls), /response rejected/, label);
    }
    assert.strictEqual(client.getSubmitCalls(), 0, 'normalising never runs a tool');
    await agent.close();

    // Through the agent loop: an Ollama-shaped first turn is accepted, the tool runs once, its result carries the
    // generated id, and the run ends with the fixed JSON reply.
    {
        const loopClient = createFakeClient();
        const seen = [];
        loopClient.llmChat = async (body) => {
            seen.push(body.messages);
            return { success: true, data: seen.length === 1 ? {
                content: '', tool_calls: [{ function: { index: 0, name: 'read_file', arguments: { path: './README.md' } } }],
                done: true, model: 'broker-model',
            } : {
                content: '{"type":"final","code":"completed"}', tool_calls: [], done: true, model: 'broker-model',
            } };
        };
        const loopAgent = plainGovernedAgent(loopClient);
        await loopAgent.init();
        assert.strictEqual(await loopAgent.send('read the readme'), '{"type":"final","code":"completed"}');
        assert.strictEqual(loopClient.getSubmitCalls(), 1, 'the Ollama-shaped call runs');
        const assistant = loopAgent.messages.find((message) => Array.isArray(message.tool_calls));
        assert.deepStrictEqual(assistant.tool_calls, [{ id: 'governed_call_1', function: { name: 'read_file', arguments: { path: './README.md' } } }]);
        const toolMessage = loopAgent.messages.find((message) => message.role === 'tool');
        assert.strictEqual(toolMessage.tool_call_id, 'governed_call_1');
        assert.strictEqual(toolMessage.content, 'ok');
        await loopAgent.close();
    }

    // Through the agent loop: a malformed call ends the run before any tool is dispatched.
    {
        const loopClient = createFakeClient();
        loopClient.llmChat = async () => ({ success: true, data: {
            content: '', tool_calls: [{ function: { name: 'read_file', arguments: { path: './README.md' } } }, { function: { name: 'read_file', arguments: 5 } }],
            done: true, model: 'broker-model',
        } });
        const loopAgent = plainGovernedAgent(loopClient);
        await loopAgent.init();
        assert.strictEqual(await loopAgent.send('read the readme'), '{"type":"failure","code":"invalid_response"}');
        assert.strictEqual(loopClient.getSubmitCalls(), 0, 'no call of a rejected response runs, not even the valid one');
        await loopAgent.close();
    }
}

/**
 * Models (Claude and reasoning models in particular) often send a sentence next to their native tool calls. That
 * text is dropped: never parsed, never executed, not kept in the conversation and not used as the final answer,
 * whatever its length. The 4096 character limit still applies to a reply without tool calls (the final answer).
 */
async function testProseAlongsideNativeToolCalls() {
    const readCall = { id: 'call_read', type: 'function', function: { name: 'read_file', arguments: { path: './README.md' } } };
    const textCall = '<tool_call>{"name":"run_command","arguments":{"command":"dir"}}</tool_call>';
    const fence = '```';

    for (const prose of [
        'I will read the file first.',
        'x'.repeat(5000),
        '{"type":"final","code":"completed"}',
        '{"type":"refusal","code":"policy_denied"}',
        textCall,
        `${fence}json\n{"name":"read_file","arguments":{"path":"./SECRET.md"}}\n${fence}`,
        '   ',
    ]) {
        const client = createFakeClient();
        const seen = [];
        client.llmChat = async (body) => {
            seen.push(body.messages.map((message) => ({ ...message })));
            return { success: true, data: seen.length === 1
                ? { content: prose, tool_calls: [readCall], done: true, model: 'broker-model' }
                : { content: '{"type":"final","code":"completed"}', tool_calls: [], done: true, model: 'broker-model' } };
        };
        const agent = plainGovernedAgent(client);
        await agent.init();
        const label = prose.slice(0, 40);
        assert.strictEqual(await agent.send('read the readme'), '{"type":"final","code":"completed"}', label);
        assert.strictEqual(seen.length, 2, `the run goes on after the tool call: ${label}`);
        assert.strictEqual(client.getSubmitCalls(), 1, `only the native call runs: ${label}`);
        const assistant = agent.messages.find((message) => Array.isArray(message.tool_calls));
        assert.strictEqual(assistant.content, '', `the prose is not kept in the conversation: ${label}`);
        assert.deepStrictEqual(assistant.tool_calls.map((call) => call.function.name), ['read_file']);
        if (prose.trim() !== '') {
            // The system prompt itself quotes the fixed JSON replies, so only the conversation after it is checked.
            const conversation = JSON.stringify(seen[1].filter((message) => message.role !== 'system'));
            assert(!conversation.includes(JSON.stringify(prose).slice(1, -1)), `the prose is not sent back to the model: ${label}`);
        }
        await agent.close();
    }

    // Straight from the executor: the content of a tool-call turn is empty, whatever the broker sent.
    const client = createFakeClient();
    const agent = plainGovernedAgent(client);
    await agent.init();
    const chatWith = async (content, toolCalls) => {
        client.llmChat = async () => ({ success: true, data: { content, tool_calls: toolCalls, done: true, model: 'broker-model' } });
        return await agent.governedExecutor.chat({ model: 'x', messages: [{ role: 'user', content: 'hi' }], tools: [] });
    };
    for (const content of ['Reading now.', 'y'.repeat(100000), null, 42, { text: 'structured' }]) {
        const result = await chatWith(content, [readCall]);
        assert.strictEqual(result.content, '');
        assert.strictEqual(result.toolCalls.length, 1);
    }

    // Without tool calls the reply is the final answer: the length limit and the string check stay.
    assert.strictEqual((await chatWith('x'.repeat(4096), [])).content.length, 4096);
    assert.strictEqual((await chatWith(null, [])).content, '');
    for (const content of ['x'.repeat(4097), 42, { text: 'structured' }, ['a']]) {
        await assert.rejects(() => chatWith(content, []), /oversized or invalid assistant content; response rejected/);
    }
    await agent.close();

    // Through the loop: long prose without a tool call still ends the run, and is never the answer.
    const longClient = createFakeClient();
    longClient.llmChat = async () => ({ success: true, data: { content: 'x'.repeat(5000), tool_calls: [], done: true, model: 'broker-model' } });
    const longAgent = plainGovernedAgent(longClient);
    await longAgent.init();
    assert.strictEqual(await longAgent.send('hello'), '{"type":"failure","code":"invalid_response"}');
    await longAgent.close();
}

/**
 * --line-listen drives its own loop. It runs native tool calls only: reply text is never parsed as a tool call,
 * also when native tool calling is off (then no tools are offered and the reply is plain text for the LINE user).
 */
async function testLineListenerRunsNativeToolCallsOnly() {
    const textCall = '<tool_call>\n{"name": "read_file", "arguments": {"path": "./README.md"}}\n</tool_call>';
    const makeAgent = (useNativeTools, replies) => {
        const executed = [];
        const chats = [];
        return {
            executed,
            chats,
            agent: {
                messages: [{ role: 'system', content: 'system prompt' }],
                maxIterations: 4,
                model: 'broker-model',
                projectRoot: ROOT,
                useNativeTools,
                toolDefinitions: [{ type: 'function', function: { name: 'read_file', parameters: { type: 'object' } } }],
                provider: {
                    async chat(params) {
                        chats.push(params);
                        const reply = replies.shift();
                        assert(reply, 'the listener asked the model more often than expected');
                        return reply;
                    },
                },
                governedExecutor: {
                    async executeTool(name, args) {
                        executed.push({ name, args });
                        return 'file content';
                    },
                },
            },
        };
    };

    // Native tool calling off (the broker says the model has none, or ReAct was forced): a <tool_call> block in the
    // reply is not run, no tools are offered, and the text is the reply.
    {
        const { agent, executed, chats } = makeAgent(false, [{ content: textCall, toolCalls: [] }]);
        const listener = new LineListener(agent);
        const reply = await listener._sendToLlm([{ role: 'user', content: 'read the readme' }]);
        assert.strictEqual(reply, textCall, 'the text is passed on as text');
        assert.deepStrictEqual(executed, [], 'a tool call written as text is never executed');
        assert.strictEqual(chats.length, 1);
        assert(!('tools' in chats[0]), 'no tools are offered without native tool calling');
        assert(!agent.messages.some((message) => message.role === 'tool' || Array.isArray(message.tool_calls)));
    }

    // Native tool calling on: text is still not run; a native call runs once and its result goes back as a tool message.
    {
        const { agent, executed, chats } = makeAgent(true, [
            { content: '', toolCalls: [{ id: 'call_1', function: { name: 'read_file', arguments: { path: './README.md' } } }] },
            { content: `Here it is. ${textCall}`, toolCalls: [] },
        ]);
        const listener = new LineListener(agent);
        const reply = await listener._sendToLlm([{ role: 'user', content: 'read the readme' }]);
        assert.strictEqual(reply, `Here it is. ${textCall}`);
        assert.deepStrictEqual(executed, [{ name: 'read_file', args: { path: './README.md' } }], 'only the native call runs');
        assert.strictEqual(chats.length, 2);
        assert.strictEqual(chats[0].tools.length, 1);
        const toolMessage = agent.messages.find((message) => message.role === 'tool');
        assert.deepStrictEqual(toolMessage, { role: 'tool', content: 'file content', tool_call_id: 'call_1' });
        // The listener's own system message says that replies are text for the LINE user, tools native only.
        const lineContext = agent.messages[1];
        assert.strictEqual(lineContext.role, 'system');
        assert(lineContext.content.includes('不要使用上方 Governed Broker Contract 規定的固定 JSON 回覆'));
        assert(lineContext.content.includes('工具只能以原生 function call 呼叫'));
    }

    const source = require('fs').readFileSync(path.join(ROOT, 'tools', 'agent', 'lib', 'line-listener.js'), 'utf8');
    assert(!source.includes('react-parser') && !source.includes('parseToolCalls'), 'the listener has no text tool-call parser');
}

/** The text parser only accepts objects with a string name and object arguments. */
function testTextToolCallParser() {
    const { parseTextToolCalls } = require('../lib/text-tool-calls');
    const fence = '```';
    assert.deepStrictEqual(parseTextToolCalls(`${fence}json\n{"name":"a","arguments":{"x":1}}\n${fence}`), [{ name: 'a', arguments: { x: 1 } }]);
    assert.deepStrictEqual(parseTextToolCalls(`${fence}\n{"name":"a","arguments":{}}`), [{ name: 'a', arguments: {} }]);
    assert.deepStrictEqual(parseTextToolCalls('Call: {"name":"a","arguments":{"y":"z"},}'), [{ name: 'a', arguments: { y: 'z' } }]);
    assert.deepStrictEqual(parseTextToolCalls('[{"name":"a","arguments":{}},{"name":"b","arguments":{}}]').map((entry) => entry.name), ['a', 'b']);
    assert.deepStrictEqual(parseTextToolCalls('{"tool_calls":[{"function":{"name":"a","arguments":{}}}]}').map((entry) => entry.name), ['a']);
    assert.deepStrictEqual(parseTextToolCalls('<tool_call>{"name":"a","arguments":{}}</tool_call>').map((entry) => entry.name), ['a']);
    for (const text of [
        '',
        'no json here',
        '{"kind":"definition-template"}',
        '{"name":"a","arguments":"{}"}',
        '{"name":"a"}',
        '{"name":5,"arguments":{}}',
        '{"name":"a","arguments":[1]}',
    ]) {
        assert.deepStrictEqual(parseTextToolCalls(text), [], text);
    }
    // A fence in another language is skipped; the bare JSON fallback still finds an object written in the text.
    assert.deepStrictEqual(parseTextToolCalls(`${fence}python\nprint(1)\n${fence}\n{"name":"a","arguments":{}}`).map((entry) => entry.name), ['a']);
    assert.deepStrictEqual(parseTextToolCalls(`{"name":"a","arguments":{"pad":"${'x'.repeat(300 * 1024)}"}}`), [], 'oversized text is ignored');
}

/** AGENT_MAX_ITERATIONS and --max-iterations accept integers from 1 to 100; anything else keeps the default. */
function testMaxIterationsParsing() {
    const { parseMaxIterations, DEFAULT_MAX_ITERATIONS } = require('../lib/utils');
    assert.strictEqual(DEFAULT_MAX_ITERATIONS, 20);
    assert.strictEqual(parseMaxIterations('12'), 12);
    assert.strictEqual(parseMaxIterations(' 4 '), 4);
    assert.strictEqual(parseMaxIterations(100), 100);
    for (const invalid of [undefined, '', '0', '-3', '101', '7x', '1.5', 'NaN']) {
        assert.strictEqual(parseMaxIterations(invalid), DEFAULT_MAX_ITERATIONS, `rejects ${invalid}`);
    }
    assert.strictEqual(parseMaxIterations('abc', 9), 9);

    const fs = require('fs');
    const agentCli = fs.readFileSync(path.join(ROOT, 'tools', 'agent', 'agent.js'), 'utf8');
    assert(agentCli.includes('parseMaxIterations(process.env.AGENT_MAX_ITERATIONS'), 'agent.js reads AGENT_MAX_ITERATIONS');
    const entrypoint = fs.readFileSync(path.join(ROOT, 'tools', 'agent', 'container', 'entrypoint.sh'), 'utf8');
    assert(entrypoint.includes('--max-iterations "$AGENT_MAX_ITERATIONS"'), 'the container entrypoint passes AGENT_MAX_ITERATIONS');
}

/** The registration secret must never reach the prompt context or the system prompt (both go to the model). */
function assertSecretNotExposed(promptContext, prompt) {
    assert(!JSON.stringify(promptContext).includes(TEST_REGISTRATION_SECRET), 'prompt context must not contain the registration secret');
    assert(!prompt.includes(TEST_REGISTRATION_SECRET), 'system prompt must not contain the registration secret');
    assert.strictEqual(promptContext.requestBodies.registerOuter.plaintext.registration_secret, '<registration secret>');
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
            registrationSecret: TEST_REGISTRATION_SECRET,
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
    assert.deepStrictEqual(fakeClient.getRegisteredSecrets(), [TEST_REGISTRATION_SECRET]);
    assertSecretNotExposed(promptContext, prompt);
    assert(
        !util.inspect(agent.governedExecutor, { depth: 4, showHidden: true }).includes(TEST_REGISTRATION_SECRET),
        'inspecting the executor (for example in a log line) must not reveal the registration secret'
    );
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

    const allowed = await agent.governedExecutor.executeTool('read_file', { path: './README.html' }, {
        projectRoot: ROOT,
        noConfirm: true,
        verbose: false,
    });
    assert.strictEqual(allowed, 'ok');
    assert.strictEqual(fakeClient.getSubmitCalls(), 1);

    await agent.close();

    await testBrokerClientTokenRenewal();
    await testSessionRecovery();
    await testKillSwitchEndsTheExecutor();
    await testKillSwitchSeenOnlyWhenRegisteringAgain();
    await testRegistrationRefusalEndsTheExecutor();
    await testGenerationTools();
    await testLowRiskGenerationGrantsKeepTheGeneralPrompt();
    await testGenerationRunNeedsAToolCallEveryTurn();
    await testReplyTextIsNeverRunAsAToolCall();
    await testGovernedJsonResponseBoundary();
    await testNativeToolCallNormalisation();
    await testProseAlongsideNativeToolCalls();
    await testUnsupportedToolsInAGenerationTask();
    await testObjectArgumentsSentAsJsonStrings();
    await testExecutionStateCase();
    await testDeeplyNestedToolCallArguments();
    await testLineListenerRunsNativeToolCallsOnly();
    await testRunOnceClosesTheAgent();
    // The text-tool parser module still exists, but no governed code path calls it (see the tests above).
    testTextToolCallParser();
    testMaxIterationsParsing();
    console.log('Governed mode tests passed.');
}

main().catch((error) => {
    console.error(error);
    process.exit(1);
});
