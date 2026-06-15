import { createComponentState } from '../../utils/component-state.js';
import { Heading } from '../../common/Heading/Heading.js';
import { Text } from '../../common/Text/Text.js';

/**
 * ContentSection — 內容區段複合。= Heading + Text(+ 媒體)。確定性。
 */
export class ContentSection {
    constructor(options = {}) {
        this.options = { title: '', body: '', mediaUrl: '', mediaAlt: '', level: 2, ...options };
        this.element = null;
        this._children = [];
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
        if (document.getElementById('contentsection-component-styles')) return;
        const s = document.createElement('style');
        s.id = 'contentsection-component-styles';
        s.textContent = `.cl-content { padding: 24px; display: flex; flex-direction: column; gap: 10px; } .cl-content-media img { max-width: 100%; height: auto; border-radius: var(--cl-radius-md); }`;
        document.head.appendChild(s);
    }

    _safeMedia(url) {
        const v = String(url ?? '').trim();
        if (!v) return '';
        if (v.startsWith('/')) return v;
        try { const u = new URL(v, 'https://local.invalid'); return (u.protocol === 'http:' || u.protocol === 'https:') ? v : ''; } catch { return ''; }
    }

    _create() {
        this.element = document.createElement('section');
        this.element.className = 'cl-content';
        if (this.options.title) { const h = new Heading({ text: this.options.title, level: this.options.level }); h.mount(this.element); this._children.push(h); }
        if (this.options.body) { const t = new Text({ text: this.options.body, variant: 'body', tag: 'p' }); t.mount(this.element); this._children.push(t); }
        const media = this._safeMedia(this.options.mediaUrl);
        if (media) {
            const mw = document.createElement('div');
            mw.className = 'cl-content-media';
            const img = document.createElement('img');
            img.src = media; img.alt = String(this.options.mediaAlt ?? '');
            mw.appendChild(img);
            this.element.appendChild(mw);
        }
    }

    _applyState() { if (this.element) this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : ''; }
    snapshot() { return this._state.snapshot(); }
    send(e, p = null) { const n = this._state.send(e, p); this._applyState(); return n; }

    mount(container) {
        const t = typeof container === 'string' ? document.querySelector(container) : container;
        if (!t) { console.warn('[ContentSection] mount target not found:', container); return this; }
        t.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this._children.forEach((c) => c.destroy?.()); this._children = []; this.send('DESTROY'); this.element?.remove(); this.element = null; }
}

export default ContentSection;
