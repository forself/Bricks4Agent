import { createComponentState } from '../../utils/component-state.js';
import { Link } from '../Link/Link.js';
import { Icon } from '../Icon/Icon.js';

/**
 * DropdownMenu — 動作選單(複合,vs Dropdown=select)。觸發鈕 + 選單(Link 項)。確定性開合。
 */
export class DropdownMenu {
    constructor(options = {}) {
        this.options = { label: '選單', items: [], ...options };
        this.element = null;
        this._menu = null;
        this._children = [];
        this._onDocClick = (e) => { if (this.element && !this.element.contains(e.target)) this._setOpen(false); };
        this._state = createComponentState(
            { lifecycle: 'created', visibility: 'visible', open: false },
            {
                MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
                SET_OPEN: (s, p) => ({ ...s, open: !!p?.open }),
                DESTROY: (s) => ({ ...s, lifecycle: 'destroyed' })
            }
        );
        this._injectStyles();
        this._create();
        this._applyState();
    }

    _injectStyles() {
        if (document.getElementById('dropdownmenu-component-styles')) return;
        const s = document.createElement('style');
        s.id = 'dropdownmenu-component-styles';
        s.textContent = `
            .cl-dropdownmenu { position: relative; display: inline-block; }
            .cl-dropdownmenu-trigger { display: inline-flex; align-items: center; gap: 4px; cursor: pointer;
                background: var(--cl-bg-primary); border: 1px solid var(--cl-border); border-radius: var(--cl-radius-md);
                padding: 6px 12px; font-family: var(--cl-font-family); color: var(--cl-text-primary); }
            .cl-dropdownmenu-panel { position: absolute; top: 100%; left: 0; margin-top: 4px; min-width: 140px;
                background: var(--cl-bg-primary); border: 1px solid var(--cl-border); border-radius: var(--cl-radius-md);
                box-shadow: 0 4px 16px rgba(0,0,0,0.12); padding: 4px; z-index: 10; display: none; }
            .cl-dropdownmenu--open .cl-dropdownmenu-panel { display: block; }
            .cl-dropdownmenu-item { display: block; padding: 6px 10px; border-radius: var(--cl-radius-sm); }
            .cl-dropdownmenu-item:hover { background: var(--cl-bg-secondary); }
        `;
        document.head.appendChild(s);
    }

    _setOpen(open) { this.send('SET_OPEN', { open }); }

    _create() {
        this.element = document.createElement('div');
        this.element.className = 'cl-dropdownmenu';
        const trigger = document.createElement('button');
        trigger.type = 'button';
        trigger.className = 'cl-dropdownmenu-trigger';
        trigger.append(document.createTextNode(this.options.label + ' '));
        const caret = new Icon({ name: 'chevron-down', size: 'small' });
        caret.mount(trigger);
        this._children.push(caret);
        trigger.addEventListener('click', () => this._setOpen(!this.snapshot().open));
        this.element.appendChild(trigger);

        this._menu = document.createElement('div');
        this._menu.className = 'cl-dropdownmenu-panel';
        (Array.isArray(this.options.items) ? this.options.items : []).forEach((item) => {
            const link = new Link({
                text: String(item.label ?? ''),
                href: String(item.href ?? ''),
                scope: item.href ? (item.scope ?? 'internal') : 'none',
                onClick: () => { this._setOpen(false); if (typeof item.onClick === 'function') item.onClick(item); }
            });
            link.mount(this._menu);
            link.element?.classList.add('cl-dropdownmenu-item');
            this._children.push(link);
        });
        this.element.appendChild(this._menu);
        document.addEventListener('click', this._onDocClick);
    }

    _applyState() {
        if (!this.element) return;
        const s = this.snapshot();
        this.element.classList.toggle('cl-dropdownmenu--open', !!s.open);
        this.element.style.display = s.visibility === 'hidden' ? 'none' : '';
    }

    snapshot() { return this._state.snapshot(); }
    send(e, p = null) { const n = this._state.send(e, p); this._applyState(); return n; }

    mount(container) {
        const t = typeof container === 'string' ? document.querySelector(container) : container;
        if (!t) { console.warn('[DropdownMenu] mount target not found:', container); return this; }
        t.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    open() { this._setOpen(true); return this; }
    close() { this._setOpen(false); return this; }
    isOpen() { return this.snapshot().open; }
    destroy() {
        document.removeEventListener('click', this._onDocClick);
        this._children.forEach((c) => c.destroy?.());
        this._children = [];
        this.send('DESTROY'); this.element?.remove(); this.element = null;
    }
}

export default DropdownMenu;
