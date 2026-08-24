import { createComponentState } from '../../utils/component-state.js';
import { Text } from '../Text/index.js';

/**
 * List — 通用平面清單(複合)。每筆 = Text(primary)+ Text(secondary)。完全由原子組成,確定性。
 */
export class List {
    constructor(options = {}) {
        this.options = { items: [], onItemClick: null, activeId: null, ...options };
        this.activeId = this.options.activeId;
        this.element = null;
        this._children = [];
        this._listeners = [];
        this._itemEntries = [];
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
        this.element = document.createElement('ul');
        this.element.className = 'cl-list';
        this.element.style.cssText = 'list-style: none; margin: 0; padding: 0;';
        this._renderItems();
    }

    _itemId(item, index) {
        if (item && typeof item === 'object') {
            if (item.id !== undefined && item.id !== null) return item.id;
            if (item.value !== undefined && item.value !== null) return item.value;
        }
        return index;
    }

    _isActive(itemId) {
        return this.activeId !== null
            && this.activeId !== undefined
            && String(itemId) === String(this.activeId);
    }

    _listen(element, type, handler) {
        element.addEventListener(type, handler);
        this._listeners.push({ element, type, handler });
    }

    _clearRenderedItems() {
        for (const { element, type, handler } of this._listeners) {
            element.removeEventListener(type, handler);
        }
        this._listeners = [];
        this._children.forEach((child) => child.destroy?.());
        this._children = [];
        this._itemEntries = [];
        this.element?.replaceChildren();
    }

    _applyActiveState() {
        for (const { li, button, itemId, clickable } of this._itemEntries) {
            const active = this._isActive(itemId);
            li.className = [
                'cl-list-item',
                clickable ? 'cl-list-item--clickable' : '',
                active ? 'cl-list-item--active' : '',
            ].filter(Boolean).join(' ');
            if (button) {
                button.setAttribute('aria-pressed', active ? 'true' : 'false');
                button.style.background = active ? 'var(--cl-bg-secondary)' : 'transparent';
                button.style.color = active ? 'var(--cl-primary)' : 'inherit';
            } else {
                li.style.background = active ? 'var(--cl-bg-secondary)' : 'transparent';
                li.style.color = active ? 'var(--cl-primary)' : 'inherit';
            }
        }
    }

    _renderItems() {
        if (!this.element) return;
        this._clearRenderedItems();
        const items = Array.isArray(this.options.items) ? this.options.items : [];
        items.forEach((item, idx) => {
            const li = document.createElement('li');
            const clickable = typeof this.options.onItemClick === 'function';
            const itemId = this._itemId(item, idx);
            const active = this._isActive(itemId);
            li.dataset.itemId = String(itemId);
            li.style.cssText = `border-bottom: ${idx === items.length - 1 ? 'none' : '1px solid var(--cl-border)'};`;

            let contentHost = li;
            let button = null;
            if (clickable) {
                button = document.createElement('button');
                button.type = 'button';
                button.className = 'cl-list-item__action';
                button.dataset.itemId = String(itemId);
                button.disabled = Boolean(item && typeof item === 'object' && item.disabled);
                button.setAttribute('aria-pressed', active ? 'true' : 'false');
                button.style.cssText = `
                    display: block;
                    width: 100%;
                    padding: 10px 12px;
                    border: 0;
                    border-radius: 0;
                    background: ${active ? 'var(--cl-bg-secondary)' : 'transparent'};
                    color: ${active ? 'var(--cl-primary)' : 'inherit'};
                    text-align: left;
                    cursor: ${button.disabled ? 'not-allowed' : 'pointer'};
                    font: inherit;
                `;
                this._listen(button, 'mouseenter', () => {
                    if (!this._isActive(itemId) && !button.disabled) button.style.background = 'var(--cl-bg-secondary)';
                });
                this._listen(button, 'mouseleave', () => {
                    if (!this._isActive(itemId)) button.style.background = 'transparent';
                });
                this._listen(button, 'click', (event) => {
                    if (button.disabled) return;
                    this.setActive(itemId);
                    this.options.onItemClick(item, event);
                });
                li.appendChild(button);
                contentHost = button;
            } else {
                li.style.padding = '10px 12px';
                if (active) {
                    li.style.background = 'var(--cl-bg-secondary)';
                    li.style.color = 'var(--cl-primary)';
                }
            }

            const primaryText = item && typeof item === 'object'
                ? (item.primary ?? item.label ?? item.name ?? '')
                : (item ?? '');
            const primary = new Text({ text: String(primaryText), variant: 'body' });
            primary.mount(contentHost);
            this._children.push(primary);
            if (item && typeof item === 'object' && item.secondary) {
                const secondary = new Text({ text: String(item.secondary), variant: 'caption' });
                secondary.mount(contentHost);
                this._children.push(secondary);
            }
            this.element.appendChild(li);
            this._itemEntries.push({ li, button, itemId, clickable });
        });
        this._applyActiveState();
    }

    _applyState() { if (this.element) this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : ''; }
    snapshot() { return this._state.snapshot(); }
    send(e, p = null) { const n = this._state.send(e, p); this._applyState(); return n; }

    mount(container) {
        const t = typeof container === 'string' ? document.querySelector(container) : container;
        if (!t) { console.warn('[List] mount target not found:', container); return this; }
        t.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }

    setItems(items) {
        this.options.items = Array.isArray(items) ? items : [];
        this._renderItems();
        this._applyState();
        return this;
    }

    setActive(id) {
        this.activeId = id;
        this.options.activeId = id;
        this._applyActiveState();
        this._applyState();
        return this;
    }

    setActiveId(id) {
        return this.setActive(id);
    }

    destroy() {
        if (!this.element) return;
        this._clearRenderedItems();
        this.send('DESTROY');
        this.element.remove();
        this.element = null;
    }
}

export default List;
