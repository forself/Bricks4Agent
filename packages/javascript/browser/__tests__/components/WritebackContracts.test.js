import { describe, it, expect, afterEach, vi } from 'vitest';
import { ModalPanel } from '../../ui_components/layout/Panel/ModalPanel.js';
import { TgosMap } from '../../ui_components/data/TgosMap/TgosMap.js';

describe('ModalPanel destroy contract', () => {
    afterEach(() => {
        document.querySelectorAll('.modal-backdrop').forEach(node => node.remove());
        vi.restoreAllMocks();
    });

    it('removes the backdrop and clears the reference on destroy', () => {
        const modal = new ModalPanel({ title: 'Demo' });
        modal.mount();
        modal.open();
        const backdrop = modal.backdrop;
        expect(document.body.contains(backdrop)).toBe(true);

        modal.destroy();
        expect(modal.backdrop).toBeNull();
        expect(document.body.contains(backdrop)).toBe(false);
        expect(document.querySelector('.modal-backdrop')).toBeNull();
    });

    it('can be destroyed from its own onClose callback', () => {
        let modal;
        modal = new ModalPanel({ title: 'Demo', onClose: () => modal.destroy() });
        modal.mount();
        modal.open();
        modal.close();
        expect(modal.backdrop).toBeNull();
        expect(document.querySelector('.modal-backdrop')).toBeNull();
        expect(document.body.style.overflow).toBe('');
    });

    it('ignores open() and mount() after destroy with a single warning', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const modal = new ModalPanel({ title: 'Demo' });
        modal.mount();
        modal.destroy();
        modal.open();
        modal.mount();
        expect(warn).toHaveBeenCalledTimes(1);
        expect(document.querySelector('.modal-backdrop')).toBeNull();
    });
});

describe('TgosMap without a 2D canvas', () => {
    afterEach(() => {
        delete window.TGOS;
        vi.restoreAllMocks();
    });

    function installFakeTgos() {
        const markers = [];
        class TGPoint { constructor(x, y) { this.x = x; this.y = y; } }
        class TGSize { constructor(width, height) { this.width = width; this.height = height; } }
        class TGImage { constructor(url) { this.url = url; } }
        class TGEnvelope { constructor(left, top, right, bottom) { Object.assign(this, { left, top, right, bottom }); } }
        class TGOnlineMap {
            constructor(host, coord, options) { Object.assign(this, { host, coord, options }); }
            fitBounds(bounds) { this.bounds = bounds; }
            setCenter(center) { this.center = center; }
            setZoom(zoom) { this.zoom = zoom; }
        }
        class TGMarker {
            constructor(map, point, title, image) { Object.assign(this, { map, point, title, image }); markers.push(this); }
            setMap(map) { this.map = map; }
            getPosition() { return this.point; }
        }
        class TGInfoWindow {
            close() {}
            setOptions() {}
            setContent(content) { this.content = content; }
            open(map) { this.openedOn = map; }
        }
        window.TGOS = {
            TGCoordSys: { EPSG3857: 'EPSG3857', EPSG3826: 'EPSG3826' },
            TGPoint, TGSize, TGImage, TGEnvelope, TGOnlineMap, TGMarker, TGInfoWindow,
            TGLocatorStatus: { OK: 'OK' },
            TGEvent: { addListener(target, name, handler) { target.events ||= {}; target.events[name] = handler; } },
        };
        return { markers };
    }

    // B4A 測試環境的 ResizeObserver 是 no-op、jsdom 尺寸恆為 0；模擬已掛載且有尺寸的地圖容器。
    function makeVisible(map) {
        vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ width: 400, height: 300, top: 0, left: 0, right: 400, bottom: 300 });
        document.body.appendChild(map.element);
    }

    it('falls back to the default TGOS marker instead of failing to initialise', async () => {
        vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(null);
        const { markers } = installFakeTgos();
        const map = new TgosMap({
            data: [{ Name: 'Site A', Lon: 121.5, Lat: 25.1 }],
            pointBuilder: row => ({ x: row.Lon, y: row.Lat, valid: true }),
        });
        makeVisible(map);
        await map.ready;

        expect(map.element.dataset.ready).toBe('true');
        expect(markers).toHaveLength(1);
        expect(markers[0].image).toBeUndefined();
        map.destroy();
    });

    it('uses a painted marker image when a 2D canvas is available', async () => {
        const { markers } = installFakeTgos();
        const map = new TgosMap({
            data: [{ Name: 'Site B', Lon: 121.5, Lat: 25.1 }],
            pointBuilder: row => ({ x: row.Lon, y: row.Lat, valid: true }),
        });
        makeVisible(map);
        await map.ready;

        expect(markers).toHaveLength(1);
        expect(markers[0].image?.url).toMatch(/^data:image\/png/);
        map.destroy();
    });
});
