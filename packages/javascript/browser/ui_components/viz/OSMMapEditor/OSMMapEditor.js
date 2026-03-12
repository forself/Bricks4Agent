/**
 * OSMMapEditor - 通用 OSM 地圖編輯器
 * 繼承 WebPainter 的繪圖/標註/圖層能力，整合 OpenStreetMap 底圖與地理工具。
 *
 * 功能：
 * - OSM 底圖（支援多種圖磚源切換）
 * - 距離測量（Haversine 公式）
 * - 面積測量（球面 Shoelace 公式）
 * - 座標顯示面板（DD / DMS 格式切換）
 * - 比例尺與指北針
 * - GeoJSON 匯入/匯出
 * - 地圖截圖 → 設為繪圖背景
 */

import { WebPainter } from '../WebPainter/WebPainter.js';
import { LeafletMap } from '../LeafletMap.js';
import { EditorButton } from '../../common/EditorButton/EditorButton.js';
import { UploadButton } from '../../common/UploadButton/UploadButton.js';
import { ModalPanel } from '../../layout/Panel/index.js';
import Locale from '../../i18n/index.js';

export class OSMMapEditor extends WebPainter {

    /** 預設圖磚源 */
    static TILE_LAYERS = {
        osm: {
            url: 'https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
            name: 'OpenStreetMap',
            maxZoom: 19
        },
        osmHot: {
            url: 'https://{s}.tile.openstreetmap.fr/hot/{z}/{x}/{y}.png',
            attribution: '&copy; OpenStreetMap contributors, Tiles: HOT',
            name: 'Humanitarian',
            maxZoom: 19
        },
        cartoDB: {
            url: 'https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png',
            attribution: '&copy; OpenStreetMap contributors, &copy; CARTO',
            name: 'CartoDB Light',
            maxZoom: 20
        }
    };

    constructor(options = {}) {
        super(options);

        // 地圖相關
        this.leafletMap = null;
        this.mapContainer = null;
        this.showingMap = false;
        this.mapCenter = options.center || { lat: 25.033, lng: 121.565 };
        this.mapZoom = options.zoom || 12;
        this.tileLayerKey = options.tileLayer || 'osm';
        this.customTileLayers = options.tileLayers || {};

        // 座標顯示
        this.coordFormat = 'DD'; // 'DD' | 'DMS'
        this.coordPanel = null;

        // 測量工具
        this.measureMode = null; // null | 'distance' | 'area'
        this.measurePoints = [];
        this.measureLayer = null;
        this.measureMarkers = [];
        this.measureTooltip = null;

        // 比例尺與指北針
        this.scaleControl = null;
        this.compassControl = null;
        this.showCompass = options.showCompass !== false;
        this.showScale = options.showScale !== false;
        this.showCoords = options.showCoords !== false;

        this._addMapUI();
    }

    // ============================================
    // 工具列 UI
    // ============================================

    _addMapUI() {
        const toolbar = this.element.querySelector('.map-editor-toolbar');
        if (!toolbar) return;

        const strings = Locale.getComponentStrings('osmMapEditor');

        // 分隔線
        this._addSeparator(toolbar);

        // 地圖按鈕
        const mapBtn = new EditorButton({
            type: 'custom',
            label: strings.toggleMap || 'Map',
            theme: 'gradient',
            onClick: () => this._toggleMap()
        });
        mapBtn.mount(toolbar);
        this._mapBtn = mapBtn;

        // 分隔線
        this._addSeparator(toolbar);

        // 距離測量
        const T = EditorButton.TYPES;
        const distanceBtn = new EditorButton({
            type: T.MEASURE_DISTANCE,
            theme: 'gradient',
            onClick: () => this._toggleMeasureMode('distance')
        });
        distanceBtn.mount(toolbar);
        this._distanceBtn = distanceBtn;

        // 面積測量
        const areaBtn = new EditorButton({
            type: T.MEASURE_AREA,
            theme: 'gradient',
            onClick: () => this._toggleMeasureMode('area')
        });
        areaBtn.mount(toolbar);
        this._areaBtn = areaBtn;

        // 分隔線
        this._addSeparator(toolbar);

        // 匯出 GeoJSON
        const exportBtn = new EditorButton({
            type: EditorButton.TYPES.EXPORT_JSON,
            label: strings.exportGeoJSON || 'Export GeoJSON',
            theme: 'gradient',
            onClick: () => this._exportGeoJSON()
        });
        exportBtn.mount(toolbar);

        // 匯入 GeoJSON
        const importBtn = new UploadButton({
            accept: '.json,.geojson',
            label: strings.importGeoJSON || 'Import GeoJSON',
            size: 'small',
            onSelect: (files) => this._importGeoJSON(files[0])
        });
        importBtn.mount(toolbar);

        // 建立地圖容器
        this._createMapContainer();
    }

    _addSeparator(parent) {
        const sep = document.createElement('div');
        sep.style.cssText = 'width: 1px; height: 24px; background: var(--cl-divider-inverse); margin: 0 4px;';
        parent.appendChild(sep);
    }

    // ============================================
    // 地圖容器
    // ============================================

    _createMapContainer() {
        const strings = Locale.getComponentStrings('osmMapEditor');

        const container = document.createElement('div');
        container.style.cssText = `
            display: none;
            position: fixed;
            top: 0; left: 0;
            width: 100%; height: 100%;
            background: var(--cl-bg-overlay-heavy);
            z-index: 10000;
            padding: 20px;
        `;

        // Header
        const header = document.createElement('div');
        header.style.cssText = `
            background: var(--cl-primary-dark);
            color: var(--cl-text-inverse);
            padding: 15px 20px;
            border-radius: var(--cl-radius-lg) var(--cl-radius-lg) 0 0;
            display: flex;
            justify-content: space-between;
            align-items: center;
        `;

        const title = document.createElement('h3');
        title.textContent = `🗺️ ${strings.mapTitle || 'OpenStreetMap'}`;
        title.style.margin = '0';

        const closeBtn = document.createElement('button');
        closeBtn.textContent = `✕ ${strings.close || 'Close'}`;
        closeBtn.style.cssText = `
            background: var(--cl-danger);
            color: var(--cl-text-inverse);
            border: none;
            padding: 8px 16px;
            border-radius: var(--cl-radius-sm);
            cursor: pointer;
            font-size: var(--cl-font-size-lg);
        `;
        closeBtn.onclick = () => this._toggleMap();

        header.appendChild(title);
        header.appendChild(closeBtn);

        // Map wrapper
        const mapWrapper = document.createElement('div');
        mapWrapper.style.cssText = `
            background: var(--cl-bg);
            border-radius: 0 0 var(--cl-radius-lg) var(--cl-radius-lg);
            height: calc(100% - 60px);
            position: relative;
            overflow: hidden;
        `;

        // Map div
        const mapDiv = document.createElement('div');
        mapDiv.style.cssText = 'width: 100%; height: 100%;';

        // 截取框
        const captureFrame = document.createElement('div');
        captureFrame.style.cssText = `
            position: absolute;
            top: 50%; left: 50%;
            transform: translate(-50%, -50%);
            width: ${this.width}px;
            height: ${this.height}px;
            border: 4px solid var(--cl-primary);
            background: var(--cl-primary-soft-subtle);
            pointer-events: none;
            z-index: 999;
            box-shadow: 0 0 0 9999px var(--cl-bg-overlay-soft-hover);
        `;

        const frameLabel = document.createElement('div');
        frameLabel.textContent = `📸 ${strings.captureHint || 'Capture Area'} ${this.width}×${this.height}px`;
        frameLabel.style.cssText = `
            position: absolute;
            top: -35px; left: 50%;
            transform: translateX(-50%);
            background: var(--cl-primary);
            color: var(--cl-text-inverse);
            padding: 8px 16px;
            border-radius: var(--cl-radius-md);
            font-size: var(--cl-font-size-xl);
            font-weight: bold;
            white-space: nowrap;
            box-shadow: var(--cl-shadow-md);
        `;
        captureFrame.appendChild(frameLabel);
        this._captureFrame = captureFrame;

        // 控制面板
        const controls = document.createElement('div');
        controls.style.cssText = `
            position: absolute;
            top: 20px; right: 20px;
            background: var(--cl-bg);
            padding: 18px;
            border-radius: var(--cl-radius-lg);
            box-shadow: var(--cl-shadow-lg);
            z-index: 1000;
            min-width: 220px;
        `;

        // 截圖按鈕
        const captureButton = new EditorButton({
            type: 'custom',
            label: strings.captureMap || 'Capture Map',
            theme: 'gradient',
            variant: 'primary',
            onClick: () => this._captureMap()
        });
        captureButton.element.style.cssText = 'margin-bottom: 12px; width: 100%;';
        captureButton.mount(controls);

        // 圖磚源切換
        const layerSelector = document.createElement('select');
        layerSelector.style.cssText = `
            width: 100%;
            padding: 10px;
            margin-bottom: 12px;
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-sm);
            font-size: var(--cl-font-size-lg);
        `;

        const allLayers = { ...OSMMapEditor.TILE_LAYERS, ...this.customTileLayers };
        Object.entries(allLayers).forEach(([key, layer]) => {
            const option = document.createElement('option');
            option.value = key;
            option.textContent = layer.name;
            option.selected = key === this.tileLayerKey;
            layerSelector.appendChild(option);
        });

        layerSelector.onchange = (e) => this._switchTileLayer(e.target.value);
        controls.appendChild(layerSelector);

        const hint = document.createElement('div');
        hint.innerHTML = `
            <div style="background: var(--cl-bg-secondary); padding: 12px; border-radius: var(--cl-radius-md); font-size: var(--cl-font-size-md); color: var(--cl-text-secondary); line-height: 1.8;">
                <div>🗺️ ${strings.hintDrag || 'Drag to adjust position'}</div>
                <div>🔍 ${strings.hintZoom || 'Scroll to zoom'}</div>
                <div>📸 ${strings.hintCapture || 'Capture frame content'}</div>
                <div>📏 ${strings.hintMeasure || 'Use toolbar to measure'}</div>
            </div>
        `;
        controls.appendChild(hint);

        mapWrapper.appendChild(mapDiv);
        mapWrapper.appendChild(captureFrame);
        mapWrapper.appendChild(controls);

        // 座標面板
        if (this.showCoords) {
            this._createCoordPanel(mapWrapper);
        }

        // 比例尺
        if (this.showScale) {
            this._createScaleControl(mapWrapper);
        }

        // 指北針
        if (this.showCompass) {
            this._createCompassControl(mapWrapper);
        }

        container.appendChild(header);
        container.appendChild(mapWrapper);

        document.body.appendChild(container);
        this.mapContainer = container;
        this._mapDiv = mapDiv;
        this._mapWrapper = mapWrapper;
    }

    // ============================================
    // 地圖操作
    // ============================================

    _toggleMap() {
        if (!this.mapContainer) return;

        this.showingMap = !this.showingMap;
        this.mapContainer.style.display = this.showingMap ? 'block' : 'none';

        if (this.showingMap && !this.leafletMap) {
            this._initMap();
        } else if (this.showingMap && this.leafletMap && this.leafletMap.map) {
            setTimeout(() => this.leafletMap.map.invalidateSize(), 100);
        }
    }

    _initMap() {
        try {
            this.leafletMap = new LeafletMap({
                container: this._mapDiv,
                center: this.mapCenter,
                zoom: this.mapZoom,
                tileLayer: 'osm',
                onReady: () => {
                    // 設定自訂圖磚（覆寫 LeafletMap 預設的 NLSC）
                    this._switchTileLayer(this.tileLayerKey);
                    this._setupMapEvents();
                    this._updateScaleDisplay(this.mapZoom, this.mapCenter.lat);
                }
            });
        } catch (error) {
            console.error('OSMMapEditor: Map init failed', error);
            ModalPanel.alert({ message: Locale.t('osmMapEditor.mapInitError') + ': ' + error.message });
        }
    }

    _switchTileLayer(key) {
        if (!this.leafletMap || !this.leafletMap.map) return;

        const allLayers = { ...OSMMapEditor.TILE_LAYERS, ...this.customTileLayers };
        const layer = allLayers[key];
        if (!layer) return;

        this.tileLayerKey = key;

        // 移除現有圖層
        if (this.leafletMap.tileLayer) {
            this.leafletMap.map.removeLayer(this.leafletMap.tileLayer);
        }

        // 加入新圖層
        this.leafletMap.tileLayer = L.tileLayer(layer.url, {
            attribution: layer.attribution,
            maxZoom: layer.maxZoom || 19,
            crossOrigin: 'anonymous'
        });
        this.leafletMap.tileLayer.addTo(this.leafletMap.map);
    }

    _setupMapEvents() {
        if (!this.leafletMap || !this.leafletMap.map) return;

        const map = this.leafletMap.map;

        // 座標更新
        map.on('mousemove', (e) => {
            this._updateCoordDisplay(e.latlng.lat, e.latlng.lng);
        });

        // 比例尺更新
        map.on('zoomend', () => {
            const center = map.getCenter();
            this._updateScaleDisplay(map.getZoom(), center.lat);
        });

        // 測量點擊
        map.on('click', (e) => {
            if (!this.measureMode) return;
            this.measurePoints.push({ lat: e.latlng.lat, lng: e.latlng.lng });
            this._updateMeasureDisplay();
        });

        // 雙擊結束測量
        map.on('dblclick', (e) => {
            if (!this.measureMode) return;
            L.DomEvent.stopPropagation(e);
            L.DomEvent.preventDefault(e);

            if (this.measureMode === 'distance') {
                this._finishDistanceMeasure();
            } else if (this.measureMode === 'area') {
                this._finishAreaMeasure();
            }
        });
    }

    // ============================================
    // 地圖截圖
    // ============================================

    async _captureMap() {
        if (!this.leafletMap || !this._mapDiv || !this._captureFrame) {
            ModalPanel.alert({ message: Locale.t('osmMapEditor.mapNotReady') });
            return;
        }

        const strings = Locale.getComponentStrings('osmMapEditor');

        try {
            const frameRect = this._captureFrame.getBoundingClientRect();
            const mapRect = this._mapDiv.getBoundingClientRect();
            const x = frameRect.left - mapRect.left;
            const y = frameRect.top - mapRect.top;

            const fullCanvas = await this.leafletMap.captureToCanvas();

            // 測試 canvas 是否被汙染
            try {
                fullCanvas.getContext('2d').getImageData(0, 0, 1, 1);
            } catch (e) {
                throw new Error('Canvas tainted');
            }

            // 裁切
            const cropped = document.createElement('canvas');
            cropped.width = this.width;
            cropped.height = this.height;
            cropped.getContext('2d').drawImage(
                fullCanvas,
                x, y, this.width, this.height,
                0, 0, this.width, this.height
            );

            this.setBackgroundImage(cropped.toDataURL('image/png'));
            this._toggleMap();
            ModalPanel.alert({
                message: `${strings.captureSuccess || 'Map captured'} (${this.width}×${this.height}px)`
            });

        } catch (error) {
            console.warn('OSMMapEditor: native capture failed, trying html2canvas', error);

            try {
                if (typeof html2canvas === 'undefined') {
                    await this._loadHtml2Canvas();
                }

                const canvas = await html2canvas(this._mapDiv, {
                    useCORS: true,
                    allowTaint: false,
                    logging: false,
                    backgroundColor: 'var(--cl-bg)'
                });

                const frameRect = this._captureFrame.getBoundingClientRect();
                const mapRect = this._mapDiv.getBoundingClientRect();
                const x = frameRect.left - mapRect.left;
                const y = frameRect.top - mapRect.top;

                const cropped = document.createElement('canvas');
                cropped.width = this.width;
                cropped.height = this.height;
                cropped.getContext('2d').drawImage(
                    canvas,
                    x, y, this.width, this.height,
                    0, 0, this.width, this.height
                );

                this.setBackgroundImage(cropped.toDataURL('image/png'));
                this._toggleMap();
                ModalPanel.alert({
                    message: `${strings.captureSuccess || 'Map captured'} (${this.width}×${this.height}px)`
                });

            } catch (fallbackError) {
                console.error('OSMMapEditor: html2canvas failed', fallbackError);
                ModalPanel.alert({ message: strings.captureFailed || 'Capture failed' });
            }
        }
    }

    _loadHtml2Canvas() {
        return new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = 'https://cdnjs.cloudflare.com/ajax/libs/html2canvas/1.4.1/html2canvas.min.js';
            script.onload = resolve;
            script.onerror = () => reject(new Error('Failed to load html2canvas'));
            document.head.appendChild(script);
        });
    }

    // ============================================
    // 座標面板
    // ============================================

    _createCoordPanel(mapWrapper) {
        const strings = Locale.getComponentStrings('osmMapEditor');

        const panel = document.createElement('div');
        panel.style.cssText = `
            position: absolute;
            bottom: 10px; left: 10px;
            background: var(--cl-bg-surface-overlay);
            padding: 10px 15px;
            border-radius: var(--cl-radius-md);
            box-shadow: var(--cl-shadow-md);
            z-index: 1000;
            font-family: var(--cl-font-family-mono);
            font-size: var(--cl-font-size-md);
            min-width: 280px;
        `;

        const coordDisplay = document.createElement('div');
        coordDisplay.style.cssText = 'margin-bottom: 8px; line-height: 1.6;';
        coordDisplay.innerHTML = `
            <div style="color: var(--cl-text-secondary); margin-bottom: 4px;">
                <span style="display: inline-block; width: 50px;">${strings.latitude || 'Lat'}:</span>
                <span class="coord-lat" style="color: var(--cl-text); font-weight: bold;">--</span>
            </div>
            <div style="color: var(--cl-text-secondary);">
                <span style="display: inline-block; width: 50px;">${strings.longitude || 'Lng'}:</span>
                <span class="coord-lng" style="color: var(--cl-text); font-weight: bold;">--</span>
            </div>
        `;
        panel.appendChild(coordDisplay);

        // DD/DMS 切換
        const formatToggle = document.createElement('div');
        formatToggle.style.cssText = 'display: flex; gap: 5px; border-top: 1px solid var(--cl-border-light); padding-top: 8px;';

        const ddBtn = document.createElement('button');
        ddBtn.textContent = strings.coordDD || 'DD';
        ddBtn.style.cssText = `
            flex: 1; padding: 4px 8px;
            border: 1px solid var(--cl-primary);
            background: var(--cl-primary);
            color: var(--cl-text-inverse);
            border-radius: var(--cl-radius-sm); cursor: pointer; font-size: var(--cl-font-size-sm);
        `;

        const dmsBtn = document.createElement('button');
        dmsBtn.textContent = strings.coordDMS || 'DMS';
        dmsBtn.style.cssText = `
            flex: 1; padding: 4px 8px;
            border: 1px solid var(--cl-primary);
            background: var(--cl-bg);
            color: var(--cl-primary);
            border-radius: var(--cl-radius-sm); cursor: pointer; font-size: var(--cl-font-size-sm);
        `;

        ddBtn.onclick = () => {
            this.coordFormat = 'DD';
            ddBtn.style.background = 'var(--cl-primary)';
            ddBtn.style.color = 'var(--cl-text-inverse)';
            dmsBtn.style.background = 'var(--cl-bg)';
            dmsBtn.style.color = 'var(--cl-primary)';
        };
        dmsBtn.onclick = () => {
            this.coordFormat = 'DMS';
            dmsBtn.style.background = 'var(--cl-primary)';
            dmsBtn.style.color = 'var(--cl-text-inverse)';
            ddBtn.style.background = 'var(--cl-bg)';
            ddBtn.style.color = 'var(--cl-primary)';
        };

        formatToggle.appendChild(ddBtn);
        formatToggle.appendChild(dmsBtn);
        panel.appendChild(formatToggle);

        mapWrapper.appendChild(panel);
        this.coordPanel = panel;
        this._coordLatEl = panel.querySelector('.coord-lat');
        this._coordLngEl = panel.querySelector('.coord-lng');
    }

    _updateCoordDisplay(lat, lng) {
        if (!this._coordLatEl || !this._coordLngEl) return;

        if (this.coordFormat === 'DD') {
            this._coordLatEl.textContent = lat.toFixed(6);
            this._coordLngEl.textContent = lng.toFixed(6);
        } else {
            this._coordLatEl.textContent = this._toDMS(lat, 'lat');
            this._coordLngEl.textContent = this._toDMS(lng, 'lng');
        }
    }

    _toDMS(decimal, type) {
        const absolute = Math.abs(decimal);
        const degrees = Math.floor(absolute);
        const minutesFloat = (absolute - degrees) * 60;
        const minutes = Math.floor(minutesFloat);
        const seconds = ((minutesFloat - minutes) * 60).toFixed(1);

        let direction;
        if (type === 'lat') {
            direction = decimal >= 0 ? 'N' : 'S';
        } else {
            direction = decimal >= 0 ? 'E' : 'W';
        }

        return `${degrees}°${minutes.toString().padStart(2, '0')}'${seconds.padStart(4, '0')}"${direction}`;
    }

    // ============================================
    // 比例尺
    // ============================================

    _createScaleControl(mapWrapper) {
        const scale = document.createElement('div');
        scale.style.cssText = `
            position: absolute;
            bottom: 10px; right: 10px;
            background: var(--cl-bg-surface-overlay);
            padding: 5px 10px;
            border-radius: var(--cl-radius-sm);
            box-shadow: var(--cl-shadow-sm);
            z-index: 1000;
            font-size: var(--cl-font-size-xs);
            display: flex;
            align-items: center;
            gap: 8px;
        `;

        const scaleLine = document.createElement('div');
        scaleLine.style.cssText = `
            width: 100px; height: 8px;
            border: 2px solid var(--cl-text);
            border-top: none;
            position: relative;
        `;

        ['left', 'right'].forEach(side => {
            const cap = document.createElement('div');
            cap.style.cssText = `
                position: absolute;
                ${side}: 0; top: -2px;
                width: 2px; height: 8px;
                background: var(--cl-text);
            `;
            scaleLine.appendChild(cap);
        });

        const scaleText = document.createElement('span');
        scaleText.textContent = '1 km';
        scaleText.style.fontWeight = 'bold';

        scale.appendChild(scaleLine);
        scale.appendChild(scaleText);

        mapWrapper.appendChild(scale);
        this.scaleControl = scale;
        this._scaleTextEl = scaleText;
        this._scaleLineEl = scaleLine;
    }

    _updateScaleDisplay(zoom, lat = 25) {
        if (!this._scaleTextEl || !this._scaleLineEl) return;

        const strings = Locale.getComponentStrings('osmMapEditor');

        // 每像素公尺數 = 地球周長 × cos(緯度) / 2^(zoom+8)
        const metersPerPixel = (40075016.686 * Math.cos(lat * Math.PI / 180)) / Math.pow(2, zoom + 8);

        let distance = metersPerPixel * 100;
        let unit = strings.meters || 'm';
        let displayDistance = distance;

        if (distance >= 1000) {
            displayDistance = distance / 1000;
            unit = strings.kilometers || 'km';
        }

        let rounded;
        if (displayDistance >= 100) rounded = Math.round(displayDistance / 100) * 100;
        else if (displayDistance >= 10) rounded = Math.round(displayDistance / 10) * 10;
        else if (displayDistance >= 1) rounded = Math.round(displayDistance);
        else rounded = Math.round(displayDistance * 10) / 10;

        const scaleFactor = rounded / displayDistance;
        const scaleWidth = 100 * scaleFactor;
        this._scaleLineEl.style.width = `${Math.min(150, Math.max(50, scaleWidth))}px`;
        this._scaleTextEl.textContent = `${rounded} ${unit}`;
    }

    // ============================================
    // 指北針
    // ============================================

    _createCompassControl(mapWrapper) {
        const strings = Locale.getComponentStrings('osmMapEditor');

        const compass = document.createElement('div');
        compass.style.cssText = `
            position: absolute;
            top: 10px; right: 10px;
            width: 50px; height: 50px;
            background: var(--cl-bg-surface-overlay);
            border-radius: var(--cl-radius-round);
            box-shadow: var(--cl-shadow-md);
            z-index: 1000;
            display: flex;
            align-items: center;
            justify-content: center;
            cursor: pointer;
        `;

        compass.innerHTML = `
            <svg width="40" height="40" viewBox="0 0 40 40">
                <circle cx="20" cy="20" r="18" fill="none" stroke="var(--cl-border)" stroke-width="1"/>
                <polygon points="20,4 24,20 20,17 16,20" fill="var(--cl-danger)" stroke="var(--cl-danger-dark)" stroke-width="0.5"/>
                <polygon points="20,36 24,20 20,23 16,20" fill="var(--cl-grey-dark)" stroke="var(--cl-text)" stroke-width="0.5"/>
                <text x="20" y="8" text-anchor="middle" font-size="6" font-weight="bold" fill="var(--cl-text)">N</text>
            </svg>
        `;

        compass.title = strings.compass || 'Compass - Click to reset';
        compass.onclick = () => {
            if (this.leafletMap && this.leafletMap.map && this.leafletMap.map.setBearing) {
                this.leafletMap.map.setBearing(0);
            }
        };

        mapWrapper.appendChild(compass);
        this.compassControl = compass;
    }

    // ============================================
    // 距離測量
    // ============================================

    _toggleMeasureMode(mode) {
        if (this.measureMode === mode) {
            this._exitMeasureMode();
        } else {
            this._exitMeasureMode();
            this.measureMode = mode;
            this.measurePoints = [];
            this._updateMeasureButtonStyles();

            if (this.leafletMap && this.leafletMap.map) {
                this.leafletMap.map.getContainer().style.cursor = 'crosshair';
            }

            const strings = Locale.getComponentStrings('osmMapEditor');
            const hint = mode === 'distance'
                ? (strings.distanceHint || 'Click to add points, double-click to finish')
                : (strings.areaHint || 'Click to add vertices, double-click to close');
            this._showMeasureTooltip(hint);
        }
    }

    _exitMeasureMode() {
        this.measureMode = null;
        this.measurePoints = [];
        this._updateMeasureButtonStyles();
        this._clearMeasureLayer();
        this._hideMeasureTooltip();

        if (this.leafletMap && this.leafletMap.map) {
            this.leafletMap.map.getContainer().style.cursor = '';
        }
    }

    _updateMeasureButtonStyles() {
        if (this._distanceBtn) this._distanceBtn.active = (this.measureMode === 'distance');
        if (this._areaBtn) this._areaBtn.active = (this.measureMode === 'area');
    }

    _showMeasureTooltip(text) {
        if (!this._mapWrapper) return;

        if (!this.measureTooltip) {
            this.measureTooltip = document.createElement('div');
            this.measureTooltip.style.cssText = `
                position: absolute;
                top: 60px; left: 50%;
                transform: translateX(-50%);
                background: var(--cl-bg-overlay-strong);
                color: var(--cl-text-inverse);
                padding: 10px 20px;
                border-radius: var(--cl-radius-md);
                font-size: var(--cl-font-size-lg);
                z-index: 2000;
                white-space: nowrap;
            `;
            this._mapWrapper.appendChild(this.measureTooltip);
        }

        this.measureTooltip.textContent = text;
        this.measureTooltip.style.display = 'block';
    }

    _hideMeasureTooltip() {
        if (this.measureTooltip) {
            this.measureTooltip.style.display = 'none';
        }
    }

    _clearMeasureLayer() {
        if (this.measureLayer && this.leafletMap && this.leafletMap.map) {
            this.leafletMap.map.removeLayer(this.measureLayer);
            this.measureLayer = null;
        }
        if (this.measureMarkers) {
            this.measureMarkers.forEach(m => {
                if (this.leafletMap && this.leafletMap.map) {
                    this.leafletMap.map.removeLayer(m);
                }
            });
            this.measureMarkers = [];
        }
    }

    _updateMeasureDisplay() {
        if (!this.leafletMap || !this.leafletMap.map) return;

        const map = this.leafletMap.map;
        const strings = Locale.getComponentStrings('osmMapEditor');

        this._clearMeasureLayer();
        if (this.measurePoints.length === 0) return;

        // 繪製測量點
        this.measurePoints.forEach((point, index) => {
            const marker = L.circleMarker([point.lat, point.lng], {
                radius: 6,
                color: this.measureMode === 'distance' ? 'var(--cl-primary-dark)' : 'var(--cl-purple)',
                fillColor: 'var(--cl-bg)',
                fillOpacity: 1,
                weight: 2
            }).addTo(map);

            marker.bindTooltip(String(index + 1), {
                permanent: true,
                direction: 'center',
                className: 'measure-label'
            });

            this.measureMarkers.push(marker);
        });

        // 繪製線段/多邊形
        if (this.measurePoints.length > 1) {
            const latlngs = this.measurePoints.map(p => [p.lat, p.lng]);

            if (this.measureMode === 'distance') {
                this.measureLayer = L.polyline(latlngs, {
                    color: 'var(--cl-primary-dark)',
                    weight: 3,
                    dashArray: '10, 5'
                }).addTo(map);

                const distance = this._calculateTotalDistance(this.measurePoints);
                this._showMeasureTooltip(
                    `${strings.totalDistance || 'Total'}: ${this._formatDistance(distance)}`
                );
            } else if (this.measureMode === 'area') {
                const closed = [...latlngs, latlngs[0]];
                this.measureLayer = L.polygon(closed, {
                    color: 'var(--cl-purple)',
                    weight: 3,
                    fillColor: 'var(--cl-purple)',
                    fillOpacity: 0.2,
                    dashArray: '10, 5'
                }).addTo(map);

                if (this.measurePoints.length >= 3) {
                    const area = this._calculatePolygonArea(this.measurePoints);
                    this._showMeasureTooltip(
                        `${strings.area || 'Area'}: ${this._formatArea(area)}`
                    );
                } else {
                    this._showMeasureTooltip(strings.needMorePoints || 'Need at least 3 points');
                }
            }
        }
    }

    _finishDistanceMeasure() {
        const strings = Locale.getComponentStrings('osmMapEditor');

        if (this.measurePoints.length < 2) {
            ModalPanel.alert({ message: strings.needTwoPoints || 'Need at least 2 points' });
            return;
        }

        const distance = this._calculateTotalDistance(this.measurePoints);
        ModalPanel.alert({
            message: `📏 ${strings.distanceResult || 'Distance'}\n\n${strings.totalDistance || 'Total'}: ${this._formatDistance(distance)}\n${strings.pointCount || 'Points'}: ${this.measurePoints.length}`
        });
        this._exitMeasureMode();
    }

    _finishAreaMeasure() {
        const strings = Locale.getComponentStrings('osmMapEditor');

        if (this.measurePoints.length < 3) {
            ModalPanel.alert({ message: strings.needThreePoints || 'Need at least 3 points' });
            return;
        }

        const area = this._calculatePolygonArea(this.measurePoints);
        ModalPanel.alert({
            message: `📐 ${strings.areaResult || 'Area'}\n\n${strings.area || 'Area'}: ${this._formatArea(area)}\n${strings.vertexCount || 'Vertices'}: ${this.measurePoints.length}`
        });
        this._exitMeasureMode();
    }

    // ============================================
    // 地理計算
    // ============================================

    /** Haversine 公式 — 兩點間距離 (公尺) */
    _haversineDistance(lat1, lng1, lat2, lng2) {
        const R = 6371000;
        const toRad = (deg) => deg * Math.PI / 180;

        const dLat = toRad(lat2 - lat1);
        const dLng = toRad(lng2 - lng1);

        const a = Math.sin(dLat / 2) ** 2 +
                  Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) *
                  Math.sin(dLng / 2) ** 2;

        return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    }

    /** 折線總距離 */
    _calculateTotalDistance(points) {
        let total = 0;
        for (let i = 1; i < points.length; i++) {
            total += this._haversineDistance(
                points[i - 1].lat, points[i - 1].lng,
                points[i].lat, points[i].lng
            );
        }
        return total;
    }

    /** 球面 Shoelace — 多邊形面積 (平方公尺) */
    _calculatePolygonArea(points) {
        if (points.length < 3) return 0;

        const toRad = (deg) => deg * Math.PI / 180;
        const R = 6371000;
        let total = 0;
        const n = points.length;

        for (let i = 0; i < n; i++) {
            const j = (i + 1) % n;
            const xi = toRad(points[i].lng);
            const yi = toRad(points[i].lat);
            const xj = toRad(points[j].lng);
            const yj = toRad(points[j].lat);
            total += (xj - xi) * (2 + Math.sin(yi) + Math.sin(yj));
        }

        return Math.abs(total * R * R / 2);
    }

    /** 格式化距離 */
    _formatDistance(meters) {
        const strings = Locale.getComponentStrings('osmMapEditor');
        if (meters >= 1000) {
            return `${(meters / 1000).toFixed(2)} ${strings.kilometers || 'km'}`;
        }
        return `${meters.toFixed(1)} ${strings.meters || 'm'}`;
    }

    /** 格式化面積 */
    _formatArea(sqMeters) {
        const strings = Locale.getComponentStrings('osmMapEditor');
        if (sqMeters >= 1000000) {
            return `${(sqMeters / 1000000).toFixed(4)} ${strings.squareKilometers || 'km²'}`;
        } else if (sqMeters >= 10000) {
            return `${(sqMeters / 10000).toFixed(4)} ${strings.hectares || 'ha'}`;
        }
        return `${sqMeters.toFixed(2)} ${strings.squareMeters || 'm²'}`;
    }

    // ============================================
    // GeoJSON 匯入/匯出
    // ============================================

    _exportGeoJSON() {
        if (!this.leafletMap || !this.leafletMap.map) {
            ModalPanel.alert({ message: Locale.t('osmMapEditor.openMapFirst') });
            return;
        }

        const features = [];
        const map = this.leafletMap.map;

        // Leaflet 圖層
        map.eachLayer((layer) => {
            if (layer.toGeoJSON && !(layer instanceof L.TileLayer)) {
                try {
                    const geojson = layer.toGeoJSON();
                    if (geojson.geometry) {
                        geojson.properties = geojson.properties || {};
                        if (layer.options) {
                            geojson.properties.style = {
                                color: layer.options.color,
                                weight: layer.options.weight,
                                opacity: layer.options.opacity,
                                fillColor: layer.options.fillColor,
                                fillOpacity: layer.options.fillOpacity
                            };
                        }
                        features.push(geojson);
                    }
                } catch (e) { /* 忽略 */ }
            }
        });

        // 編輯器元素
        this.elements.forEach(el => {
            const feature = this._elementToGeoJSON(el);
            if (feature) features.push(feature);
        });

        const data = {
            type: 'FeatureCollection',
            features,
            metadata: {
                exportTime: new Date().toISOString(),
                source: 'OSMMapEditor',
                version: '1.0'
            }
        };

        const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/geo+json' });
        const link = document.createElement('a');
        link.download = `map-data-${Date.now()}.geojson`;
        link.href = URL.createObjectURL(blob);
        link.click();
        URL.revokeObjectURL(link.href);

        const strings = Locale.getComponentStrings('osmMapEditor');
        ModalPanel.alert({
            message: Locale.t('osmMapEditor.exportSuccess', { count: features.length })
                || `Exported ${features.length} features`
        });
    }

    _elementToGeoJSON(el) {
        if (!el) return null;

        let geometry = null;
        const properties = {
            type: el.type,
            color: el.color || el.strokeColor,
            fillColor: el.fillColor,
            lineWidth: el.lineWidth
        };

        switch (el.type) {
            case 'marker':
                geometry = { type: 'Point', coordinates: [el.lng || el.x, el.lat || el.y] };
                properties.text = el.text;
                break;
            case 'line':
            case 'arrow':
                geometry = {
                    type: 'LineString',
                    coordinates: [
                        [el.lng || el.x, el.lat || el.y],
                        [el.lng2 || el.x2, el.lat2 || el.y2]
                    ]
                };
                break;
            case 'path':
                if (el.points && el.points.length > 1) {
                    geometry = {
                        type: 'LineString',
                        coordinates: el.points.map(p => [p.lng || p.x, p.lat || p.y])
                    };
                }
                break;
            case 'rect': {
                const x1 = Math.min(el.x, el.x2), x2 = Math.max(el.x, el.x2);
                const y1 = Math.min(el.y, el.y2), y2 = Math.max(el.y, el.y2);
                geometry = {
                    type: 'Polygon',
                    coordinates: [[[x1, y1], [x2, y1], [x2, y2], [x1, y2], [x1, y1]]]
                };
                break;
            }
            case 'circle': {
                const cx = (el.x + el.x2) / 2, cy = (el.y + el.y2) / 2;
                const rx = Math.abs(el.x2 - el.x) / 2, ry = Math.abs(el.y2 - el.y) / 2;
                const pts = [];
                for (let i = 0; i <= 36; i++) {
                    const angle = (i * 10) * Math.PI / 180;
                    pts.push([cx + rx * Math.cos(angle), cy + ry * Math.sin(angle)]);
                }
                geometry = { type: 'Polygon', coordinates: [pts] };
                break;
            }
            default:
                return null;
        }

        return geometry ? { type: 'Feature', geometry, properties } : null;
    }

    async _importGeoJSON(file) {
        if (!file) return;

        if (!this.leafletMap || !this.leafletMap.map) {
            ModalPanel.alert({ message: Locale.t('osmMapEditor.openMapFirst') });
            return;
        }

        try {
            const text = await file.text();
            const geojsonData = JSON.parse(text);

            if (!geojsonData.type || (geojsonData.type !== 'FeatureCollection' && geojsonData.type !== 'Feature')) {
                throw new Error('Invalid GeoJSON');
            }

            const map = this.leafletMap.map;
            const features = geojsonData.type === 'FeatureCollection'
                ? geojsonData.features
                : [geojsonData];

            let importCount = 0;
            const bounds = L.latLngBounds();

            features.forEach(feature => {
                try {
                    const style = feature.properties?.style || {};
                    const layer = L.geoJSON(feature, {
                        style: {
                            color: style.color || 'var(--cl-primary-dark)',
                            weight: style.weight || 3,
                            opacity: style.opacity || 1,
                            fillColor: style.fillColor || 'var(--cl-primary-dark)',
                            fillOpacity: style.fillOpacity || 0.2
                        },
                        pointToLayer: (feat, latlng) => {
                            bounds.extend(latlng);
                            return L.circleMarker(latlng, {
                                radius: 8,
                                fillColor: style.color || 'var(--cl-primary-dark)',
                                color: 'var(--cl-bg)',
                                weight: 2,
                                fillOpacity: 0.8
                            });
                        },
                        onEachFeature: (feat, lyr) => {
                            if (feat.properties?.text) lyr.bindPopup(feat.properties.text);
                            if (lyr.getBounds) bounds.extend(lyr.getBounds());
                            else if (lyr.getLatLng) bounds.extend(lyr.getLatLng());
                        }
                    });

                    layer.addTo(map);
                    importCount++;
                } catch (e) {
                    console.warn('OSMMapEditor: failed to import feature', e);
                }
            });

            if (bounds.isValid()) {
                map.fitBounds(bounds, { padding: [50, 50] });
            }

            ModalPanel.alert({
                message: Locale.t('osmMapEditor.importSuccess', { count: importCount })
                    || `Imported ${importCount} features`
            });

        } catch (error) {
            console.error('OSMMapEditor: GeoJSON import failed', error);
            ModalPanel.alert({ message: (Locale.t('osmMapEditor.importFailed') || 'Import failed') + ': ' + error.message });
        }
    }

    // ============================================
    // 公開 API
    // ============================================

    /** 設定地圖中心點 */
    setCenter(lat, lng) {
        this.mapCenter = { lat, lng };
        if (this.leafletMap) this.leafletMap.setCenter(lat, lng);
    }

    /** 設定地圖縮放層級 */
    setZoom(zoom) {
        this.mapZoom = zoom;
        if (this.leafletMap) this.leafletMap.setZoom(zoom);
    }

    /** 取得底層 Leaflet map 實例 */
    getMap() {
        return this.leafletMap?.map || null;
    }
}

export default OSMMapEditor;
