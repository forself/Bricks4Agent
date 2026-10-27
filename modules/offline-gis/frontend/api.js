// Endpoint and credentials come from trusted host code, never the definition JSON.
export class GisApi {
    constructor(base = 'api/gis/', { headers = () => ({}) } = {}) {
        this.base = new URL(base, globalThis.document?.baseURI ?? 'http://localhost/');
        if (!['http:', 'https:'].includes(this.base.protocol) || this.base.username || this.base.password
            || this.base.search || this.base.hash || !this.base.pathname.endsWith('/')) throw new Error('GIS requires an HTTP(S) service directory URL without credentials/query/fragment');
        this.headers = headers;
    }
    async request(path, { signal, body, blob = false } = {}) {
        const url = new URL(path, this.base);
        if (url.origin !== this.base.origin || !url.pathname.startsWith(this.base.pathname)) throw new Error('Request must stay inside the configured GIS service');
        const response = await fetch(url, { signal, credentials: 'same-origin',
            headers: { ...await this.headers(), ...(body ? { 'Content-Type': 'application/json' } : {}) },
            ...(body ? { method: 'POST', body: JSON.stringify(body) } : {}) });
        if (!response.ok) throw new Error(`GIS HTTP ${response.status}`);
        return blob ? response.blob() : response.json();
    }
    search(q, signal) { return this.request(`addresses?q=${encodeURIComponent(q)}`, { signal }); }
    query(body, signal) { return this.request('query', { body, signal }); }
    add(body, signal) { return this.request('markers', { body, signal }); }
    tile(z, x, y, signal) { return this.request(`tiles/${z}/${x}/${y}.png`, { blob: true, signal }); }
}
