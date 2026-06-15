import { createComponentState } from '../../utils/component-state.js';
import { Heading } from '../../common/Heading/Heading.js';
import { Link } from '../../common/Link/Link.js';

/**
 * PageHeader — 站頭導覽區段複合。= 品牌(Heading)+ 導覽(Link 群)。確定性。
 */
export class PageHeader {
    constructor(options = {}) {
        this.options = { brand: '', navLinks: [], ...options };
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
        if (document.getElementById('pageheader-component-styles')) return;
        const s = document.createElement('style');
        s.id = 'pageheader-component-styles';
        s.textContent = `
            .cl-pageheader { display: flex; align-items: center; justify-content: space-between; gap: 16px;
                padding: 12px 24px; border-bottom: 1px solid var(--cl-border); }
            .cl-pageheader-nav { display: flex; gap: 16px; flex-wrap: wrap; }
        `;
        document.head.appendChild(s);
    }

    _create() {
        this.element = document.createElement('header');
        this.element.className = 'cl-pageheader';
        const brand = document.createElement('div');
        if (this.options.brand) { const h = new Heading({ text: this.options.brand, level: 3 }); h.mount(brand); this._children.push(h); }
        this.element.appendChild(brand);
        const nav = document.createElement('nav');
        nav.className = 'cl-pageheader-nav';
        (Array.isArray(this.options.navLinks) ? this.options.navLinks : []).forEach((l) => {
            const link = new Link({ text: String(l.label ?? ''), href: String(l.href ?? ''), scope: l.scope ?? 'internal' });
            link.mount(nav); this._children.push(link);
        });
        this.element.appendChild(nav);
    }

    _applyState() { if (this.element) this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : ''; }
    snapshot() { return this._state.snapshot(); }
    send(e, p = null) { const n = this._state.send(e, p); this._applyState(); return n; }

    mount(container) {
        const t = typeof container === 'string' ? document.querySelector(container) : container;
        if (!t) { console.warn('[PageHeader] mount target not found:', container); return this; }
        t.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this._children.forEach((c) => c.destroy?.()); this._children = []; this.send('DESTROY'); this.element?.remove(); this.element = null; }
}

export default PageHeader;
