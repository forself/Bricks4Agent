function line(context, ...points) {
    context.beginPath();
    points.forEach(([x, y], index) => index === 0 ? context.moveTo(x, y) : context.lineTo(x, y));
    context.stroke();
}

function draw(canvas) {
    const context = canvas.getContext('2d');
    context.clearRect(0, 0, 24, 24);
    const computedColor = getComputedStyle(canvas).color;
    if (computedColor) context.strokeStyle = computedColor;
    context.lineWidth = 2;
    context.lineCap = 'round';
    context.lineJoin = 'round';
    const name = canvas.dataset.canvasIcon;
    if (name === 'close') {
        line(context, [6, 6], [18, 18]); line(context, [18, 6], [6, 18]);
    } else if (name === 'plus') {
        line(context, [12, 5], [12, 19]); line(context, [5, 12], [19, 12]);
    } else if (name === 'refresh') {
        context.beginPath(); context.arc(12, 12, 8, -0.5, Math.PI * 1.55); context.stroke();
        line(context, [18, 4], [20, 9], [15, 8]);
    } else if (name === 'folder') {
        context.beginPath(); context.rect(3, 7, 18, 13); context.stroke(); line(context, [3, 7], [9, 7], [11, 4], [21, 4], [21, 7]);
    } else if (name === 'file') {
        line(context, [6, 3], [15, 3], [20, 8], [20, 21], [6, 21], [6, 3]); line(context, [15, 3], [15, 8], [20, 8]);
    } else if (name === 'monitor') {
        context.strokeRect(2, 3, 20, 14); line(context, [8, 21], [16, 21]); line(context, [12, 17], [12, 21]);
    } else {
        line(context, [12, 3], [3, 8], [12, 13], [21, 8], [12, 3]);
        line(context, [3, 13], [12, 18], [21, 13]); line(context, [3, 18], [12, 23], [21, 18]);
    }
}

document.querySelectorAll('canvas[data-canvas-icon]').forEach(draw);
