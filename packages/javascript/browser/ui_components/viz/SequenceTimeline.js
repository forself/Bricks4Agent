import { BasicButton } from '../common/BasicButton/index.js';
import { paintToken, resolveTokens } from '../utils/theme-bus.js';

/** B4A vertical event sequence visualization with raster export. */
export class SequenceTimeline {
    constructor(options = {}) {
        this.options = {
            title: '時序圖',
            events: [],
            emptyText: '目前無時序資料',
            fileName: format => `timeline.${format === 'jpeg' ? 'jpg' : 'png'}`,
            ...options,
        };
        this.events = Array.isArray(this.options.events) ? [...this.options.events] : [];
        this.buttons = [];
        this.element = document.createElement('section');
        this.element.className = 'tim-cp-case-timeline';
        this.element.dataset.b4aComposite = 'SequenceTimeline';
        this._build();
    }

    _build() {
        const toolbar = document.createElement('div');
        toolbar.className = 'tim-workflow-chart__toolbar';
        const title = document.createElement('h3');
        title.textContent = this.options.title;
        const actions = document.createElement('div');
        actions.className = 'tim-workflow-chart__actions';
        for (const format of ['jpeg', 'png']) {
            const button = new BasicButton({
                type: BasicButton.TYPES.CUSTOM,
                size: 'small',
                variant: 'secondary',
                showIcon: false,
                customLabel: format.toUpperCase(),
                onClick: () => this.download(format),
            });
            button.mount(actions);
            this.buttons.push(button);
        }
        toolbar.append(title, actions);
        this.host = document.createElement('div');
        this.host.className = 'tim-cp-case-timeline__track';
        this.element.append(toolbar, this.host);
        this.render();
    }

    mount(container) { container?.appendChild(this.element); return this; }

    setEvents(events) {
        this.events = Array.isArray(events) ? [...events] : [];
        this.render();
        return this;
    }

    render() {
        this.host.replaceChildren();
        if (!this.events.length) {
            const empty = document.createElement('p');
            empty.textContent = this.options.emptyText;
            this.host.appendChild(empty);
            return;
        }
        this.events.forEach((item, index) => {
            const event = document.createElement('article');
            event.className = 'tim-cp-case-timeline__event';
            const marker = document.createElement('span');
            marker.className = 'tim-cp-case-timeline__marker';
            marker.textContent = String(index + 1);
            const body = document.createElement('div');
            const title = document.createElement('h4');
            title.textContent = item.title || `事件${index + 1}`;
            body.appendChild(title);
            for (const value of [item.time, item.address, item.document]) {
                const line = document.createElement('p');
                line.textContent = String(value || '');
                body.appendChild(line);
            }
            event.append(marker, body);
            this.host.appendChild(event);
        });
    }

    download(format = 'png') {
        const width = 1400;
        const height = Math.max(320, this.events.length * 130 + 100);
        const canvas = document.createElement('canvas');
        canvas.width = width * 2;
        canvas.height = height * 2;
        const ctx = canvas.getContext('2d');
        const paints = resolveTokens([
            '--cl-bg',
            '--cl-text-heading',
            '--cl-primary',
            '--cl-text-inverse',
            '--cl-text',
            '--cl-text-secondary',
        ], this.element);
        ctx.scale(2, 2);
        ctx.fillStyle = paintToken(paints, '--cl-bg');
        ctx.fillRect(0, 0, width, height);
        ctx.fillStyle = paintToken(paints, '--cl-text-heading');
        ctx.font = 'bold 24px sans-serif';
        ctx.fillText(this.options.title, 36, 42);
        ctx.font = '16px sans-serif';
        this.events.forEach((item, index) => {
            const y = 90 + index * 125;
            ctx.strokeStyle = paintToken(paints, '--cl-primary');
            ctx.lineWidth = 3;
            if (index < this.events.length - 1) {
                ctx.beginPath(); ctx.moveTo(58, y + 20); ctx.lineTo(58, y + 125); ctx.stroke();
            }
            ctx.fillStyle = paintToken(paints, '--cl-primary'); ctx.beginPath(); ctx.arc(58, y, 18, 0, Math.PI * 2); ctx.fill();
            ctx.fillStyle = paintToken(paints, '--cl-text-inverse'); ctx.fillText(String(index + 1), 53, y + 6);
            ctx.fillStyle = paintToken(paints, '--cl-text'); ctx.fillText(item.title || `事件${index + 1}`, 94, y - 5);
            ctx.fillStyle = paintToken(paints, '--cl-text-secondary'); ctx.fillText(item.time || '', 94, y + 24); ctx.fillText(item.address || '', 94, y + 50);
        });
        const link = document.createElement('a');
        link.href = canvas.toDataURL(format === 'jpeg' ? 'image/jpeg' : 'image/png', 0.94);
        link.download = this.options.fileName(format);
        link.click();
    }

    destroy() {
        this.buttons.splice(0).forEach(button => button.destroy?.());
        this.element.remove();
    }
}

export default SequenceTimeline;
