export class RemotePrototypeStore {
    constructor(config, base, formal = false) { this.config = config; this.base = base; this.role = null; this.formal = formal; }
    static async connect(config, base, { formal = false } = {}) {
        const store = new RemotePrototypeStore(config, base, formal);
        let response = await fetch(`${base}/session`, { credentials: 'same-origin', cache: 'no-store' });
        if (response.status === 401) {
            if (formal) {
                const token = globalThis.prompt?.('請輸入 B4A Broker 核發的有效權杖：')?.trim();
                if (!token) throw new Error('需要 Broker 身分驗證');
                await store.#request('POST', '/auth/exchange', { scoped_token: token });
            } else await store.setRole(config.roles[0].code);
            response = await fetch(`${base}/session`, { credentials: 'same-origin', cache: 'no-store' });
        }
        if (!response.ok) throw new Error('無法建立工作階段');
        store.role = (await response.json()).role;
        return store;
    }
    async #request(method, target, body) {
        const response = await fetch(`${this.base}${target}`, {
            method, credentials: 'same-origin', cache: 'no-store',
            ...(body === undefined ? {} : { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
        });
        const result = await response.json();
        if (!response.ok) { const error = new Error(result.error || `HTTP ${response.status}`); error.status = response.status; throw error; }
        return result;
    }
    async setRole(code) {
        if (this.formal) throw new Error('正式模式的角色由 Broker 與核定政策決定');
        const result = await this.#request('POST', '/demo/session', { role_code: code });
        this.role = result.role;
    }
    async resetData() {
        if (this.formal) throw new Error('正式模式不可重設資料');
        return this.#request('POST', '/reset', {});
    }
    get(url) { return this.#request('GET', url); }
    post(url, body) { return this.#request('POST', url, body); }
    put(url, body) { return this.#request('PUT', url, body); }
    delete(url) { return this.#request('DELETE', url); }
    query(url, body) { return this.#request('POST', `${url}/query`, body); }
    lookup(url, body) { return this.#request('POST', `${url}/lookup`, body); }
    summary(url, body) { return this.#request('POST', `${url}/summary`, body); }
    async exportCsv(url, body) {
        const response = await fetch(`${this.base}${url}/export`, {
            method: 'POST', credentials: 'same-origin', cache: 'no-store',
            headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
        });
        if (!response.ok) {
            const result = await response.json();
            const error = new Error(result.error || `HTTP ${response.status}`);
            error.status = response.status;
            throw error;
        }
        return response.blob();
    }
}

export function prototypeApiBase(location = globalThis.location) {
    const match = /^\/preview\/([a-f0-9]{32})\//.exec(location.pathname);
    return match ? `/api/prototype/${match[1]}` : '/api/prototype';
}
