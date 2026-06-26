export class PortalApiError extends Error {
    constructor(message, status = 0, payload = null) {
        super(message);
        this.name = 'PortalApiError';
        this.status = status;
        this.payload = payload;
    }
}

export class PortalApiClient {
    constructor(options = {}) {
        this.basePath = options.basePath ?? '/api/v1/portal';
    }

    async status() {
        return await this._request('/auth/status');
    }

    async register(payload) {
        return await this._request('/auth/register', {
            method: 'POST',
            body: payload
        });
    }

    async login(payload) {
        return await this._request('/auth/login', {
            method: 'POST',
            body: payload
        });
    }

    async logout() {
        return await this._request('/auth/logout', { method: 'POST' });
    }

    async me() {
        return await this._request('/me');
    }

    async sendCommand(message) {
        return await this._request('/commands', {
            method: 'POST',
            body: { message }
        });
    }

    async results(limit = 30) {
        return await this._request(`/results?limit=${encodeURIComponent(limit)}`);
    }

    async artifacts(limit = 50) {
        return await this._request(`/artifacts?limit=${encodeURIComponent(limit)}`);
    }

    async _request(path, options = {}) {
        const headers = { Accept: 'application/json', ...(options.headers ?? {}) };
        const init = {
            method: options.method ?? 'GET',
            credentials: 'include',
            headers
        };

        if (options.body !== undefined) {
            headers['Content-Type'] = 'application/json';
            init.body = JSON.stringify(options.body);
        }

        const response = await fetch(`${this.basePath}${path}`, init);
        const payload = await readJson(response);
        if (!response.ok || payload?.success === false) {
            throw new PortalApiError(payload?.message ?? response.statusText, response.status, payload);
        }

        return payload?.data ?? payload;
    }
}

async function readJson(response) {
    const text = await response.text();
    if (!text) return null;
    try {
        return JSON.parse(text);
    } catch {
        return null;
    }
}

export default PortalApiClient;
