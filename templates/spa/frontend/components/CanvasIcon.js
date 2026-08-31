const DEFAULT_SIZE = 20;

function setupContext(canvas) {
    const size = Number(canvas.dataset.canvasIconSize || canvas.getAttribute('width') || DEFAULT_SIZE);
    const ratio = Math.max(1, globalThis.devicePixelRatio || 1);
    canvas.width = Math.round(size * ratio);
    canvas.height = Math.round(size * ratio);
    canvas.style.width = `${size}px`;
    canvas.style.height = `${size}px`;
    canvas.style.display = 'inline-block';
    canvas.style.flexShrink = '0';
    const context = canvas.getContext('2d');
    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    context.clearRect(0, 0, size, size);
    const computedColor = getComputedStyle(canvas).color;
    if (computedColor) context.strokeStyle = computedColor;
    context.fillStyle = context.strokeStyle;
    context.lineWidth = Math.max(1.5, size / 12);
    context.lineCap = 'round';
    context.lineJoin = 'round';
    context.scale(size / 24, size / 24);
    return context;
}

function line(context, ...points) {
    context.beginPath();
    points.forEach(([x, y], index) => index === 0 ? context.moveTo(x, y) : context.lineTo(x, y));
    context.stroke();
}

export function drawCanvasIcon(canvas) {
    const context = setupContext(canvas);
    const name = canvas.dataset.canvasIcon || 'info';

    if (name === 'close' || name === 'error') {
        if (name === 'error') {
            context.beginPath(); context.arc(12, 12, 9, 0, Math.PI * 2); context.stroke();
        }
        line(context, [7, 7], [17, 17]); line(context, [17, 7], [7, 17]);
    } else if (name === 'chevron-left' || name === 'arrow-left') {
        line(context, [15, 5], [8, 12], [15, 19]);
        if (name === 'arrow-left') line(context, [8, 12], [21, 12]);
    } else if (name === 'chevron-right') {
        line(context, [9, 5], [16, 12], [9, 19]);
    } else if (name === 'plus' || name === 'zoom-in') {
        line(context, [12, 6], [12, 18]); line(context, [6, 12], [18, 12]);
        if (name === 'zoom-in') {
            context.beginPath(); context.arc(10, 10, 7, 0, Math.PI * 2); context.stroke();
            line(context, [15, 15], [21, 21]);
        }
    } else if (name === 'zoom-out') {
        context.beginPath(); context.arc(10, 10, 7, 0, Math.PI * 2); context.stroke();
        line(context, [6, 10], [14, 10]); line(context, [15, 15], [21, 21]);
    } else if (name === 'check') {
        line(context, [5, 12], [10, 17], [19, 7]);
    } else if (name === 'warning') {
        line(context, [12, 3], [22, 20], [2, 20], [12, 3]);
        line(context, [12, 8], [12, 14]);
        context.beginPath(); context.arc(12, 17, 0.8, 0, Math.PI * 2); context.fill();
    } else if (name === 'calendar') {
        context.strokeRect(3, 5, 18, 16); line(context, [3, 9], [21, 9]);
        line(context, [8, 3], [8, 7]); line(context, [16, 3], [16, 7]);
    } else if (name === 'refresh') {
        context.beginPath(); context.arc(12, 12, 8, -0.5, Math.PI * 1.55); context.stroke();
        line(context, [18, 4], [20, 9], [15, 8]);
    } else if (name === 'search') {
        context.beginPath(); context.arc(10, 10, 7, 0, Math.PI * 2); context.stroke();
        line(context, [15, 15], [21, 21]);
    } else if (name === 'edit') {
        line(context, [5, 19], [8, 14], [17, 5], [20, 8], [11, 17], [5, 19]);
    } else if (name === 'user') {
        context.beginPath(); context.arc(12, 8, 4, 0, Math.PI * 2); context.stroke();
        context.beginPath(); context.arc(12, 22, 8, Math.PI, Math.PI * 2); context.stroke();
    } else if (name === 'list') {
        for (const y of [6, 12, 18]) { line(context, [8, y], [21, y]); context.fillRect(3, y - 1, 2, 2); }
    } else if (name === 'settings') {
        context.beginPath(); context.arc(12, 12, 4, 0, Math.PI * 2); context.stroke();
        for (let angle = 0; angle < Math.PI * 2; angle += Math.PI / 4) {
            line(context, [12 + Math.cos(angle) * 7, 12 + Math.sin(angle) * 7], [12 + Math.cos(angle) * 10, 12 + Math.sin(angle) * 10]);
        }
    } else if (name === 'eye') {
        context.beginPath(); context.ellipse(12, 12, 10, 6, 0, 0, Math.PI * 2); context.stroke();
        context.beginPath(); context.arc(12, 12, 2.5, 0, Math.PI * 2); context.fill();
    } else if (name === 'trash') {
        context.strokeRect(6, 7, 12, 14); line(context, [4, 7], [20, 7]); line(context, [9, 4], [15, 4]);
    } else {
        context.beginPath(); context.arc(12, 12, 9, 0, Math.PI * 2); context.stroke();
        context.font = 'bold 14px sans-serif'; context.textAlign = 'center'; context.textBaseline = 'middle';
        context.fillText('i', 12, 12);
    }
    canvas.dataset.canvasIconReady = 'true';
    return canvas;
}

export function createCanvasIcon(name, size = DEFAULT_SIZE, label = '', color = '') {
    const canvas = document.createElement('canvas');
    canvas.dataset.canvasIcon = name;
    canvas.dataset.canvasIconSize = String(size);
    canvas.setAttribute('width', String(size));
    canvas.setAttribute('height', String(size));
    if (color) canvas.style.color = color;
    canvas.setAttribute('aria-hidden', label ? 'false' : 'true');
    if (label) canvas.setAttribute('aria-label', label);
    return drawCanvasIcon(canvas);
}

export function canvasIconMarkup(name, size = DEFAULT_SIZE, className = '') {
    return `<canvas data-canvas-icon="${name}" data-canvas-icon-size="${size}" width="${size}" height="${size}"${className ? ` class="${className}"` : ''} aria-hidden="true"></canvas>`;
}

export function hydrateCanvasIcons(root = document) {
    root.querySelectorAll('canvas[data-canvas-icon]').forEach((canvas) => drawCanvasIcon(canvas));
}
