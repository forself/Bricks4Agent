'use strict';

const crypto = require('crypto');

class BrokerClient {
    constructor(brokerUrl, brokerPubKeyBase64) {
        this.brokerUrl = brokerUrl.replace(/\/$/, '');
        this.brokerPubKeyBase64 = brokerPubKeyBase64;
        this.sessionId = null;
        this.sessionKey = null;
        this.scopedToken = null;
        this.tokenExpiresAt = null;
        this.sessionExpiresAt = null;
        this.seq = 0;
        this._requestChain = Promise.resolve();
    }

    /**
     * Registers a session. The registration secret is sent only inside the encrypted handshake
     * payload (sealed with the broker's pinned public key); it is never logged or kept on the client.
     */
    async registerSession(principalId, taskId, roleId, registrationSecret) {
        const ecdh = crypto.createECDH('prime256v1');
        const clientPubUncompressed = ecdh.generateKeys();
        const clientPubSpki = ecdhPubToSpki(clientPubUncompressed);
        const clientPubBase64 = clientPubSpki.toString('base64');

        const registerPayload = JSON.stringify({
            principal_id: principalId,
            task_id: taskId,
            role_id: roleId,
            registration_secret: registrationSecret || '',
        });

        const brokerPubKeyBuffer = Buffer.from(this.brokerPubKeyBase64, 'base64');
        const brokerRawPub = spkiToRawPub(brokerPubKeyBuffer);
        const sharedSecret = ecdh.computeSecret(brokerRawPub);

        const nonce = crypto.randomBytes(12);
        const handshakeKey = crypto.hkdfSync(
            'sha256',
            sharedSecret,
            nonce,
            Buffer.from('broker-handshake-v1'),
            32
        );

        const aad = clientPubBase64 + '/api/v1/sessions/register';
        const { ciphertext, tag } = aesGcmEncrypt(
            handshakeKey,
            nonce,
            Buffer.from(registerPayload),
            Buffer.from(aad)
        );

        const encryptedRequest = {
            v: 1,
            client_ephemeral_pub: clientPubBase64,
            envelope: {
                alg: 'ECDH-ES+A256GCM',
                seq: 0,
                nonce: nonce.toString('base64'),
                ciphertext: ciphertext.toString('base64'),
                tag: tag.toString('base64'),
            },
        };

        const response = await this._post('/api/v1/sessions/register', encryptedRequest);

        this.sessionId = response.session_id;
        if (!this.sessionId) {
            throw new Error('Register response missing session_id');
        }
        this.seq = 0;

        const sessionKey = crypto.hkdfSync(
            'sha256',
            sharedSecret,
            Buffer.from(this.sessionId),
            Buffer.from('broker-session-v1'),
            32
        );
        this.sessionKey = Buffer.from(sessionKey);

        if (response.envelope) {
            const respAad = `resp:${this.sessionId}0/api/v1/sessions/register`;
            const respNonce = Buffer.from(response.envelope.nonce, 'base64');
            const respCiphertext = Buffer.from(response.envelope.ciphertext, 'base64');
            const respTag = Buffer.from(response.envelope.tag, 'base64');
            let decrypted;
            try {
                decrypted = aesGcmDecrypt(
                    this.sessionKey,
                    respNonce,
                    respCiphertext,
                    respTag,
                    Buffer.from(respAad)
                );
            } catch (error) {
                throw new Error(`Failed to decrypt session/register response: ${error.message}`);
            }
            const innerData = JSON.parse(decrypted.toString());
            return this._acceptRegistration(innerData.data || innerData);
        }

        return this._acceptRegistration(response.data || {});
    }

    _acceptRegistration(data) {
        this.scopedToken = data.scoped_token || null;
        this.tokenExpiresAt = data.token_expires_at || null;
        this.sessionExpiresAt = data.expires_at || null;
        return {
            sessionId: this.sessionId,
            scopedToken: this.scopedToken,
            expiresAt: this.sessionExpiresAt,
            tokenExpiresAt: this.tokenExpiresAt,
        };
    }

    async submitRequest(capabilityId, payload, idempotencyKey, intent = '') {
        this._ensureRegistered();
        return await this._encryptedPost('/api/v1/execution-requests/submit', {
            capability_id: capabilityId,
            intent,
            payload,
            idempotency_key: idempotencyKey,
        });
    }

    /**
     * Keeps the session alive and renews the scoped token. The broker answers with a new token for
     * the same session; it is stored before any request queued behind the heartbeat is sent.
     */
    async heartbeat() {
        this._ensureRegistered();
        return await this._encryptedPost('/api/v1/sessions/heartbeat', {}, (result, sessionId) => {
            const data = result?.data;
            if (!data || typeof data.scoped_token !== 'string' || !data.scoped_token) {
                return;
            }
            if (data.session_id && data.session_id !== sessionId) {
                return;
            }
            if (this.sessionId !== sessionId) {
                return;
            }
            this.scopedToken = data.scoped_token;
            this.tokenExpiresAt = data.token_expires_at || null;
            this.sessionExpiresAt = data.session_expires_at || this.sessionExpiresAt;
        });
    }

    async closeSession(reason = 'Client closing') {
        this._ensureRegistered();
        const result = await this._encryptedPost('/api/v1/sessions/close', {
            reason,
        });

        if (this.sessionKey) {
            this.sessionKey.fill(0);
            this.sessionKey = null;
        }
        this.sessionId = null;
        this.scopedToken = null;
        this.tokenExpiresAt = null;
        this.sessionExpiresAt = null;
        this.seq = 0;

        return result;
    }

    async listCapabilities(filter = null) {
        this._ensureRegistered();
        return await this._encryptedPost('/api/v1/capabilities/list', {
            filter,
        });
    }

    async listGrants() {
        this._ensureRegistered();
        return await this._encryptedPost('/api/v1/grants/list', {});
    }

    async getRuntimeSpec() {
        this._ensureRegistered();
        return await this._encryptedPost('/api/v1/runtime/spec', {});
    }

    async llmHealth() {
        this._ensureRegistered();
        return await this._encryptedPost('/api/v1/llm/health', {});
    }

    async llmModels() {
        this._ensureRegistered();
        return await this._encryptedPost('/api/v1/llm/models', {});
    }

    async llmChat(body) {
        this._ensureRegistered();
        return await this._encryptedPost('/api/v1/llm/chat', {
            ...body,
        });
    }

    /**
     * Sends one encrypted request. Requests are serialised so sequence numbers reach the broker in
     * order, and the scoped token is read when the request is actually sent, so a request queued
     * behind a heartbeat uses the renewed token. onResult runs before the next queued request.
     */
    async _encryptedPost(path, body, onResult = null) {
        const run = async () => {
            this._ensureRegistered();
            const sessionId = this.sessionId;
            const sessionKey = this.sessionKey;
            const seq = ++this.seq;
            const plaintext = JSON.stringify({ ...body, scoped_token: this.scopedToken });
            const aad = `req:${sessionId}${seq}${path}`;
            const nonce = crypto.randomBytes(12);

            const { ciphertext, tag } = aesGcmEncrypt(
                sessionKey,
                nonce,
                Buffer.from(plaintext),
                Buffer.from(aad)
            );

            const encryptedRequest = {
                v: 1,
                session_id: sessionId,
                envelope: {
                    alg: 'A256GCM',
                    seq,
                    nonce: nonce.toString('base64'),
                    ciphertext: ciphertext.toString('base64'),
                    tag: tag.toString('base64'),
                },
            };

            // Errors raised after the envelope was opened (for example an authentication failure) come
            // back encrypted with the session key, so the envelope is opened before the status is checked.
            const reply = await this._postRaw(path, encryptedRequest);
            let result = reply.parsed;
            if (reply.parsed && reply.parsed.envelope) {
                const respAad = `resp:${sessionId}${seq}${path}`;
                const respNonce = Buffer.from(reply.parsed.envelope.nonce, 'base64');
                const respCiphertext = Buffer.from(reply.parsed.envelope.ciphertext, 'base64');
                const respTag = Buffer.from(reply.parsed.envelope.tag, 'base64');
                let decrypted;
                try {
                    decrypted = aesGcmDecrypt(
                        sessionKey,
                        respNonce,
                        respCiphertext,
                        respTag,
                        Buffer.from(respAad)
                    );
                } catch (error) {
                    if (!reply.ok) {
                        throw brokerError(reply.status, 'unreadable error response');
                    }
                    throw new Error(`Failed to decrypt broker response for ${path} seq=${seq}: ${error.message}`);
                }

                result = JSON.parse(decrypted.toString());
            }

            if (!reply.ok) {
                throw brokerError(reply.status, result?.message || reply.text);
            }
            if (result === null) {
                result = JSON.parse(reply.text);
            }

            if (onResult) {
                onResult(result, sessionId);
            }

            return result;
        };

        const next = this._requestChain.then(run, run);
        this._requestChain = next.catch(() => {});
        return await next;
    }

    async _post(path, body) {
        const reply = await this._postRaw(path, body);
        if (!reply.ok) {
            throw brokerError(reply.status, reply.parsed?.message || reply.text);
        }

        return JSON.parse(reply.text);
    }

    async _postRaw(path, body) {
        const url = `${this.brokerUrl}${path}`;
        const response = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
        });

        const text = await response.text();
        let parsed = null;
        try {
            parsed = JSON.parse(text);
        } catch {
            parsed = null;
        }

        return { status: response.status, ok: response.ok, text, parsed };
    }

    _ensureRegistered() {
        if (!this.sessionKey || !this.sessionId) {
            throw new Error('Session not registered. Call registerSession() first.');
        }
    }
}

/** An HTTP error from the broker; status and the broker's message are kept for callers that recover from 401. */
function brokerError(status, message) {
    const text = typeof message === 'string' ? message : String(message ?? '');
    const error = new Error(`Broker error ${status}: ${text}`);
    error.status = status;
    error.brokerMessage = text;
    return error;
}

function aesGcmEncrypt(key, nonce, plaintext, aad) {
    const cipher = crypto.createCipheriv('aes-256-gcm', key, nonce);
    cipher.setAAD(aad);
    const ciphertext = Buffer.concat([cipher.update(plaintext), cipher.final()]);
    const tag = cipher.getAuthTag();
    return { ciphertext, tag };
}

function aesGcmDecrypt(key, nonce, ciphertext, tag, aad) {
    const decipher = crypto.createDecipheriv('aes-256-gcm', key, nonce);
    decipher.setAAD(aad);
    decipher.setAuthTag(tag);
    return Buffer.concat([decipher.update(ciphertext), decipher.final()]);
}

function ecdhPubToSpki(rawPubKey) {
    const spkiPrefix = Buffer.from([
        0x30, 0x59,
        0x30, 0x13,
        0x06, 0x07, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x02, 0x01,
        0x06, 0x08, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x03, 0x01, 0x07,
        0x03, 0x42, 0x00,
    ]);
    return Buffer.concat([spkiPrefix, rawPubKey]);
}

function spkiToRawPub(spki) {
    return spki.subarray(spki.length - 65);
}

module.exports = { BrokerClient };
