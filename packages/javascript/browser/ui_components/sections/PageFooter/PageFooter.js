import { createComponentState } from '../../utils/component-state.js';
import { Link } from '../../common/Link/index.js';
import { Text } from '../../common/Text/index.js';

/**
 * PageFooter — 站尾區段複合。= 連結群(Link)+ 版權(Text)。確定性。
 */
export class PageFooter {
    constructor(options = {}) {
        this.options = { links: [], copyright: '', ...options };
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

    _create() {
        this.element = document.createElement('footer');
        this.element.className = 'cl-pagefooter';
        this.element.style.cssText = 'padding: 24px; border-top: 1px solid var(--cl-border); display: flex; flex-direction: column; gap: 10px;';
        const links = document.createElement('div');
        links.className = 'cl-pagefooter-links';
        links.style.cssText = 'display: flex; gap: 16px; flex-wrap: wrap;';
        (Array.isArray(this.options.links) ? this.options.links : []).forEach((l) => {
            const link = new Link({ text: String(l.label ?? ''), href: String(l.href ?? ''), scope: l.scope ?? 'internal' });
            link.mount(links); this._children.push(link);
        });
        this.element.appendChild(links);
        if (this.options.copyright) { const c = new Text({ text: this.options.copyright, variant: 'caption' }); c.mount(this.element); this._children.push(c); }
    }

    _applyState() { if (this.element) this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : 'flex'; }
    snapshot() { return this._state.snapshot(); }
    send(e, p = null) { const n = this._state.send(e, p); this._applyState(); return n; }

    mount(container) {
        const t = typeof container === 'string' ? document.querySelector(container) : container;
        if (!t) { console.warn('[PageFooter] mount target not found:', container); return this; }
        t.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this._children.forEach((c) => c.destroy?.()); this._children = []; this.send('DESTROY'); this.element?.remove(); this.element = null; }
}

export default PageFooter;
