import { createComponentState } from '../../utils/component-state.js';
import { Link } from '../Link/index.js';
import { Text } from '../Text/index.js';
import { Tag } from '../Tag/index.js';

/**
 * ResultList — 檢索結果列表(複合)。每筆 = Link(標題)+ Text(摘要)+ Text(meta)+ Tag(標籤)。
 * 完全由原子組成,確定性;狀態僅 visibility。
 */
export class ResultList {
    constructor(options = {}) {
        this.options = { items: [], onItemClick: null, emptyText: '無結果', ...options };
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
        this.element = document.createElement('div');
        this.element.className = 'cl-resultlist';
        this.element.style.cssText = `
            display: flex;
            flex-direction: column;
            gap: 12px;
        `;
        const items = Array.isArray(this.options.items) ? this.options.items : [];
        if (items.length === 0) {
            const empty = new Text({ text: this.options.emptyText, variant: 'caption', align: 'center' });
            const wrap = document.createElement('div');
            wrap.className = 'cl-resultlist-empty';
            wrap.style.cssText = `
                color: var(--cl-text-secondary);
                padding: 16px;
                text-align: center;
            `;
            empty.mount(wrap);
            this.element.appendChild(wrap);
            this._children.push(empty);
            return;
        }
        items.forEach((item) => {
            const row = document.createElement('div');
            row.className = 'cl-resultlist-item';
            row.style.cssText = `
                padding: 10px 12px;
                border: 1px solid var(--cl-border);
                border-radius: var(--cl-radius-md);
            `;
            const title = new Link({ text: String(item.title ?? ''), href: String(item.url ?? ''), scope: item.scope ?? 'internal', onClick: this.options.onItemClick ? () => this.options.onItemClick(item) : null });
            title.mount(row);
            title.element?.style.setProperty('font-weight', '600');
            this._children.push(title);
            if (item.snippet) { const sn = new Text({ text: String(item.snippet), variant: 'body' }); sn.mount(row); this._children.push(sn); }
            const meta = document.createElement('div');
            meta.className = 'cl-resultlist-meta';
            meta.style.cssText = `
                display: flex;
                gap: 6px;
                align-items: center;
                margin-top: 4px;
            `;
            if (item.meta) { const m = new Text({ text: String(item.meta), variant: 'caption' }); m.mount(meta); this._children.push(m); }
            (Array.isArray(item.tags) ? item.tags : []).forEach((t) => { const tag = new Tag({ text: String(t) }); (tag.mount ? tag.mount(meta) : tag.render?.(meta)); this._children.push(tag); });
            if (meta.childNodes.length) row.appendChild(meta);
            this.element.appendChild(row);
        });
    }

    _applyState() { if (this.element) this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : 'flex'; }
    snapshot() { return this._state.snapshot(); }
    send(e, p = null) { const n = this._state.send(e, p); this._applyState(); return n; }

    mount(container) {
        const t = typeof container === 'string' ? document.querySelector(container) : container;
        if (!t) { console.warn('[ResultList] mount target not found:', container); return this; }
        t.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    count() { return Array.isArray(this.options.items) ? this.options.items.length : 0; }
    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this._children.forEach((c) => c.destroy?.()); this._children = []; this.send('DESTROY'); this.element?.remove(); this.element = null; }
}

export default ResultList;
