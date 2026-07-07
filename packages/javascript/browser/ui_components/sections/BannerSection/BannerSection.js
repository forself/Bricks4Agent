import { createComponentState } from '../../utils/component-state.js';
import { Heading } from '../../common/Heading/index.js';
import { Text } from '../../common/Text/index.js';
import { BasicButton } from '../../common/BasicButton/index.js';
import { Link } from '../../common/Link/index.js';

/**
 * BannerSection — 區段複合(中性命名,取代 hero)。
 * = 媒體 + Heading + Text + 行動(BasicButton/Link)。完全由原子/複合組成,確定性。
 */
export class BannerSection {
    constructor(options = {}) {
        this.options = { title: '', body: '', mediaUrl: '', mediaAlt: '', actionLabel: '', actionUrl: '', actionScope: 'internal', onAction: null, ...options };
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
        this._create();
        this._applyState();
    }

    _safeMedia(url) {
        const v = String(url ?? '').trim();
        if (!v) return '';
        if (v.startsWith('/')) return v;
        try { const u = new URL(v, 'https://local.invalid'); return (u.protocol === 'http:' || u.protocol === 'https:') ? v : ''; } catch { return ''; }
    }

    _create() {
        const media = this._safeMedia(this.options.mediaUrl);
        this.element = document.createElement('section');
        this.element.className = 'cl-banner' + (media ? ' cl-banner--with-media' : '');
        this.element.style.cssText = 'display: grid; gap: 16px; padding: 32px 24px; align-items: center;'
            + (media ? ' grid-template-columns: 1fr 1fr;' : '');

        const body = document.createElement('div');
        if (this.options.title) { const h = new Heading({ text: this.options.title, level: 1 }); h.mount(body); this._children.push(h); }
        if (this.options.body) { const t = new Text({ text: this.options.body, variant: 'body' }); t.mount(body); this._children.push(t); }
        if (this.options.actionLabel) {
            const cta = document.createElement('div');
            cta.className = 'cl-banner-cta';
            cta.style.cssText = 'margin-top: 8px;';
            if (this.options.actionUrl) {
                const link = new Link({ text: this.options.actionLabel, href: this.options.actionUrl, scope: this.options.actionScope, onClick: this.options.onAction });
                link.mount(cta); this._children.push(link);
            } else {
                const btn = new BasicButton({ type: 'custom', customLabel: this.options.actionLabel, showIcon: false, variant: 'primary', onClick: this.options.onAction });
                (btn.mount ? btn.mount(cta) : btn.render?.(cta)); this._children.push(btn);
            }
            body.appendChild(cta);
        }
        this.element.appendChild(body);

        if (media) {
            const mw = document.createElement('div');
            mw.className = 'cl-banner-media';
            const img = document.createElement('img');
            img.src = media;
            img.alt = String(this.options.mediaAlt ?? '');
            img.style.cssText = 'width: 100%; height: auto; border-radius: var(--cl-radius-md); display: block;';
            mw.appendChild(img);
            this.element.appendChild(mw);
        }
    }

    _applyState() { if (this.element) this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : 'grid'; }
    snapshot() { return this._state.snapshot(); }
    send(e, p = null) { const n = this._state.send(e, p); this._applyState(); return n; }

    mount(container) {
        const t = typeof container === 'string' ? document.querySelector(container) : container;
        if (!t) { console.warn('[BannerSection] mount target not found:', container); return this; }
        t.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this._children.forEach((c) => c.destroy?.()); this._children = []; this.send('DESTROY'); this.element?.remove(); this.element = null; }
}

export default BannerSection;
