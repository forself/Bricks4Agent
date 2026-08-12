import { BasicButton } from '../../common/BasicButton/BasicButton.js';

// TGOS MAP API Lite publishes this browser key for public integration. The
// endpoint is deliberately immutable at runtime: changing a meta tag must not
// turn the dynamic script loader into an arbitrary-script primitive.
const TGOS_LITE_URL = 'https://api.tgos.tw/TGOS_API/tgos?ver=2&AppID=x+JLVSx85Lk=&APIKey=in8W74q0ogpcfW/STwicK8D5QwCdddJf05/7nb+OtDh8R99YN3T0LurV4xato3TpL/fOfylvJ9Wv/khZEsXEWxsBmg+GEj4AuokiNXCh14Rei21U5GtJpIkO++Mq3AguFK/ISDEWn4hMzqgrkxNe1Q==';

let tgosLoadPromise = null;

const asArray = value => Array.isArray(value) ? value : [];
const finite = value => value !== null && value !== undefined && value !== '' && Number.isFinite(Number(value));
const escapeHtml = value => String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');

function configuredScriptUrl() {
    const url = new URL(TGOS_LITE_URL);
    if (url.protocol !== 'https:' || url.hostname !== 'api.tgos.tw' || url.pathname !== '/TGOS_API/tgos') {
        throw new Error('TGOS API URL is outside the approved endpoint.');
    }
    return url.href;
}

function safeLinkHref(value, windowRef = globalThis.window) {
    const href = String(value || '').trim();
    if (!href || /[\u0000-\u001f\u007f]/.test(href) || href.includes('\\')) return '';
    if (href.startsWith('#/')) return href;
    try {
        const base = windowRef?.location?.href || 'https://invalid.local/';
        const resolved = new URL(href, base);
        const origin = windowRef?.location?.origin;
        if (!origin || resolved.origin !== origin || !['http:', 'https:'].includes(resolved.protocol)) return '';
        return resolved.href;
    } catch {
        return '';
    }
}

export function loadTgosApi({ windowRef = globalThis.window, documentRef = globalThis.document, timeoutMs = 15000 } = {}) {
    if (windowRef?.TGOS?.TGOnlineMap) return Promise.resolve(windowRef.TGOS);
    if (tgosLoadPromise) return tgosLoadPromise;

    tgosLoadPromise = new Promise((resolve, reject) => {
        if (!documentRef?.head) {
            reject(new Error('TGOS 載入失敗：找不到文件節點。'));
            return;
        }

        const existing = documentRef.querySelector('script[data-b4a-tgos-api]');
        const script = existing || documentRef.createElement('script');
        let timer = 0;
        let poll = 0;
        const cleanup = () => {
            windowRef?.clearTimeout?.(timer);
            windowRef?.clearInterval?.(poll);
        };
        const ready = () => {
            if (!windowRef?.TGOS?.TGOnlineMap) return false;
            cleanup();
            resolve(windowRef.TGOS);
            return true;
        };
        const fail = () => {
            cleanup();
            tgosLoadPromise = null;
            reject(new Error('TGOS 地圖服務無法載入，請確認網路、CSP 與 TGOS 授權。'));
        };

        script.addEventListener('load', () => {
            if (ready()) return;
            poll = windowRef.setInterval(ready, 50);
        }, { once: true });
        script.addEventListener('error', fail, { once: true });
        timer = windowRef.setTimeout(fail, timeoutMs);

        if (!existing) {
            script.async = true;
            script.charset = 'utf-8';
            script.dataset.b4aTgosApi = 'true';
            script.src = configuredScriptUrl();
            documentRef.head.appendChild(script);
        } else if (!ready()) {
            poll = windowRef.setInterval(ready, 50);
        }
    });

    return tgosLoadPromise;
}

function markerDataUrl(color) {
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="24" height="32" viewBox="0 0 24 32"><path fill="${color}" stroke="#fff" stroke-width="1.5" d="M12 1C5.9 1 1 5.9 1 12c0 8.4 11 19 11 19s11-10.6 11-19C23 5.9 18.1 1 12 1Z"/><circle cx="12" cy="12" r="4" fill="#fff"/></svg>`;
    return `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(svg)}`;
}

const MARKERS = Object.freeze({
    red: markerDataUrl('#d93b32'),
    green: markerDataUrl('#2e9d57'),
    blue: markerDataUrl('#3478c5'),
    yellow: markerDataUrl('#e0a51b'),
});

/**
 * B4A TGOS map adapter. It deliberately fails visibly when TGOS is unavailable;
 * it never substitutes an approximate/static map for the legacy map contract.
 */
export class TgosMap {
    constructor(options = {}) {
        this.options = {
            data: [],
            height: '540px',
            coordinateSystem: 'EPSG3857',
            disableDefaultUI: true,
            emptyText: '無可定位資料',
            showList: true,
            listTitle: '查詢結果',
            geocodeMissingAddresses: false,
            fitStrategy: 'bounds',
            emptyView: null,
            minZoom: null,
            maxZoom: null,
            onLocationStats: null,
            pointBuilder: row => row?.Addr ? { x: row.Addr.lon, y: row.Addr.lat, valid: Boolean(row.Addr.ExpireDate) } : { x: row?.Lon, y: row?.Lat, valid: true },
            linkBuilder: null,
            detailBuilder: null,
            markerColorBuilder: row => String(row?.TubeStatus || '') === '1' ? 'red'
                : String(row?.TubeStatus || '') === '2' ? 'green'
                    : String(row?.TubeStatus || '') === '3' ? 'blue' : 'yellow',
            ...options,
        };
        this.data = asArray(this.options.data);
        this.markers = [];
        this.listButtons = [];
        this.map = null;
        this.infoWindow = null;
        this.locator = null;
        this.refreshPromise = Promise.resolve();
        this._refreshVersion = 0;
        this.destroyed = false;
        this._visibilityObserver = null;
        this._resolveVisibility = null;
        this.element = document.createElement('section');
        this.element.className = `b4a-tgos-map${this.options.showList ? ' b4a-tgos-map--with-list' : ''}`;
        this.element.dataset.mapEngine = 'TGOS';
        this.element.setAttribute('role', 'region');
        this.element.setAttribute('aria-label', 'TGOS 地圖');

        this.mapHost = document.createElement('div');
        this.mapHost.className = 'b4a-tgos-map__canvas';
        this.mapHost.style.height = this.options.height;
        this.status = document.createElement('p');
        this.status.className = 'b4a-tgos-map__status';
        this.status.setAttribute('role', 'status');
        this.status.textContent = 'TGOS 地圖載入中…';
        this.mapHost.appendChild(this.status);
        this.element.appendChild(this.mapHost);

        if (this.options.showList) {
            this.listPanel = document.createElement('aside');
            this.listPanel.className = 'b4a-tgos-map__list-panel';
            const heading = document.createElement('h4');
            heading.textContent = this.options.listTitle;
            this.listHost = document.createElement('div');
            this.listHost.className = 'b4a-tgos-map__list';
            this.listPanel.append(heading, this.listHost);
            this.element.appendChild(this.listPanel);
        }

        this.ready = this._initialize();
        this._renderList();
    }

    _waitUntilVisible() {
        if (this.destroyed) return Promise.resolve(false);
        const rect = this.mapHost.getBoundingClientRect?.();
        if (this.element.isConnected && rect?.width > 0 && rect?.height > 0) {
            return Promise.resolve(true);
        }
        if (typeof globalThis.ResizeObserver !== 'function') {
            return Promise.resolve(true);
        }

        return new Promise((resolve) => {
            this._resolveVisibility = resolve;
            this._visibilityObserver = new globalThis.ResizeObserver(() => {
                const next = this.mapHost.getBoundingClientRect?.();
                if (!this.element.isConnected || !(next?.width > 0) || !(next?.height > 0)) return;
                this._visibilityObserver?.disconnect?.();
                this._visibilityObserver = null;
                this._resolveVisibility = null;
                resolve(true);
            });
            this._visibilityObserver.observe(this.mapHost);
        });
    }

    async _initialize() {
        try {
            const TGOS = await loadTgosApi();
            if (this.destroyed) return;
            const visible = await this._waitUntilVisible();
            if (!visible || this.destroyed) return false;
            const coord = TGOS.TGCoordSys?.[this.options.coordinateSystem] || TGOS.TGCoordSys?.EPSG3857;
            this.map = new TGOS.TGOnlineMap(this.mapHost, coord, { disableDefaultUI: this.options.disableDefaultUI });
            this.infoWindow = new TGOS.TGInfoWindow();
            if (this.options.geocodeMissingAddresses && TGOS.TGLocateService) {
                this.locator = new TGOS.TGLocateService();
            }
            if ((finite(this.options.minZoom) || finite(this.options.maxZoom)) && TGOS.TGEvent?.addListener) {
                TGOS.TGEvent.addListener(this.map, 'zoom_changed', () => {
                    const current = Number(this.map?.getZoom?.());
                    if (!Number.isFinite(current)) return;
                    if (finite(this.options.minZoom) && current < Number(this.options.minZoom)) {
                        this.map.setZoom?.(Number(this.options.minZoom));
                    } else if (finite(this.options.maxZoom) && current > Number(this.options.maxZoom)) {
                        this.map.setZoom?.(Number(this.options.maxZoom));
                    }
                });
            }
            this.status.remove();
            this.element.dataset.ready = 'true';
            this.refreshPromise = this._refreshMarkers();
            await this.refreshPromise;
        } catch (error) {
            if (this.destroyed) return;
            this.element.dataset.ready = 'false';
            this.element.dataset.error = 'tgos-unavailable';
            this.status.textContent = error?.message || 'TGOS 地圖服務無法載入。';
            return false;
        }
        return true;
    }

    _locatedRows() {
        return this.data.map((row, sourceIndex) => {
            const point = this.options.pointBuilder?.(row) || {};
            return { row, sourceIndex, x: Number(point.x), y: Number(point.y), valid: point.valid !== false && finite(point.x) && finite(point.y) };
        }).filter(item => item.valid);
    }

    _expireDateOneYearFromNow() {
        const date = new Date();
        date.setFullYear(date.getFullYear() + 1);
        return `${date.getFullYear()}${String(date.getMonth() + 1).padStart(2, '0')}${String(date.getDate()).padStart(2, '0')}`;
    }

    _locateAddress(TGOS, address) {
        return new Promise((resolve, reject) => {
            if (!this.locator || !address?.Address) {
                reject(new Error('地址資訊不存在或 TGOS 定位服務尚未就緒。'));
                return;
            }
            this.locator.locateTWD97({ address: address.Address }, (result, status) => {
                const location = result?.[0]?.geometry?.location;
                if (status !== TGOS.TGLocatorStatus?.OK || !finite(location?.x) || !finite(location?.y)) {
                    reject(new Error('TGOS 地址定位失敗。'));
                    return;
                }
                resolve({
                    ...address,
                    RealAddress: address.Address,
                    lon: Number(location.x),
                    lat: Number(location.y),
                    ExpireDate: this._expireDateOneYearFromNow(),
                });
            });
        });
    }

    async _geocodeRows(rows) {
        if (!this.options.geocodeMissingAddresses || !this.locator || !globalThis.window?.TGOS) return rows;
        const TGOS = globalThis.window.TGOS;
        const pending = rows.map((row, index) => ({ row, index }))
            .filter(({ row }) => row?.Addr?.Address && !row.Addr.ExpireDate);
        if (!pending.length) return rows;

        this.status.textContent = 'TGOS 地址定位中…';
        if (!this.status.isConnected) this.mapHost.appendChild(this.status);
        const results = await Promise.allSettled(pending.map(({ row }) => this._locateAddress(TGOS, row.Addr)));
        const next = [...rows];
        results.forEach((result, resultIndex) => {
            if (result.status !== 'fulfilled') return;
            const { row, index } = pending[resultIndex];
            next[index] = { ...row, Addr: result.value };
        });
        return next;
    }

    async _refreshMarkers() {
        if (!this.map || this.destroyed) return false;
        const version = ++this._refreshVersion;
        const rows = await this._geocodeRows(this.data);
        if (this.destroyed || version !== this._refreshVersion) return false;
        this.data = rows;
        this._renderMarkers();
        return true;
    }

    _clearMarkers() {
        for (const marker of this.markers) {
            try { marker.setMap?.(null); } catch { /* best effort */ }
        }
        this.markers = [];
        try { this.infoWindow?.close?.(); } catch { /* best effort */ }
    }

    _renderMarkers() {
        if (!this.map || !globalThis.window?.TGOS) return;
        const TGOS = globalThis.window.TGOS;
        this._clearMarkers();
        const located = this._locatedRows();
        this.options.onLocationStats?.({
            total: this.data.length,
            located: located.length,
            failed: this.data.length - located.length,
        });
        if (!located.length) {
            this.status.textContent = this.options.emptyText;
            this.mapHost.appendChild(this.status);
            const empty = this.options.emptyView;
            if (empty && finite(empty.x) && finite(empty.y)) {
                this.map.setCenter?.(new TGOS.TGPoint(Number(empty.x), Number(empty.y)));
                if (finite(empty.zoom)) this.map.setZoom?.(Number(empty.zoom));
            }
            return;
        }
        this.status.remove();

        for (const item of located) {
            const color = this.options.markerColorBuilder?.(item.row) || 'red';
            const image = new TGOS.TGImage(MARKERS[color] || MARKERS.red, new TGOS.TGSize(24, 32), new TGOS.TGPoint(0, 0), new TGOS.TGPoint(10, 31));
            const point = new TGOS.TGPoint(item.x, item.y);
            const marker = new TGOS.TGMarker(this.map, point, String(item.row?.Name || item.row?.CarNo || ''), image);
            marker.__b4aRow = item.row;
            TGOS.TGEvent.addListener(marker, 'click', event => this._openInfo(event?.target?.__b4aRow || item.row, event?.target || marker));
            this.markers[item.sourceIndex] = marker;
        }
        this._fit(located);
    }

    _fit(located) {
        if (!located.length || !this.map || !globalThis.window?.TGOS) return;
        const TGOS = globalThis.window.TGOS;
        const xs = located.map(item => item.x);
        const ys = located.map(item => item.y);
        try {
            if (this.options.fitStrategy === 'legacy-dashboard') {
                const centerX = xs.reduce((sum, value) => sum + value, 0) / xs.length;
                const centerY = ys.reduce((sum, value) => sum + value, 0) / ys.length;
                const dimension = Math.max(Math.max(...xs) - Math.min(...xs), Math.max(...ys) - Math.min(...ys));
                let zoom = 8;
                if (dimension > 5000) zoom = 7;
                if (dimension > 10000) zoom = 6;
                if (dimension > 20000) zoom = 5;
                if (dimension > 50000) zoom = 4;
                if (dimension > 100000) zoom = 3;
                this.map.setCenter?.(new TGOS.TGPoint(centerX, centerY));
                this.map.setZoom?.(zoom);
                return;
            }
            if (located.length > 1 && TGOS.TGEnvelope && this.map.fitBounds) {
                this.map.fitBounds(new TGOS.TGEnvelope(Math.min(...xs), Math.max(...ys), Math.max(...xs), Math.min(...ys)));
            } else {
                this.map.setCenter?.(new TGOS.TGPoint(xs[0], ys[0]));
                this.map.setZoom?.(12);
            }
        } catch { /* preserve the TGOS default viewport */ }
    }

    _detail(row) {
        const custom = this.options.detailBuilder?.(row);
        if (Array.isArray(custom)) return custom;
        const address = row?.Addr?.Address || row?.Address || '';
        return [['地址', address]];
    }

    _infoHtml(row) {
        const href = safeLinkHref(this.options.linkBuilder?.(row));
        const name = row?.Name || row?.CarNo || row?.Key || '檢視';
        const title = href
            ? `<a href="${escapeHtml(href)}" target="_blank" rel="noopener noreferrer">${escapeHtml(name)}</a>`
            : escapeHtml(name);
        const details = this._detail(row)
            .filter(([, value]) => value !== undefined && value !== null && String(value) !== '')
            .map(([label, value]) => `<br>${escapeHtml(label)}：${escapeHtml(value)}`)
            .join('');
        return `${title}${details}`;
    }

    _openInfo(row, marker) {
        if (!this.map || !this.infoWindow || !marker || !globalThis.window?.TGOS) return;
        this.infoWindow.close();
        this.infoWindow.setOptions({
            position: marker.getPosition(),
            maxWidth: 400,
            pixelOffset: new globalThis.window.TGOS.TGSize(5, -30),
            zIndex: 99,
        });
        this.infoWindow.setContent(this._infoHtml(row));
        this.infoWindow.open(this.map);
    }

    _renderList() {
        if (!this.listHost) return;
        this.listButtons.splice(0).forEach(button => button.destroy?.());
        this.listHost.replaceChildren();
        if (!this.data.length) {
            const empty = document.createElement('p');
            empty.className = 'b4a-tgos-map__empty';
            empty.textContent = this.options.emptyText;
            this.listHost.appendChild(empty);
            return;
        }
        this.data.forEach((row, index) => {
            const button = new BasicButton({
                type: BasicButton.TYPES.CUSTOM,
                variant: 'secondary',
                showIcon: false,
                customLabel: String(row?.Name || row?.CarNo || row?.Key || `第 ${index + 1} 筆`),
                onClick: () => this._openInfo(row, this.markers[index]),
            });
            button.element.classList.add('b4a-tgos-map__list-item');
            button.element.dataset.markerIndex = String(index);
            button.mount(this.listHost);
            this.listButtons.push(button);
        });
    }

    setData(data) {
        this.data = asArray(data);
        this._renderList();
        this.refreshPromise = this._refreshMarkers();
        return this.refreshPromise;
    }

    destroy() {
        this.destroyed = true;
        this._visibilityObserver?.disconnect?.();
        this._visibilityObserver = null;
        this._resolveVisibility?.(false);
        this._resolveVisibility = null;
        this._clearMarkers();
        this.listButtons.splice(0).forEach(button => button.destroy?.());
        this.map = null;
        this.infoWindow = null;
        this.locator = null;
        this.element.remove();
    }
}

export default TgosMap;
