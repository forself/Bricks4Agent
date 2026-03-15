/**
 * Broker Client — Agent 端的加密通訊客戶端
 *
 * 負責：
 * 1. ECDH P-256 金鑰交換（session 註冊）
 * 2. AES-256-GCM 信封加密/解密
 * 3. Replay 防護（序號遞增）
 * 4. Session 生命週期（註冊、心跳、關閉）
 * 5. 執行請求提交
 *
 * 使用方式：
 *   const client = new BrokerClient('http://localhost:5000', brokerPubKeyBase64);
 *   await client.registerSession(principalId, taskId, roleId);
 *   const result = await client.submitRequest(
 *     'file.read',
 *     { route: 'read_file', args: { path: './README.md' }, project_root: '.' },
 *     'key-1'
 *   );
 *   await client.closeSession();
 *
 * 加密協議：
 * - 初始交握：ECDH P-256 → HKDF → session_key
 * - 後續請求：AES-256-GCM(session_key, nonce, aad=session_id+seq+path)
 * - 所有通訊透過加密信封，中間節點只看到密文
 */
const crypto = require('crypto');

class BrokerClient {
    /**
     * @param {string} brokerUrl - Broker API base URL (e.g., 'http://localhost:5000')
     * @param {string} brokerPubKeyBase64 - Broker 的 ECDH P-256 公鑰 (Base64 DER/SPKI)
     */
    constructor(brokerUrl, brokerPubKeyBase64) {
        this.brokerUrl = brokerUrl.replace(/\/$/, '');
        this.brokerPubKeyBase64 = brokerPubKeyBase64;
        this.sessionId = null;
        this.sessionKey = null;
        this.scopedToken = null;
        this.seq = 0;
    }

    /**
     * 1. 註冊 Session（ECDH 交握 + 取得 session_key + scoped token）
     */
    async registerSession(principalId, taskId, roleId) {
        // 生成臨時 ECDH 金鑰對 (P-256 / prime256v1)
        const ecdh = crypto.createECDH('prime256v1');
        const clientPubUncompressed = ecdh.generateKeys();

        // 將 client 公鑰轉為 SPKI DER 格式 (與 .NET ImportSubjectPublicKeyInfo 相容)
        const clientPubSpki = ecdhPubToSpki(clientPubUncompressed);
        const clientPubBase64 = clientPubSpki.toString('base64');

        // 準備註冊 payload
        const registerPayload = JSON.stringify({
            principal_id: principalId,
            task_id: taskId,
            role_id: roleId
        });

        // 用 ECDH 導出初始交握金鑰
        const brokerPubKeyBuffer = Buffer.from(this.brokerPubKeyBase64, 'base64');
        // 從 SPKI 格式提取 raw 公鑰點
        const brokerRawPub = spkiToRawPub(brokerPubKeyBuffer);
        const sharedSecret = ecdh.computeSecret(brokerRawPub);

        // HKDF → handshake key (使用 nonce 作為 salt)
        const nonce = crypto.randomBytes(12);
        const handshakeKey = crypto.hkdfSync('sha256', sharedSecret, nonce,
            Buffer.from('broker-handshake-v1'), 32);

        // AES-256-GCM 加密註冊資料
        const aad = clientPubBase64 + '/api/v1/sessions/register';
        const { ciphertext, tag } = aesGcmEncrypt(handshakeKey, nonce, Buffer.from(registerPayload), Buffer.from(aad));

        // 組裝加密請求
        const encryptedRequest = {
            v: 1,
            client_ephemeral_pub: clientPubBase64,
            envelope: {
                alg: 'ECDH-ES+A256GCM',
                seq: 0,
                nonce: nonce.toString('base64'),
                ciphertext: ciphertext.toString('base64'),
                tag: tag.toString('base64')
            }
        };

        // POST /api/v1/sessions/register
        const response = await this._post('/api/v1/sessions/register', encryptedRequest, false);

        // 從加密回應的外層讀取 session_id（明文，用於 derive session_key）
        this.sessionId = response.session_id;
        if (!this.sessionId) {
            throw new Error('Register response missing session_id in outer envelope');
        }
        this.seq = 0;

        // 用 session_id 重新 derive session_key（與 broker 端一致）
        const sessionKey = crypto.hkdfSync('sha256', sharedSecret, Buffer.from(this.sessionId),
            Buffer.from('broker-session-v1'), 32);
        this.sessionKey = Buffer.from(sessionKey);

        // 解密信封內的回應（取得 scoped_token 等）
        if (response.envelope) {
            const respAad = `${this.sessionId}0/api/v1/sessions/register`;
            const respNonce = Buffer.from(response.envelope.nonce, 'base64');
            const respCiphertext = Buffer.from(response.envelope.ciphertext, 'base64');
            const respTag = Buffer.from(response.envelope.tag, 'base64');

            const decrypted = aesGcmDecrypt(
                this.sessionKey, respNonce, respCiphertext, respTag, Buffer.from(respAad));
            const innerData = JSON.parse(decrypted.toString());

            // 從解密後的回應取得 scoped_token
            this.scopedToken = innerData.data?.scoped_token || innerData.scoped_token;

            return {
                sessionId: this.sessionId,
                scopedToken: this.scopedToken,
                expiresAt: innerData.data?.expires_at || innerData.expires_at
            };
        }

        // 若回應未加密（不應出現，但作為防禦）
        this.scopedToken = response.data?.scoped_token;
        return {
            sessionId: this.sessionId,
            scopedToken: this.scopedToken,
            expiresAt: response.data?.expires_at
        };
    }

    /**
     * 2. 提交執行請求
     */
    async submitRequest(capabilityId, payload, idempotencyKey, intent = '') {
        if (!this.sessionKey || !this.sessionId) {
            throw new Error('Session not registered. Call registerSession() first.');
        }

        const requestBody = {
            scoped_token: this.scopedToken,
            capability_id: capabilityId,
            intent: intent,
            payload: payload,
            idempotency_key: idempotencyKey
        };

        return await this._encryptedPost('/api/v1/execution-requests/submit', requestBody);
    }

    /**
     * 3. 心跳
     */
    async heartbeat() {
        return await this._encryptedPost('/api/v1/sessions/heartbeat', {
            scoped_token: this.scopedToken
        });
    }

    /**
     * 4. 優雅關閉
     */
    async closeSession(reason = 'Client closing') {
        const result = await this._encryptedPost('/api/v1/sessions/close', {
            scoped_token: this.scopedToken,
            reason
        });

        // 清零金鑰
        if (this.sessionKey) {
            this.sessionKey.fill(0);
            this.sessionKey = null;
        }
        this.sessionId = null;
        this.scopedToken = null;
        this.seq = 0;

        return result;
    }

    /**
     * 5. 查詢能力列表
     */
    async listCapabilities(filter = null) {
        return await this._encryptedPost('/api/v1/capabilities/list', {
            scoped_token: this.scopedToken,
            filter
        });
    }

    /**
     * 6. 查詢我的 grants
     */
    async listGrants() {
        return await this._encryptedPost('/api/v1/grants/list', {
            scoped_token: this.scopedToken
        });
    }

    // ── 內部方法 ──

    /**
     * 加密 POST（已建立 session 後使用）
     */
    async _encryptedPost(path, body) {
        this.seq++;
        const plaintext = JSON.stringify(body);

        // AAD = session_id + seq + path
        const aad = `${this.sessionId}${this.seq}${path}`;
        const nonce = crypto.randomBytes(12);

        const { ciphertext, tag } = aesGcmEncrypt(
            this.sessionKey, nonce, Buffer.from(plaintext), Buffer.from(aad));

        const encryptedRequest = {
            v: 1,
            session_id: this.sessionId,
            envelope: {
                alg: 'A256GCM',
                seq: this.seq,
                nonce: nonce.toString('base64'),
                ciphertext: ciphertext.toString('base64'),
                tag: tag.toString('base64')
            }
        };

        const rawResponse = await this._post(path, encryptedRequest, false);

        // 解密回應
        if (rawResponse.envelope) {
            const respAad = `${this.sessionId}${this.seq}${path}`;
            const respNonce = Buffer.from(rawResponse.envelope.nonce, 'base64');
            const respCiphertext = Buffer.from(rawResponse.envelope.ciphertext, 'base64');
            const respTag = Buffer.from(rawResponse.envelope.tag, 'base64');

            const decrypted = aesGcmDecrypt(
                this.sessionKey, respNonce, respCiphertext, respTag, Buffer.from(respAad));

            return JSON.parse(decrypted.toString());
        }

        return rawResponse;
    }

    /**
     * 發送 POST 請求
     */
    async _post(path, body, parse = true) {
        const url = `${this.brokerUrl}${path}`;
        const response = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });

        const text = await response.text();
        if (!response.ok) {
            let parsed;
            try { parsed = JSON.parse(text); } catch { parsed = { message: text }; }
            throw new Error(`Broker error ${response.status}: ${parsed.message || text}`);
        }

        return JSON.parse(text);
    }
}

// ── Crypto 工具函式 ──

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

/**
 * 將 ECDH raw 公鑰（65 bytes uncompressed）轉為 SPKI DER 格式
 * 供 .NET ECDiffieHellman.ImportSubjectPublicKeyInfo() 使用
 */
function ecdhPubToSpki(rawPubKey) {
    // SPKI DER 前綴 for P-256 uncompressed public key
    const spkiPrefix = Buffer.from([
        0x30, 0x59, // SEQUENCE (89 bytes)
        0x30, 0x13, // SEQUENCE (19 bytes)
        0x06, 0x07, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x02, 0x01, // OID 1.2.840.10045.2.1 (EC)
        0x06, 0x08, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x03, 0x01, 0x07, // OID 1.2.840.10045.3.1.7 (P-256)
        0x03, 0x42, 0x00 // BIT STRING (66 bytes, 0 unused bits)
    ]);
    return Buffer.concat([spkiPrefix, rawPubKey]);
}

/**
 * 從 SPKI DER 格式提取 raw 公鑰（65 bytes uncompressed）
 */
function spkiToRawPub(spki) {
    // P-256 SPKI 前綴固定 26 bytes，之後 1 byte (0x00 unused bits) + 65 bytes raw key
    // 但實際是 BIT STRING wrapper，raw key 在最後 65 bytes
    return spki.subarray(spki.length - 65);
}

module.exports = { BrokerClient };
