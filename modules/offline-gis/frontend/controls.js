import { t } from './locale.js';
export function element(tag, text, parent) {
    const el = document.createElement(tag);
    if (text !== undefined) el.textContent = text;
    parent?.append(el); return el;
}
export class Control {
    constructor(options = {}) { this.options = options; this.element = null; this.cleanup = []; }
    mount(container) {
        if (this.element) throw new Error('Already mounted');
        this.element = element('section'); this.element.className = 'offline-gis-control';
        (typeof container === 'string' ? document.querySelector(container) : container).append(this.element);
        this.build(); return this;
    }
    listen(el, type, callback) { el.addEventListener(type, callback); this.cleanup.push(() => el.removeEventListener(type, callback)); }
    button(key, callback) { const b = element('button', t(key), this.element); b.type = 'button'; this.listen(b, 'click', callback); return b; }
    destroy() { this.cleanup.splice(0).forEach(fn => fn()); this.element?.remove(); this.element = null; }
}
export class GisSearch extends Control {
    build() {
        this.input = element('input', undefined, this.element); this.input.setAttribute('aria-label', t('search')); this.input.maxLength = 120;
        this.button('search', () => this.options.onChange?.(this.input.value));
        this.results = element('ul', undefined, this.element); this.results.setAttribute('aria-live', 'polite');
    }
    setValue(candidates) {
        this.results.replaceChildren();
        if (!candidates.length) element('li', t('empty'), this.results);
        for (const item of candidates) {
            const li = element('li', undefined, this.results), b = element('button', `${item.address} (${item.level}; ${item.match})`, li);
            b.type = 'button'; b.addEventListener('click', () => this.options.onSelect?.(item));
        }
    }
}
export class GisScope extends Control {
    build() {
        for (const mode of ['pan', 'marker', 'circle', 'polygon', 'clear', 'density']) this.button(mode, () => this.options.onChange?.(mode));
        this.label = element('input', undefined, this.element); this.label.maxLength = 200; this.label.value = 'GIS'; this.label.setAttribute('aria-label', t('label'));
        this.status = element('output', t('ready'), this.element); this.status.setAttribute('aria-live', 'polite');
        this.results = element('ul', undefined, this.element); this.results.className = 'offline-gis-results';
    }
    getValue() { return this.label?.value || 'GIS'; }
    setValue(value) { this.status.textContent = String(value); }
    setResults(items) {
        this.results.replaceChildren();
        for (const item of items.slice(0, 100)) element('li', `${item.label} · ${item.lon}, ${item.lat} · ${new Date(item.time).toISOString()}`, this.results);
        this.results.setAttribute('aria-label', `${t('count')}: ${items.length}`);
    }
}
export class GisTimeline extends Control {
    build() {
        const min = this.options.min ?? 0, max = this.options.max ?? 4102444800000;
        if (!Number.isSafeInteger(min) || !Number.isSafeInteger(max) || min < 0 || max <= min) throw new Error('Invalid timeline bounds');
        this.from = element('input', undefined, this.element); this.to = element('input', undefined, this.element);
        for (const [input, key, value] of [[this.from, 'from', min], [this.to, 'time', max]]) {
            input.type = 'range'; input.min = min; input.max = max; input.step = Math.max(1, Math.floor((max - min) / 100)); input.value = value;
            input.setAttribute('aria-label', t(key)); this.listen(input, 'input', () => this.emit());
        }
        this.button('play', () => {
            if (this.timer) { clearInterval(this.timer); this.timer = null; return; }
            this.to.value = this.from.value;
            this.timer = setInterval(() => {
                this.to.value = Math.min(max, Number(this.to.value) + Number(this.to.step)); this.emit();
                if (Number(this.to.value) >= max) { clearInterval(this.timer); this.timer = null; }
            }, 250);
        });
        this.output = element('output', undefined, this.element); this.emit();
    }
    getValue() { return { from: Math.min(Number(this.from.value), Number(this.to.value)), to: Math.max(Number(this.from.value), Number(this.to.value)) }; }
    emit() { const value = this.getValue(); this.output && (this.output.textContent = `${new Date(value.from).toISOString()} — ${new Date(value.to).toISOString()}`); this.options.onChange?.(value); }
    destroy() { clearInterval(this.timer); this.timer = null; super.destroy(); }
}
