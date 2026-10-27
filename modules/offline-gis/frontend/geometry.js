export const validCoordinate = (lon, lat) => Number.isFinite(lon) && Number.isFinite(lat)
    && lon >= -180 && lon <= 180 && lat >= -85.05112878 && lat <= 85.05112878;
export function project(lon, lat, zoom) {
    const scale = 256 * 2 ** zoom, sine = Math.sin(Math.max(-85.05112878, Math.min(85.05112878, lat)) * Math.PI / 180);
    return [(lon + 180) / 360 * scale, (0.5 - Math.log((1 + sine) / (1 - sine)) / (4 * Math.PI)) * scale];
}
export function unproject(x, y, zoom) {
    const scale = 256 * 2 ** zoom;
    return [x / scale * 360 - 180, Math.atan(Math.sinh(Math.PI * (1 - 2 * y / scale))) * 180 / Math.PI];
}
export function distance(a, b) {
    const r = Math.PI / 180;
    const h = Math.sin((b[1] - a[1]) * r / 2) ** 2 + Math.cos(a[1] * r) * Math.cos(b[1] * r) * Math.sin((b[0] - a[0]) * r / 2) ** 2;
    return 6371008.8 * 2 * Math.asin(Math.sqrt(Math.min(1, h)));
}
export function polygonContains(p, ring) {
    let inside = false;
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
        const a = ring[j], b = ring[i];
        const cross = (p[0] - a[0]) * (b[1] - a[1]) - (p[1] - a[1]) * (b[0] - a[0]);
        if (Math.abs(cross) < 1e-10 && p[0] >= Math.min(a[0], b[0]) - 1e-10 && p[0] <= Math.max(a[0], b[0]) + 1e-10
            && p[1] >= Math.min(a[1], b[1]) - 1e-10 && p[1] <= Math.max(a[1], b[1]) + 1e-10) return true;
        if ((a[1] > p[1]) !== (b[1] > p[1]) && p[0] < (b[0] - a[0]) * (p[1] - a[1]) / (b[1] - a[1]) + a[0]) inside = !inside;
    }
    return inside;
}
export const bounds = points => [Math.min(...points.map(p => p[0])), Math.min(...points.map(p => p[1])),
    Math.max(...points.map(p => p[0])), Math.max(...points.map(p => p[1]))];
export function densityCells(points, zoom, size = 48) {
    const cells = new Map();
    for (const p of points) {
        const [x, y] = project(p.lon, p.lat, zoom), key = `${Math.floor(x / size)},${Math.floor(y / size)}`;
        const cell = cells.get(key) || { x: Math.floor(x / size) * size, y: Math.floor(y / size) * size, count: 0, weight: 0 };
        cell.count++; cell.weight += p.weight ?? 1; cells.set(key, cell);
    }
    return [...cells.values()];
}
