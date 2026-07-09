/**
 * LeafletMap Component
 * 使用 Leaflet + NLSC (國土測繪中心) 圖磚
 * 優點：NLSC 圖磚可能支援 CORS，可進行截圖
 */

export class LeafletMap {
    /**
     * @param {Object} options
     * @param {string|HTMLElement} options.container - 容器
     * @param {Object} options.center - {lat, lng}
     * @param {number} options.zoom - 縮放層級
     * @param {string} options.tileLayer - 圖層類型 ('nlsc'|'osm')
     * @param {Function} options.onReady - 地圖準備完成回調
     */
    constructor(options = {}) {
        this.options = {
            container: '',
            center: { lat: 25.033, lng: 121.5654 }, // 台北 101
            zoom: 12,
            tileLayer: 'nlsc', // 預設使用 NLSC
            // Leaflet 資源:預設走庫內 vendored(../vendor/leaflet/,以本模組 URL 解析,
            // 零外網、嚴格 CSP 'self' 可用)。可覆寫為自訂路徑;vendored 載入失敗才退 CDN(附警告)。
            leafletCssUrl: null,
            leafletJsUrl: null,
            leafletCssIntegrity: 'sha256-p4NxAoJBhIIN+hmNHrzRCf9tD/miZyoHS5obTRR9BMY=',
            leafletJsIntegrity: 'sha256-20nQCchB9co0qIjJZRGuk2/Z9VM+kNiyxNV1lvTlZBo=',
            ...options
        };

        this.container = typeof this.options.container === 'string'
            ? document.querySelector(this.options.container)
            : this.options.container;

        this.map = null;
        this.tileLayer = null;
        // RWD: 容器尺寸監聽狀態
        this._resizeObserver = null;
        this._resizeTimer = null;

        if (!this.container) {
            console.error('Map container not found');
            return;
        }

        this._loadLeaflet();
    }

    async _loadLeaflet() {
        const CDN_CSS = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.css';
        const CDN_JS = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.js';
        // 庫內 vendored(1.9.4,SHA-256 與上列 CDN SRI 相同):以本模組 URL 解析,任何伺服器根皆正確
        const VENDOR_CSS = new URL('../vendor/leaflet/leaflet.css', import.meta.url).href;
        const VENDOR_JS = new URL('../vendor/leaflet/leaflet.js', import.meta.url).href;

        // 若 Leaflet 已由宿主頁面載入,直接用,不再抓取
        if (typeof L !== 'undefined') {
            this._initMap();
            return;
        }

        const cssUrl = this.options.leafletCssUrl || VENDOR_CSS;
        const jsUrl = this.options.leafletJsUrl || VENDOR_JS;

        // 載入 Leaflet CSS(vendored/自訂失敗時換 CDN 備援)
        if (!document.querySelector('link[href*="leaflet"]')) {
            const link = document.createElement('link');
            link.rel = 'stylesheet';
            link.href = cssUrl;
            link.onerror = () => {
                console.warn('[LeafletMap] 本地 Leaflet CSS 載入失敗,改用 CDN 備援:' + cssUrl);
                link.onerror = null;
                link.integrity = this.options.leafletCssIntegrity;
                link.crossOrigin = '';
                link.href = CDN_CSS;
            };
            document.head.appendChild(link);
        }

        // 載入 Leaflet JS(vendored/自訂失敗時換 CDN 備援;離線+缺 vendored 才會真的失敗)
        try {
            await this._loadScript(jsUrl, null);
        } catch (e) {
            console.warn('[LeafletMap] 本地 Leaflet JS 載入失敗,改用 CDN 備援(嚴格 CSP/離線環境請確認 vendor/leaflet/ 已部署):' + jsUrl);
            await this._loadScript(CDN_JS, this.options.leafletJsIntegrity);
        }

        this._initMap();
    }

    _loadScript(src, integrity) {
        return new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = src;
            if (integrity) {
                script.integrity = integrity;
                script.crossOrigin = '';
            }
            script.onload = resolve;
            script.onerror = () => reject(new Error(`Failed to load ${src}`));
            document.head.appendChild(script);
        });
    }

    _initMap() {
        if (typeof L === 'undefined') {
            console.error('Leaflet not loaded');
            return;
        }

        try {
            // RWD: 容器 0 高時給合理 fallback,避免地圖看不見
            if (!this.container.clientHeight) {
                this.container.style.setProperty('min-height', '300px');
            }
            // 確保容器不超出父層寬度
            this.container.style.setProperty('max-width', '100%');
            this.container.style.setProperty('box-sizing', 'border-box');

            // 初始化地圖
            this.map = L.map(this.container, {
                center: [this.options.center.lat, this.options.center.lng],
                zoom: this.options.zoom
            });

            // 設定圖層
            this._setTileLayer(this.options.tileLayer);

            // RWD: 容器尺寸變化時重算地圖 (debounce 100ms),避免底圖破碎/留白
            if (typeof ResizeObserver !== 'undefined') {
                this._resizeObserver = new ResizeObserver(() => {
                    if (this._resizeTimer) clearTimeout(this._resizeTimer);
                    this._resizeTimer = setTimeout(() => {
                        this._resizeTimer = null;
                        if (this.map) this.map.invalidateSize();
                    }, 100);
                });
                this._resizeObserver.observe(this.container);
            }

            console.log('Leaflet Map Initialized');

            if (this.options.onReady) {
                this.options.onReady(this);
            }
        } catch (e) {
            console.error('Leaflet Map Init Error:', e);
            // CSP style-src 'self':inline style 屬性會被剝除,改用 CSSOM cssText
            this.container.innerHTML = '';
            const errorBox = document.createElement('div');
            errorBox.style.cssText = 'padding: 20px; color: var(--cl-danger); border:1px solid var(--cl-border); background:var(--cl-bg);';
            errorBox.textContent = `地圖初始化錯誤: ${e.message}`;
            this.container.appendChild(errorBox);
        }
    }

    /**
     * 設定圖層
     * @param {string} layerType - 'nlsc' | 'nlsc-photo' | 'osm'
     */
    _setTileLayer(layerType) {
        if (this.tileLayer) {
            this.map.removeLayer(this.tileLayer);
        }

        const layers = {
            // NLSC 通用版電子地圖
            'nlsc': {
                url: 'https://wmts.nlsc.gov.tw/wmts/EMAP/default/GoogleMapsCompatible/{z}/{y}/{x}',
                options: {
                    attribution: '© <a href="https://maps.nlsc.gov.tw/" target="_blank">國土測繪中心</a>',
                    maxZoom: 20,
                    crossOrigin: 'anonymous'
                }
            },
            // NLSC 正射影像
            'nlsc-photo': {
                url: 'https://wmts.nlsc.gov.tw/wmts/PHOTO2/default/GoogleMapsCompatible/{z}/{y}/{x}',
                options: {
                    attribution: '© <a href="https://maps.nlsc.gov.tw/" target="_blank">國土測繪中心</a>',
                    maxZoom: 20,
                    crossOrigin: 'anonymous'
                }
            },
            // OpenStreetMap (備用)
            'osm': {
                url: 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',
                options: {
                    attribution: '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
                    maxZoom: 19,
                    crossOrigin: 'anonymous'
                }
            }
        };

        const layer = layers[layerType] || layers['nlsc'];
        this.tileLayer = L.tileLayer(layer.url, layer.options);
        this.tileLayer.addTo(this.map);
    }

    /**
     * 切換圖層
     */
    switchLayer(layerType) {
        this._setTileLayer(layerType);
    }

    /**
     * 設定中心點
     */
    setCenter(lat, lng) {
        if (this.map) {
            this.map.setView([lat, lng]);
        }
    }

    /**
     * 設定縮放
     */
    setZoom(zoom) {
        if (this.map) {
            this.map.setZoom(zoom);
        }
    }

    /**
     * 取得地圖截圖（使用 leaflet-image 概念）
     * @returns {Promise<HTMLCanvasElement>}
     */
    async captureToCanvas() {
        if (!this.map) {
            throw new Error('Map not initialized');
        }

        // 等待所有圖磚載入完成
        await this._waitForTilesLoaded();

        // 取得地圖容器尺寸
        const mapSize = this.map.getSize();

        // 建立 canvas
        const canvas = document.createElement('canvas');
        canvas.width = mapSize.x;
        canvas.height = mapSize.y;
        const ctx = canvas.getContext('2d');

        // 白色背景
        ctx.fillStyle = 'var(--cl-bg)';
        ctx.fillRect(0, 0, canvas.width, canvas.height);

        // 收集所有圖磚圖片
        const tilePane = this.container.querySelector('.leaflet-tile-pane');
        if (!tilePane) {
            throw new Error('Tile pane not found');
        }

        const tiles = tilePane.querySelectorAll('img.leaflet-tile');
        const loadPromises = [];

        tiles.forEach(tile => {
            if (tile.complete && tile.naturalWidth > 0) {
                this._drawTile(ctx, tile);
            } else {
                const promise = new Promise((resolve) => {
                    const img = new Image();
                    img.crossOrigin = 'anonymous';
                    img.onload = () => {
                        this._drawTileFromImg(ctx, tile, img);
                        resolve();
                    };
                    img.onerror = () => {
                        console.warn('Failed to load tile:', tile.src);
                        resolve();
                    };
                    img.src = tile.src;
                });
                loadPromises.push(promise);
            }
        });

        await Promise.all(loadPromises);

        return canvas;
    }

    _drawTile(ctx, tile) {
        try {
            const rect = tile.getBoundingClientRect();
            const containerRect = this.container.getBoundingClientRect();
            const x = rect.left - containerRect.left;
            const y = rect.top - containerRect.top;
            ctx.drawImage(tile, x, y, rect.width, rect.height);
        } catch (e) {
            console.warn('Failed to draw tile:', e);
        }
    }

    _drawTileFromImg(ctx, tile, img) {
        try {
            const rect = tile.getBoundingClientRect();
            const containerRect = this.container.getBoundingClientRect();
            const x = rect.left - containerRect.left;
            const y = rect.top - containerRect.top;
            ctx.drawImage(img, x, y, rect.width, rect.height);
        } catch (e) {
            console.warn('Failed to draw tile from img:', e);
        }
    }

    _waitForTilesLoaded() {
        return new Promise((resolve) => {
            // 等待一段時間讓圖磚載入
            const checkLoaded = () => {
                const tiles = this.container.querySelectorAll('img.leaflet-tile');
                const allLoaded = Array.from(tiles).every(t => t.complete);
                if (allLoaded) {
                    resolve();
                } else {
                    setTimeout(checkLoaded, 100);
                }
            };

            // 最多等待 5 秒
            setTimeout(resolve, 5000);
            checkLoaded();
        });
    }

    /**
     * 銷毀地圖
     */
    destroy() {
        // RWD: 移除尺寸監聽與待執行的 debounce
        if (this._resizeObserver) {
            this._resizeObserver.disconnect();
            this._resizeObserver = null;
        }
        if (this._resizeTimer) {
            clearTimeout(this._resizeTimer);
            this._resizeTimer = null;
        }
        if (this.map) {
            this.map.remove();
            this.map = null;
        }
    }
}
