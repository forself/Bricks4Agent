import { createComponentState } from '../../utils/component-state.js';

/**
 * Icon — 圖示原子。固定 inline-SVG 註冊表(閉集)。
 * 未知 name = fail-closed(渲染空 + warn),絕不現捏;path 來自固定表,非使用者輸入,無注入面。
 */
const ICON_PATHS = {
    'chevron-down': 'M6 9l6 6 6-6',
    'chevron-up': 'M18 15l-6-6-6 6',
    'chevron-left': 'M15 18l-6-6 6-6',
    'chevron-right': 'M9 18l6-6-6-6',
    'close': 'M18 6L6 18M6 6l12 12',
    'check': 'M20 6L9 17l-5-5',
    'search': 'M21 21l-4.35-4.35M11 19a8 8 0 100-16 8 8 0 000 16z',
    'info': 'M12 16v-4M12 8h.01M12 22a10 10 0 100-20 10 10 0 000 20z',
    'warning': 'M12 9v4M12 17h.01M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z',
    'error': 'M12 8v4M12 16h.01M12 22a10 10 0 100-20 10 10 0 000 20z',
    'menu': 'M3 12h18M3 6h18M3 18h18',
    'plus': 'M12 5v14M5 12h14',
    'minus': 'M5 12h14',
    'external-link': 'M18 13v6a2 2 0 01-2 2H5a2 2 0 01-2-2V8a2 2 0 012-2h6M15 3h6v6M10 14L21 3'
};

export class Icon {
    static SIZES = { SMALL: 'small', MEDIUM: 'medium', LARGE: 'large' };

    /** 公開閉集:可用圖示名稱。 */
    static names() { return Object.keys(ICON_PATHS); }
    static has(name) { return Object.prototype.hasOwnProperty.call(ICON_PATHS, name); }

    constructor(options = {}) {
        this.options = { name: '', size: Icon.SIZES.MEDIUM, label: '', spin: false, ...options };
        this.element = null;
        this._state = createComponentState(this._buildInitialState(), {
            MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
            SHOW: (s) => ({ ...s, visibility: 'visible' }),
            HIDE: (s) => ({ ...s, visibility: 'hidden' }),
            SET_NAME: (s, p) => ({ ...s, content: { ...s.content, name: String(p?.name ?? '') } }),
            DESTROY: (s) => ({ ...s, lifecycle: 'destroyed' })
        });
        this._injectStyles();
        this._create();
        this._applyState();
    }

    _buildInitialState() {
        return {
            lifecycle: 'created',
            visibility: 'visible',
            content: {
                name: String(this.options.name),
                size: this.options.size,
                label: this.options.label,
                spin: !!this.options.spin
            }
        };
    }

    _injectStyles() {
        if (document.getElementById('icon-component-styles')) return;
        const style = document.createElement('style');
        style.id = 'icon-component-styles';
        style.textContent = `
            .cl-icon { display: inline-flex; align-items: center; justify-content: center; color: currentColor; vertical-align: middle; }
            .cl-icon svg { display: block; stroke: currentColor; stroke-width: 2; stroke-linecap: round; stroke-linejoin: round; fill: none; }
            .cl-icon--small svg { width: 16px; height: 16px; }
            .cl-icon--medium svg { width: 20px; height: 20px; }
            .cl-icon--large svg { width: 24px; height: 24px; }
            .cl-icon--spin svg { animation: cl-icon-spin 1s linear infinite; }
            @keyframes cl-icon-spin { to { transform: rotate(360deg); } }
        `;
        document.head.appendChild(style);
    }

    _create() { this.element = document.createElement('span'); }

    _applyState() {
        if (!this.element) return;
        const { content, visibility } = this.snapshot();
        const classes = ['cl-icon', `cl-icon--${content.size}`];
        if (content.spin) classes.push('cl-icon--spin');
        this.element.className = classes.join(' ');
        this.element.style.display = visibility === 'hidden' ? 'none' : '';
        if (content.label) this.element.setAttribute('aria-label', content.label);
        else this.element.setAttribute('aria-hidden', 'true');

        this.element.replaceChildren();
        const path = ICON_PATHS[content.name];
        if (!path) {
            if (content.name) console.warn('[Icon] unknown icon (fail-closed, rendered empty):', content.name);
            return;
        }
        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('viewBox', '0 0 24 24');
        const p = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        p.setAttribute('d', path);
        svg.appendChild(p);
        this.element.appendChild(svg);
    }

    snapshot() { return this._state.snapshot(); }

    send(event, payload = null) {
        const next = this._state.send(event, payload);
        this._applyState();
        return next;
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) { console.warn('[Icon] mount target not found:', container); return this; }
        target.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    setName(name) { this.send('SET_NAME', { name }); return this; }
    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this.send('DESTROY'); this.element?.remove(); this.element = null; }
}

export default Icon;
