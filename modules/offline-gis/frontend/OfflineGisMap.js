import { Control, element } from './controls.js';
import { project, unproject, validCoordinate, distance, bounds, densityCells } from './geometry.js';
import { t } from './locale.js';

export class OfflineGisMap extends Control {
    constructor(options = {}) {
        super(options);
        this.center = options.center ?? [121.56, 25.03]; this.zoom = options.zoom ?? 8;
        if (!Array.isArray(this.center) || this.center.length !== 2 || !validCoordinate(...this.center)
            || !Number.isInteger(this.zoom) || this.zoom < 0 || this.zoom > 18) throw new Error('Invalid map view');
        this.points = []; this.tiles = new Map(); this.pending = new Set(); this.mode = 'pan'; this.vertices = []; this.selection = null;
        this.abort = new AbortController(); this.density = false;
    }
    build() {
        this.element.classList.add('offline-gis-map');
        this.button('zoomIn', () => this.zoomTo(this.zoom + 1)); this.button('zoomOut', () => this.zoomTo(this.zoom - 1));
        this.canvas = element('canvas', undefined, this.element); this.canvas.width = 960; this.canvas.height = 540;
        this.canvas.tabIndex = 0; this.canvas.setAttribute('aria-label', t('map'));
        this.status = element('output', '', this.element); this.status.setAttribute('aria-live', 'polite');
        element('p', t('attribution'), this.element);
        if (this.options.fixture === true) element('strong', t('fixture'), this.element);
        this.listen(this.canvas, 'pointerdown', event => { this.start = [event.clientX, event.clientY]; this.canvas.setPointerCapture?.(event.pointerId); });
        this.listen(this.canvas, 'pointerup', event => {
            if (!this.start) return;
            const rect = this.canvas.getBoundingClientRect(), dx = (event.clientX - this.start[0]) * 960 / rect.width, dy = (event.clientY - this.start[1]) * 540 / rect.height;
            this.start = null;
            if (this.mode === 'pan' && Math.hypot(dx, dy) > 3) {
                const [x, y] = project(...this.center, this.zoom); this.center = unproject(x - dx, y - dy, this.zoom);
                this.center = [Math.max(-180, Math.min(180, this.center[0])), Math.max(-85, Math.min(85, this.center[1]))]; this.render(); return;
            }
            this.pick(this.coordinate(event));
        });
        this.listen(this.canvas, 'dblclick', () => { if (this.mode === 'polygon' && this.vertices.length >= 3) {
            this.selection = { bbox: bounds(this.vertices), polygon: [...this.vertices] }; this.vertices = []; this.options.onSelect?.(this.selection); this.render();
        } });
        this.listen(this.canvas, 'keydown', e => {
            if (e.key === '+' || e.key === '=') this.zoomTo(this.zoom + 1);
            if (e.key === '-') this.zoomTo(this.zoom - 1);
            const offset = { ArrowLeft: [-80, 0], ArrowRight: [80, 0], ArrowUp: [0, -80], ArrowDown: [0, 80] }[e.key];
            if (offset) { e.preventDefault(); const p = project(...this.center, this.zoom); this.setView(unproject(p[0] + offset[0], p[1] + offset[1], this.zoom)); }
        });
        this.render();
    }
    coordinate(event) {
        const rect = this.canvas.getBoundingClientRect(), center = project(...this.center, this.zoom);
        return unproject(center[0] + (event.clientX - rect.left) * 960 / rect.width - 480,
            center[1] + (event.clientY - rect.top) * 540 / rect.height - 270, this.zoom);
    }
    pick(p) {
        if (!validCoordinate(...p)) return;
        if (this.mode === 'marker') this.options.onChange?.({ lon: p[0], lat: p[1] });
        if (this.mode === 'polygon' && this.vertices.length < 256) this.vertices.push(p);
        if (this.mode === 'circle') {
            this.vertices.push(p);
            if (this.vertices.length === 2) {
                const a = this.vertices[0], radius = distance(a, p);
                if (radius > 0 && radius <= 2000000) {
                    // Broad world candidate bbox; backend performs exact spherical distance.
                    this.selection = { bbox: [-180, -85, 180, 85], circle: [...a, radius] }; this.options.onSelect?.(this.selection);
                }
                this.vertices = [];
            }
        }
        this.render();
    }
    setMode(mode) {
        if (mode === 'density') this.density = !this.density;
        else if (mode === 'clear') { this.selection = null; this.vertices = []; this.options.onSelect?.({ bbox: [-180, -85, 180, 85] }); }
        else if (['pan', 'marker', 'circle', 'polygon'].includes(mode)) { this.mode = mode; this.vertices = []; }
        this.render();
    }
    setView(center) { if (!validCoordinate(...center)) return; this.center = [...center]; this.render(); }
    zoomTo(zoom) { this.zoom = Math.max(0, Math.min(18, zoom)); this.render(); }
    setValue(points) { this.points = points.filter(p => validCoordinate(p.lon, p.lat)); this.render(); }
    getValue() { return this.points.map(p => ({ ...p })); }
    async loadTile(z, x, y, key) {
        if (!this.options.api || this.pending.has(key) || this.tiles.has(key)) return;
        this.pending.add(key);
        try {
            const blob = await this.options.api.tile(z, x, y, this.abort.signal), bitmap = await createImageBitmap(blob);
            if (!this.element) { bitmap.close(); return; }
            this.tiles.set(key, bitmap);
            if (this.tiles.size > 128) { const first = this.tiles.keys().next().value; this.tiles.get(first)?.close?.(); this.tiles.delete(first); }
            this.render();
        } catch (error) { if (error.name !== 'AbortError' && this.element) { this.tiles.set(key, null); this.status.textContent = t('missing'); } }
        finally { this.pending.delete(key); }
    }
    render() {
        if (!this.canvas || !this.element) return;
        const ctx = this.canvas.getContext('2d'); if (!ctx) return;
        const style = getComputedStyle(this.element), color = name => style.getPropertyValue(name).trim();
        const bg = color('--cl-bg') || color('--cl-bg-primary'), ink = color('--cl-text') || color('--cl-text-primary'), accent = color('--cl-primary');
        ctx.clearRect(0, 0, 960, 540); if (bg) { ctx.fillStyle = bg; ctx.fillRect(0, 0, 960, 540); }
        const center = project(...this.center, this.zoom), ox = center[0] - 480, oy = center[1] - 270;
        for (let x = Math.floor(ox / 256); x <= Math.floor((ox + 960) / 256); x++) for (let y = Math.floor(oy / 256); y <= Math.floor((oy + 540) / 256); y++) {
            if (x < 0 || y < 0 || x >= 2 ** this.zoom || y >= 2 ** this.zoom) continue;
            const key = `${this.zoom}/${x}/${y}`, tile = this.tiles.get(key);
            if (tile) ctx.drawImage(tile, x * 256 - ox, y * 256 - oy); else this.loadTile(this.zoom, x, y, key);
        }
        if (accent) { ctx.fillStyle = accent; ctx.strokeStyle = accent; }
        if (this.density) {
            const cells = densityCells(this.points, this.zoom), max = Math.max(1, ...cells.map(c => c.weight));
            for (const cell of cells) { ctx.globalAlpha = 0.1 + 0.7 * cell.weight / max; ctx.fillRect(cell.x - ox, cell.y - oy, 48, 48); }
            ctx.globalAlpha = 1;
        }
        for (const point of this.points) {
            const [x, y] = project(point.lon, point.lat, this.zoom); ctx.beginPath(); ctx.arc(x - ox, y - oy, 5, 0, Math.PI * 2); ctx.fill();
        }
        const ring = this.selection?.polygon || this.vertices;
        if (ring.length) { ctx.beginPath(); ring.forEach((p, i) => { const [x, y] = project(...p, this.zoom); i ? ctx.lineTo(x - ox, y - oy) : ctx.moveTo(x - ox, y - oy); }); if (this.selection?.polygon) ctx.closePath(); ctx.stroke(); }
        if (this.selection?.circle) {
            const [lon, lat, radius] = this.selection.circle, [x, y] = project(lon, lat, this.zoom);
            const pixels = radius / (40075016.686 * Math.cos(lat * Math.PI / 180) / (256 * 2 ** this.zoom));
            ctx.beginPath(); ctx.arc(x - ox, y - oy, pixels, 0, Math.PI * 2); ctx.stroke();
        }
        if (ink) ctx.fillStyle = ink;
        ctx.fillText(`${this.points.length} · z${this.zoom}`, 12, 20);
        this.canvas.dataset.pointCount = String(this.points.length);
    }
    destroy() { this.abort.abort(); for (const tile of this.tiles.values()) tile?.close?.(); this.tiles.clear(); this.pending.clear(); super.destroy(); this.canvas = null; }
}
