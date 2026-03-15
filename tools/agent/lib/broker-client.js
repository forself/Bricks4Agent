'use strict';

const crypto = require('crypto');

class BrokerClient {
    constructor(brokerUrl, brokerPubKeyBase64) {
        this.brokerUrl = brokerUrl.replace(/\/$/, '');
        this.brokerPubKeyBase64 = brokerPubKeyBase64;
        this.sessionId = null;
        this.sessionKey = null;
        this.scopedToken = null;
        this.seq = 0;
        this._requestChain = Promise.resolve();
    }

    async registerSession(principalId, taskId, roleId) {
        const ecdh = crypto.createECDH('prime256v1');
        const clientPubUncompressed = ecdh.generateKeys();
        const clientPubSpki = ecdhPubToSpki(clientPubUncompressed);
        const clientPubBase64 = clientPubSpki.toString('base64');

        const registerPayload = JSON.stringify({
            principal_id: principalId,
            task_id: taskId,
            role_id: roleId,
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
            this.scopedToken = innerData.data?.scoped_token || innerData.scoped_token;
            return {
                sessionId: this.sessionId,
                scopedToken: this.scopedToken,
                expiresAt: innerData.data?.expires_at || innerData.expires_at,
            };
        }

        this.scopedToken = response.data?.scoped_token;
        return {
            sessionId: this.sessionId,
            scopedToken: this.scopedToken,
            expiresAt: response.data?.expires_at,
        };
    }

    async submitRequest(capabilityId, payload, idempotencyKey, intent = '') {
        this._ensureRegistered();
        return await this._encryptedPost('/api/v1/execution-requests/submit', {
            scoped_token: this.scopedToken,
            capability_id: capabilityId,
            intent,
            payload,
            idempotency_key: idempotencyKey,
        });
    }

    async heartbeat() {
        this._ensureRegistered();
        return await this._encryptedPost('/api/v1/sessions/heartbeat', {
            scoped_token: this.scopedToken,
        });
    }

    async closeSession(reason = 'Client closing') {
        this._ensureRegistered();
        const result = await this._encryptedPost('/api/v1/sessions/close', {
            scoped_token: this.scopedToken,
            reason,
        });

        if (this.sessionKey) {
            this.sessionKey.fill(0);
            this.sessionKey = null;
        }
        this.sessionId = null;
        this.scopedToken = null;
        this.seq = 0;

        return result;
    }

    async listCapabilities(filter = null) {
        this._ensureRegistered();
        return await this._encryptedPost('/api/v1/capabilities/list', {
            scoped_token: this.scopedToken,
            filter,
        });
    }

    async listGrants() {
        this._ensureRegistered();
        return await this._encryptedPost('/api/v1/grants/list', {
            scoped_token: this.scopedToken,
        });
    }

    async getRuntimeSpec() {
        this._ensureRegistered();
        return await this._encryptedPost('/api/v1/runtime/spec', {
            scoped_token: this.scopedToken,
        });
    }

    async llmHealth() {
        this._ensureRegistered();
        return await this._encryptedPost('/api/v1/llm/health', {
            scoped_token: this.scopedToken,
        });
    }

    async llmModels() {
        this._ensureRegistered();
        return await this._encryptedPost('/api/v1/llm/models', {
            scoped_token: this.scopedToken,
        });
    }

    async llmChat(body) {
        this._ensureRegistered();
        return await this._encryptedPost('/api/v1/llm/chat', {
            scoped_token: this.scopedToken,
            ...body,
        });
    }

    async _encryptedPost(path, body) {
        const run = async () => {
            const seq = ++this.seq;
            const plaintext = JSON.stringify(body);
            const aad = `req:${this.sessionId}${seq}${path}`;
            const nonce = crypto.randomBytes(12);

            const { ciphertext, tag } = aesGcmEncrypt(
                this.sessionKey,
                nonce,
                Buffer.from(plaintext),
                Buffer.from(aad)
            );

            const encryptedRequest = {
                v: 1,
                session_id: this.sessionId,
                envelope: {
                    alg: 'A256GCM',
                    seq,
                    nonce: nonce.toString('base64'),
                    ciphertext: ciphertext.toString('base64'),
                    tag: tag.toString('base64'),
                },
            };

            const rawResponse = await this._post(path, encryptedRequest);
            if (rawResponse.envelope) {
                const respAad = `resp:${this.sessionId}${seq}${path}`;
                const respNonce = Buffer.from(rawResponse.envelope.nonce, 'base64');
                const respCiphertext = Buffer.from(rawResponse.envelope.ciphertext, 'base64');
                const respTag = Buffer.from(rawResponse.envelope.tag, 'base64');
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
                    throw new Error(`Failed to decrypt broker response for ${path} seq=${seq}: ${error.message}`);
                }

                return JSON.parse(decrypted.toString());
            }

            return rawResponse;
        };

        const next = this._requestChain.then(run, run);
        this._requestChain = next.catch(() => {});
        return await next;
    }

    async _post(path, body) {
        const url = `${this.brokerUrl}${path}`;
        const response = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
        });

        const text = await response.text();
        if (!response.ok) {
            let parsed;
            try {
                parsed = JSON.parse(text);
            } catch {
                parsed = { message: text };
            }
            throw new Error(`Broker error ${response.status}: ${parsed.message || text}`);
        }

        return JSON.parse(text);
    }

    _ensureRegistered() {
        if (!this.sessionKey || !this.sessionId) {
            throw new Error('Session not registered. Call registerSession() first.');
        }
    }
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
