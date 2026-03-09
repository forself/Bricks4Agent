/**
 * SPA ApiService - API 服務層
 *
 * 功能：
 * - RESTful API 封裝
 * - 自動處理 JWT Token
 * - 請求/響應攔截器
 * - 錯誤統一處理
 * - 請求取消支援
 * - 快取機制
 *
 * @module ApiService
 * @version 1.0.0
 *
 * @example
 * const api = new ApiService({ baseUrl: '/api' });
 *
 * // GET 請求
 * const users = await api.get('/users');
 *
 * // POST 請求
 * const newUser = await api.post('/users', { name: 'John' });
 *
 * // 帶 Token 的請求
 * api.setToken('jwt-token-here');
 */

export class ApiService {
    /**
     * @param {Object} options - 配置選項
     * @param {string} options.baseUrl - API 基礎路徑
     * @param {number} options.timeout - 請求超時 (毫秒)
     * @param {Object} options.headers - 預設請求標頭
     */
    constructor(options = {}) {
        this.baseUrl = options.baseUrl || '';
        this.timeout = options.timeout || 30000;
        this.defaultHeaders = {
            'Content-Type': 'application/json',
            ...options.headers
        };

        this._token = null;
        this._refreshToken = null;
        this._requestInterceptors = [];
        this._responseInterceptors = [];
        this._cache = new Map();
        this._pendingRequests = new Map();
    }

    /**
     * 設定 JWT Token
     * @param {string} token - Access Token
     * @param {string} refreshToken - Refresh Token (可選)
     */
    setToken(token, refreshToken = null) {
        this._token = token;
        this._refreshToken = refreshToken;

        if (token) {
            localStorage.setItem('access_token', token);
        } else {
            localStorage.removeItem('access_token');
        }

        if (refreshToken) {
            localStorage.setItem('refresh_token', refreshToken);
        } else {
            localStorage.removeItem('refresh_token');
        }
    }

    /**
     * 取得當前 Token
     */
    getToken() {
        return this._token || localStorage.getItem('access_token');
    }

    /**
     * 清除 Token
     */
    clearToken() {
        this._token = null;
        this._refreshToken = null;
        localStorage.removeItem('access_token');
        localStorage.removeItem('refresh_token');
    }

    /**
     * 新增請求攔截器
     * @param {Function} interceptor - (config) => config
     */
    addRequestInterceptor(interceptor) {
        this._requestInterceptors.push(interceptor);
    }

    /**
     * 新增響應攔截器
     * @param {Function} interceptor - (response) => response
     */
    addResponseInterceptor(interceptor) {
        this._responseInterceptors.push(interceptor);
    }

    /**
     * GET 請求
     * @param {string} url - 請求路徑
     * @param {Object} options - 請求選項
     * @returns {Promise<any>} 響應資料
     */
    async get(url, options = {}) {
        return this._request('GET', url, null, options);
    }

    /**
     * POST 請求
     * @param {string} url - 請求路徑
     * @param {Object} data - 請求資料
     * @param {Object} options - 請求選項
     * @returns {Promise<any>} 響應資料
     */
    async post(url, data, options = {}) {
        return this._request('POST', url, data, options);
    }

    /**
     * PUT 請求
     * @param {string} url - 請求路徑
     * @param {Object} data - 請求資料
     * @param {Object} options - 請求選項
     * @returns {Promise<any>} 響應資料
     */
    async put(url, data, options = {}) {
        return this._request('PUT', url, data, options);
    }

    /**
     * PATCH 請求
     * @param {string} url - 請求路徑
     * @param {Object} data - 請求資料
     * @param {Object} options - 請求選項
     * @returns {Promise<any>} 響應資料
     */
    async patch(url, data, options = {}) {
        return this._request('PATCH', url, data, options);
    }

    /**
     * DELETE 請求
     * @param {string} url - 請求路徑
     * @param {Object} options - 請求選項
     * @returns {Promise<any>} 響應資料
     */
    async delete(url, options = {}) {
        return this._request('DELETE', url, null, options);
    }

    /**
     * 上傳檔案
     * @param {string} url - 請求路徑
     * @param {FormData} formData - 表單資料
     * @param {Function} onProgress - 進度回調
     * @returns {Promise<any>} 響應資料
     */
    async upload(url, formData, onProgress = null) {
        return new Promise((resolve, reject) => {
            const xhr = new XMLHttpRequest();
            const fullUrl = this.baseUrl + url;

            xhr.open('POST', fullUrl);

            // 設定 Token
            const token = this.getToken();
            if (token) {
                xhr.setRequestHeader('Authorization', `Bearer ${token}`);
            }

            // 進度監聽
            if (onProgress) {
                xhr.upload.onprogress = (event) => {
                    if (event.lengthComputable) {
                        const percent = Math.round((event.loaded / event.total) * 100);
                        onProgress(percent);
                    }
                };
            }

            xhr.onload = () => {
                if (xhr.status >= 200 && xhr.status < 300) {
                    try {
                        resolve(JSON.parse(xhr.responseText));
                    } catch {
                        resolve(xhr.responseText);
                    }
                } else {
                    reject(this._createError(xhr.status, xhr.responseText));
                }
            };

            xhr.onerror = () => reject(new Error('網路錯誤'));
            xhr.ontimeout = () => reject(new Error('請求超時'));

            xhr.send(formData);
        });
    }

    /**
     * 下載檔案
     * @param {string} url - 請求路徑
     * @param {string} filename - 下載檔名
     */
    async download(url, filename) {
        const response = await fetch(this.baseUrl + url, {
            headers: this._buildHeaders()
        });

        if (!response.ok) {
            throw this._createError(response.status, await response.text());
        }

        const blob = await response.blob();
        const downloadUrl = URL.createObjectURL(blob);

        const link = document.createElement('a');
        link.href = downloadUrl;
        link.download = filename;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);

        URL.revokeObjectURL(downloadUrl);
    }

    /**
     * 發送請求
     */
    async _request(method, url, data, options = {}) {
        const {
            cache = false,
            cacheTime = 60000,
            cancelKey = null,
            headers = {},
            timeout = this.timeout
        } = options;

        const fullUrl = this.baseUrl + url;
        const cacheKey = `${method}:${fullUrl}:${JSON.stringify(data)}`;

        // 檢查快取
        if (cache && method === 'GET') {
            const cached = this._getCache(cacheKey);
            if (cached) return cached;
        }

        // 取消先前的相同請求
        if (cancelKey && this._pendingRequests.has(cancelKey)) {
            this._pendingRequests.get(cancelKey).abort();
        }

        // 建立 AbortController
        const controller = new AbortController();
        if (cancelKey) {
            this._pendingRequests.set(cancelKey, controller);
        }

        // 建立請求配置
        let config = {
            method,
            headers: { ...this._buildHeaders(), ...headers },
            signal: controller.signal
        };

        if (data && (method === 'POST' || method === 'PUT' || method === 'PATCH')) {
            config.body = JSON.stringify(data);
        }

        // 執行請求攔截器
        for (const interceptor of this._requestInterceptors) {
            config = await interceptor(config);
        }

        // 設定超時
        const timeoutId = setTimeout(() => controller.abort(), timeout);

        try {
            let response = await fetch(fullUrl, config);

            clearTimeout(timeoutId);

            // 移除 pending 請求
            if (cancelKey) {
                this._pendingRequests.delete(cancelKey);
            }

            // 執行響應攔截器
            for (const interceptor of this._responseInterceptors) {
                response = await interceptor(response);
            }

            // 處理響應
            if (!response.ok) {
                await this._handleError(response);
            }

            const result = await this._parseResponse(response);

            // 設定快取
            if (cache && method === 'GET') {
                this._setCache(cacheKey, result, cacheTime);
            }

            return result;

        } catch (error) {
            clearTimeout(timeoutId);

            if (cancelKey) {
                this._pendingRequests.delete(cancelKey);
            }

            if (error.name === 'AbortError') {
                throw new Error('請求被取消或超時');
            }

            throw error;
        }
    }

    /**
     * 建構請求標頭
     */
    _buildHeaders() {
        const headers = { ...this.defaultHeaders };

        const token = this.getToken();
        if (token) {
            headers['Authorization'] = `Bearer ${token}`;
        }

        return headers;
    }

    /**
     * 解析響應
     */
    async _parseResponse(response) {
        const contentType = response.headers.get('content-type');

        if (contentType && contentType.includes('application/json')) {
            return response.json();
        }

        const text = await response.text();
        const trimmed = text.trim();

        if (trimmed && (trimmed.startsWith('{') || trimmed.startsWith('['))) {
            try {
                return JSON.parse(trimmed);
            } catch {
                // Fall back to plain text when the response is not valid JSON.
            }
        }

        return text;
    }

    /**
     * 處理錯誤響應
     */
    async _handleError(response) {
        const { status } = response;
        let message = '請求失敗';

        try {
            const text = await response.clone().text();
            if (text) {
                try {
                    const data = JSON.parse(text);
                    message = data.message || data.error || message;
                } catch {
                    message = text;
                }
            }
        } catch {
            // Keep the default message when the body cannot be read.
        }

        // 處理特定狀態碼
        switch (status) {
            case 401:
                // 未授權，嘗試刷新 Token 或登出
                this._handleUnauthorized();
                throw this._createError(status, '未授權，請重新登入');

            case 403:
                throw this._createError(status, '沒有權限執行此操作');

            case 404:
                throw this._createError(status, '找不到請求的資源');

            case 422:
                throw this._createError(status, message);

            case 429:
                throw this._createError(status, '請求過於頻繁，請稍後再試');

            case 500:
                throw this._createError(status, '伺服器內部錯誤');

            default:
                throw this._createError(status, message);
        }
    }

    /**
     * 處理未授權
     */
    _handleUnauthorized() {
        this.clearToken();
        window.dispatchEvent(new Event('unauthorized'));
    }

    /**
     * 建立錯誤物件
     */
    _createError(status, message) {
        const error = new Error(message);
        error.status = status;
        return error;
    }

    /**
     * 取得快取
     */
    _getCache(key) {
        const cached = this._cache.get(key);
        if (!cached) return null;

        if (Date.now() > cached.expireAt) {
            this._cache.delete(key);
            return null;
        }

        return cached.data;
    }

    /**
     * 設定快取
     */
    _setCache(key, data, time) {
        this._cache.set(key, {
            data,
            expireAt: Date.now() + time
        });
    }

    /**
     * 清除快取
     */
    clearCache(pattern = null) {
        if (pattern) {
            for (const key of this._cache.keys()) {
                if (key.includes(pattern)) {
                    this._cache.delete(key);
                }
            }
        } else {
            this._cache.clear();
        }
    }
}

export default ApiService;
