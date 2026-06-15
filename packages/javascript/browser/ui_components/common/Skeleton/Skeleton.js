import { createComponentState } from '../../utils/component-state.js';

/**
 * Skeleton — 載入骨架原子。葉子;純 CSS shimmer(無 random/Date),確定性。
 */
export class Skeleton {
    static VARIANTS = { TEXT: 'text', RECT: 'rect', CIRCLE: 'circle' };

    constructor(options = {}) {
        this.options = {
            variant: Skeleton.VARIANTS.TEXT,
            width: '',
            height: '',
            lines: 3,
            ...options
        };
        this.element = null;
        this._state = createComponentState(
            { lifecycle: 'created', visibility: 'visible' },
            {
                MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
                SHOW: (s) => ({ ...s, visibility: 'visible' }),
                HIDE: (s) => ({ ...s, visibility: 'hidden' }),
                DESTROY: (s) => ({ ...s, lifecycle: 'destroyed' })
            }
        );
        this._injectStyles();
        this._create();
        this._applyState();
    }

    _injectStyles() {
        if (document.getElementById('skeleton-component-styles')) return;
        const style = document.createElement('style');
        style.id = 'skeleton-component-styles';
        style.textContent = `
            .cl-skeleton { display: block; }
            .cl-skeleton-item { background: linear-gradient(90deg, var(--cl-bg-secondary) 25%, var(--cl-border) 37%, var(--cl-bg-secondary) 63%);
                background-size: 400% 100%; animation: cl-skeleton-shimmer 1.4s ease infinite; border-radius: var(--cl-radius-sm); }
            .cl-skeleton--text .cl-skeleton-item { height: 12px; margin-bottom: 8px; }
            .cl-skeleton--text .cl-skeleton-item:last-child { width: 60%; margin-bottom: 0; }
            .cl-skeleton--rect .cl-skeleton-item { width: 100%; height: 120px; }
            .cl-skeleton--circle .cl-skeleton-item { width: 40px; height: 40px; border-radius: var(--cl-radius-round); }
            @keyframes cl-skeleton-shimmer { 0% { background-position: 100% 50%; } 100% { background-position: 0 50%; } }
        `;
        document.head.appendChild(style);
    }

    _create() {
        this.element = document.createElement('div');
        this.element.className = `cl-skeleton cl-skeleton--${this.options.variant}`;
        const count = this.options.variant === Skeleton.VARIANTS.TEXT
            ? Math.max(1, parseInt(this.options.lines, 10) || 1)
            : 1;
        for (let i = 0; i < count; i++) {
            const item = document.createElement('div');
            item.className = 'cl-skeleton-item';
            if (this.options.width) item.style.width = this.options.width;
            if (this.options.height) item.style.height = this.options.height;
            this.element.appendChild(item);
        }
    }

    _applyState() {
        if (!this.element) return;
        this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : '';
    }

    snapshot() { return this._state.snapshot(); }
    send(event, payload = null) { const next = this._state.send(event, payload); this._applyState(); return next; }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) { console.warn('[Skeleton] mount target not found:', container); return this; }
        target.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this.send('DESTROY'); this.element?.remove(); this.element = null; }
}

export default Skeleton;
