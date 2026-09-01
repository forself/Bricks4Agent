// jsdom 環境初始化
if (!document.documentElement.getAttribute('data-theme')) {
    document.documentElement.setAttribute('data-theme', 'light');
}

// jsdom intentionally omits canvas drawing. These tests assert DOM/state
// contracts, so provide a deterministic 2D surface and keep asynchronous chart
// paints inside the test process instead of leaking unhandled exceptions.
class TestPath2D {
    moveTo() {}
    lineTo() {}
    closePath() {}
    rect() {}
    arc() {}
    quadraticCurveTo() {}
    bezierCurveTo() {}
}

const gradient = () => ({ addColorStop() {} });
const context2d = () => ({
    setTransform() {}, clearRect() {}, save() {}, restore() {}, beginPath() {}, closePath() {},
    moveTo() {}, lineTo() {}, rect() {}, arc() {}, quadraticCurveTo() {}, bezierCurveTo() {},
    translate() {}, scale() {}, rotate() {}, fill() {}, stroke() {}, clip() {},
    fillRect() {}, strokeRect() {}, drawImage() {}, fillText() {}, strokeText() {},
    setLineDash() {}, isPointInPath: () => false, isPointInStroke: () => false,
    measureText: text => ({ width: String(text ?? '').length * 7 }),
    createLinearGradient: gradient, createRadialGradient: gradient,
    createPattern: () => ({ setTransform() {} }),
    getImageData: (_x, _y, width = 1, height = 1) => ({
        data: new Uint8ClampedArray(Math.max(1, width * height * 4)), width, height,
    }),
    putImageData() {},
});

Object.defineProperty(globalThis, 'Path2D', { configurable: true, value: TestPath2D });
Object.defineProperty(HTMLCanvasElement.prototype, 'getContext', {
    configurable: true,
    value(type) {
        if (type !== '2d') return null;
        if (!this.__testContext2d) this.__testContext2d = context2d();
        return this.__testContext2d;
    },
});
Object.defineProperty(HTMLCanvasElement.prototype, 'toDataURL', {
    configurable: true,
    value: () => 'data:image/png;base64,',
});

if (typeof globalThis.ResizeObserver === 'undefined') {
    globalThis.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
}
if (typeof globalThis.IntersectionObserver === 'undefined') {
    globalThis.IntersectionObserver = class { observe() {} unobserve() {} disconnect() {} };
}
