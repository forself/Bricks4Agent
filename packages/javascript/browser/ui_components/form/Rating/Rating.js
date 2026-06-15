import { createComponentState } from '../../utils/component-state.js';

/**
 * Rating — 星等輸入 / 顯示原子。葉子:自繪星形(不組合 Icon),確定性。
 */
export class Rating {
    constructor(options = {}) {
        this.options = { value: 0, max: 5, readonly: false, onChange: null, ...options };
        this.element = null;
        this._stars = [];
        this._state = createComponentState(this._buildInitialState(), {
            MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
            SHOW: (s) => ({ ...s, visibility: 'visible' }),
            HIDE: (s) => ({ ...s, visibility: 'hidden' }),
            SET_VALUE: (s, p) => ({ ...s, content: { ...s.content, value: this._clamp(Number(p?.value)) } }),
            DESTROY: (s) => ({ ...s, lifecycle: 'destroyed' })
        });
        this._injectStyles();
        this._create();
        this._applyState();
    }

    _clamp(value) {
        if (Number.isNaN(value)) return 0;
        return Math.min(this.options.max, Math.max(0, Math.round(value)));
    }

    _buildInitialState() {
        return { lifecycle: 'created', visibility: 'visible', content: { value: this._clamp(Number(this.options.value)) } };
    }

    _injectStyles() {
        if (document.getElementById('rating-component-styles')) return;
        const style = document.createElement('style');
        style.id = 'rating-component-styles';
        style.textContent = `
            .cl-rating { display: inline-flex; gap: 2px; color: var(--cl-warning); line-height: 1; }
            .cl-rating--interactive .cl-rating-star { cursor: pointer; }
            .cl-rating-star { width: 18px; height: 18px; }
            .cl-rating-star svg { width: 18px; height: 18px; display: block; }
        `;
        document.head.appendChild(style);
    }

    _create() {
        this.element = document.createElement('span');
        this.element.className = 'cl-rating' + (this.options.readonly ? '' : ' cl-rating--interactive');
        const max = Math.max(1, parseInt(this.options.max, 10) || 5);
        for (let i = 1; i <= max; i++) {
            const star = document.createElement('span');
            star.className = 'cl-rating-star';
            star.dataset.index = String(i);
            const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            svg.setAttribute('viewBox', '0 0 24 24');
            const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
            path.setAttribute('d', 'M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z');
            svg.appendChild(path);
            star.appendChild(svg);
            if (!this.options.readonly) {
                star.addEventListener('click', () => {
                    this.send('SET_VALUE', { value: i });
                    if (typeof this.options.onChange === 'function') this.options.onChange(this.getValue());
                });
            }
            this.element.appendChild(star);
            this._stars.push({ index: i, el: star, path });
        }
    }

    _applyState() {
        if (!this.element) return;
        const { content, visibility } = this.snapshot();
        for (const { index, path } of this._stars) {
            path.setAttribute('fill', index <= content.value ? 'currentColor' : 'none');
            path.setAttribute('stroke', 'currentColor');
            path.setAttribute('stroke-width', '1.5');
        }
        this.element.style.display = visibility === 'hidden' ? 'none' : '';
    }

    snapshot() { return this._state.snapshot(); }

    send(event, payload = null) {
        const next = this._state.send(event, payload);
        this._applyState();
        return next;
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) { console.warn('[Rating] mount target not found:', container); return this; }
        target.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    getValue() { return this.snapshot().content.value; }
    setValue(value) { this.send('SET_VALUE', { value }); return this; }
    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this.send('DESTROY'); this.element?.remove(); this.element = null; this._stars = []; }
}

export default Rating;
