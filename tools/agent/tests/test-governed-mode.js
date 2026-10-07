#!/usr/bin/env node
'use strict';

const assert = require('assert');
const crypto = require('crypto');
const path = require('path');
const util = require('util');

const { AgentLoop } = require('../lib/agent-loop');
const { BrokerClient } = require('../lib/broker-client');
const { LineListener } = require('../lib/line-listener');
const { GENERATION_BASE_PROMPT, GENERATION_CONTINUE_REMINDER, isGenerationTask } = require('../lib/system-prompt');

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

/**
 * The generation prompt states that the run is unattended: a tool call in every turn until the summary, no
 * definition in text, no questions. The loop backs this up with one reminder when the model replies without a
 * tool call before a generate succeeded; a second such reply ends the run, and a summary after a successful
 * generate ends it at once.
 */
async function testGenerationRunNeedsAToolCallEveryTurn() {
    const template = { kind: 'definition-template', version: '0.1.0' };
    const generateOk = JSON.stringify({ ok: true, zip: { path: 'slot/req/contacts-scaffold.zip', sha256: 'a'.repeat(64), size: 10 } });

    // The prompt rules.
    {
        const client = createGenerationFakeClient();
        const agent = generationAgent(client);
        await agent.init();
        const prompt = agent.messages[0].content.replace(/\s+/g, ' ');
        assert(prompt.includes('Call a tool in every turn until the final summary'), 'the prompt requires a tool call every turn');
        assert(prompt.includes('Never write the definition in your reply text'), 'the prompt forbids the definition in text');
        assert(prompt.includes('You cannot ask the user anything'), 'the prompt forbids questions');
        assert(prompt.includes('make reasonable assumptions'), 'the prompt asks for assumptions');
        assert(prompt.includes('submit it directly with validate_definition'), 'step 2 submits the definition with validate_definition');
        await agent.close();
    }

    // The definition written as text first: one reminder, then the normal flow and the summary.
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
            { content: 'Generated 3 pages.' },
        ]);
        const agent = generationAgent(client);
        await agent.init();
        assert.strictEqual(agent.generationTask, true);
        const reply = await agent.send('build it');
        assert.strictEqual(reply, 'Generated 3 pages.');
        assert.strictEqual(seen.length, 4);
        const reminders = agent.messages.filter((message) => message.role === 'user' && message.content === GENERATION_CONTINUE_REMINDER);
        assert.strictEqual(reminders.length, 1, 'exactly one reminder');
        assert.strictEqual(seen[1][seen[1].length - 1].content, GENERATION_CONTINUE_REMINDER, 'the reminder follows the text-only reply');
        await agent.close();
    }

    // A clarifying question twice: one reminder, and the second text-only reply ends the run.
    {
        const client = createGenerationFakeClient();
        const seen = scriptLlm(client, [
            { content: 'Which fields do you need?' },
            { content: 'I still need the field list.' },
        ]);
        const agent = generationAgent(client);
        await agent.init();
        const reply = await agent.send('build it');
        assert.strictEqual(reply, 'I still need the field list.');
        assert.strictEqual(seen.length, 2, 'only one reminder, then the run ends');
        await agent.close();
    }

    // The reminder counts against the iteration limit.
    {
        const client = createGenerationFakeClient();
        const seen = scriptLlm(client, [{ content: 'Which fields do you need?' }]);
        const agent = generationAgent(client, 1);
        await agent.init();
        await agent.send('build it');
        assert.strictEqual(seen.length, 1, 'no reminder when the iteration limit is reached');
        await agent.close();
    }

    // A failed generate is not a success: a text-only reply after it still gets the one reminder.
    {
        const client = createGenerationFakeClient();
        client.submitRequest = async () => ({ success: true, data: { execution_state: 'Failed', result_payload: '{"ok":false,"errors":[]}' } });
        const seen = scriptLlm(client, [
            { tools: [['generate_scaffold', { template }]] },
            { content: 'Generation failed.' },
            { content: 'Generation failed again; stopping.' },
        ]);
        const agent = generationAgent(client);
        await agent.init();
        await agent.send('build it');
        assert.strictEqual(seen.length, 3);
        await agent.close();
    }

    // Other tasks never get the reminder.
    {
        const client = createFakeClient();
        let calls = 0;
        client.llmChat = async () => {
            calls += 1;
            return { success: true, data: { content: 'done', tool_calls: [], done: true, model: 'broker-model' } };
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
        assert.strictEqual(await agent.send('hello'), 'done');
        assert.strictEqual(calls, 1);
        await agent.close();
    }
}

/**
 * Small local models often write the tool call as JSON text instead of calling the tool, in native and in ReAct
 * mode. A generation task runs such calls (a fenced ```json block, also without the closing fence, bare JSON or a
 * <tools> wrapper) as tool calls, but only names the session was granted: any other name gets an "unsupported tool"
 * result that lists the available tools, and the run goes on. Other tasks, and a generation task after a successful
 * generate, never run text as a tool call.
 */
async function testGenerationToolCallsWrittenAsText() {
    const template = { kind: 'definition-template', version: '0.1.0' };
    const generateOk = JSON.stringify({ ok: true, zip: { path: 'slot/req/contacts-scaffold.zip', sha256: 'b'.repeat(64), size: 10 } });
    const call = (name, args) => JSON.stringify({ name, arguments: args });
    const fence = '```';
    const respondOk = (client) => {
        const submitted = [];
        client.submitRequest = async (capabilityId, payload) => {
            submitted.push({ capabilityId, route: payload.route, args: payload.args });
            return {
                success: true,
                data: {
                    execution_state: 'Succeeded',
                    result_payload: capabilityId === 'generation.scaffold.generate' ? generateOk : '{"ok":true}',
                },
            };
        };
        return submitted;
    };

    // Native mode: a fenced block, a block without the closing fence, bare JSON and a <tools> wrapper all run.
    {
        const client = createGenerationFakeClient();
        const submitted = respondOk(client);
        const seen = scriptLlm(client, [
            { content: `I will read the catalog.\n${fence}json\n${call('query_component_catalog', { section: 'overview' })}\n${fence}` },
            { content: `${fence}json\n${call('query_component_catalog', { section: 'field_types' })}` },
            { content: `<tools>${call('validate_definition', { template })}</tools>` },
            { content: call('generate_scaffold', { template, title: 'Contacts' }) },
            { content: 'Generated 3 pages.' },
        ]);
        const agent = generationAgent(client);
        await agent.init();
        assert.strictEqual(agent.useNativeTools, true);
        const reply = await agent.send('build it');
        assert.strictEqual(reply, 'Generated 3 pages.');
        assert.strictEqual(seen.length, 5);
        assert.deepStrictEqual(submitted.map((entry) => entry.route),
            ['query_component_catalog', 'query_component_catalog', 'validate_definition', 'generate_scaffold']);
        assert.deepStrictEqual(submitted[0].args, { section: 'overview' });
        assert.deepStrictEqual(submitted[2].args, { template });
        assert.strictEqual(agent.messages.filter((message) => message.content === GENERATION_CONTINUE_REMINDER).length, 0,
            'a call written as text is a call, not a text-only reply');
        const assistantCall = agent.messages.find((message) => message.role === 'assistant' && Array.isArray(message.tool_calls));
        assert.strictEqual(assistantCall.tool_calls[0].id, 'text_call_1_0');
        assert.strictEqual(assistantCall.tool_calls[0].function.name, 'query_component_catalog');
        const toolResults = agent.messages.filter((message) => message.role === 'tool');
        assert.strictEqual(toolResults.length, 4);
        assert.strictEqual(toolResults[0].tool_call_id, 'text_call_1_0');
        await agent.close();
    }

    // A name that is not granted is not run and does not end the run: the model gets an unsupported result.
    {
        const client = createGenerationFakeClient();
        const submitted = respondOk(client);
        const seen = scriptLlm(client, [
            { content: `${fence}json\n${call('generate_definition', { template })}\n${fence}` },
            { content: call('run_command', { command: 'dir' }) },
            { tools: [['validate_definition', { template }]] },
            { tools: [['generate_scaffold', { template }]] },
            { content: 'Generated.' },
        ]);
        const agent = generationAgent(client);
        await agent.init();
        const reply = await agent.send('build it');
        assert.strictEqual(reply, 'Generated.');
        assert.strictEqual(seen.length, 5, 'the run goes on after the unsupported names');
        assert.deepStrictEqual(submitted.map((entry) => entry.route), ['validate_definition', 'generate_scaffold'],
            'names that are not granted never reach the broker');
        const unsupported = agent.messages.filter((message) => message.role === 'tool' && message.content.startsWith('unsupported tool'));
        assert.strictEqual(unsupported.length, 2);
        assert.strictEqual(unsupported[0].content,
            'unsupported tool generate_definition; available: query_component_catalog, validate_definition, generate_scaffold');
        assert(unsupported[1].content.startsWith('unsupported tool run_command; available: '));
        assert.strictEqual(agent.messages.filter((message) => message.content === GENERATION_CONTINUE_REMINDER).length, 0);
        await agent.close();
    }

    // ReAct mode: a fenced block outside the <tool_call> wrapper runs too, and the result comes back as a tool result.
    {
        const client = createGenerationFakeClient();
        const submitted = respondOk(client);
        scriptLlm(client, [
            { content: `${fence}json\n${call('validate_definition', { template })}\n${fence}` },
            { content: `<tool_call>${call('generate_scaffold', { template })}</tool_call>` },
            { content: 'Generated.' },
        ]);
        const agent = generationAgent(client, 12, 'react');
        await agent.init();
        assert.strictEqual(agent.useNativeTools, false);
        assert.strictEqual(await agent.send('build it'), 'Generated.');
        assert.deepStrictEqual(submitted.map((entry) => entry.route), ['validate_definition', 'generate_scaffold']);
        assert(agent.messages.some((message) => message.role === 'user' && message.content.includes('"tool": "validate_definition"')));
        await agent.close();
    }

    // A call written after a successful generate ends the run as before.
    {
        const client = createGenerationFakeClient();
        const submitted = respondOk(client);
        const seen = scriptLlm(client, [
            { tools: [['generate_scaffold', { template }]] },
            { content: `Done. I called:\n${fence}json\n${call('generate_scaffold', { template })}\n${fence}` },
        ]);
        const agent = generationAgent(client);
        await agent.init();
        const reply = await agent.send('build it');
        assert(reply.startsWith('Done.'));
        assert.strictEqual(seen.length, 2);
        assert.strictEqual(submitted.length, 1, 'the summary after a successful generate is not run again');
        await agent.close();
    }

    // Other tasks never run text as a tool call.
    {
        const client = createFakeClient();
        let calls = 0;
        client.llmChat = async () => {
            calls += 1;
            return { success: true, data: { content: call('read_file', { path: './README.md' }), tool_calls: [], done: true, model: 'broker-model' } };
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
        const submitsBefore = client.getSubmitCalls();
        await agent.send('hello');
        assert.strictEqual(calls, 1);
        assert.strictEqual(client.getSubmitCalls(), submitsBefore, 'an ordinary task does not run JSON text');
        await agent.close();
    }
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
    await testGenerationToolCallsWrittenAsText();
    testTextToolCallParser();
    testMaxIterationsParsing();
    console.log('Governed mode tests passed.');
}

main().catch((error) => {
    console.error(error);
    process.exit(1);
});
