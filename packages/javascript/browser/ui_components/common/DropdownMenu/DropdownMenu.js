import { createComponentState } from '../../utils/component-state.js';
import { Link } from '../Link/index.js';
import { Icon } from '../Icon/index.js';

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
        this._create();
        this._applyState();
    }

    _setOpen(open) { this.send('SET_OPEN', { open }); }

    _create() {
        this.element = document.createElement('div');
        this.element.className = 'cl-dropdownmenu';
        this.element.style.cssText = `
            position: relative;
            display: inline-block;
        `;
        const trigger = document.createElement('button');
        trigger.type = 'button';
        trigger.className = 'cl-dropdownmenu-trigger';
        trigger.style.cssText = `
            display: inline-flex;
            align-items: center;
            gap: 4px;
            cursor: pointer;
            background: var(--cl-bg-primary);
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-md);
            padding: 6px 12px;
            font-family: var(--cl-font-family);
            color: var(--cl-text-primary);
        `;
        trigger.append(document.createTextNode(this.options.label + ' '));
        const caret = new Icon({ name: 'chevron-down', size: 'small' });
        caret.mount(trigger);
        this._children.push(caret);
        trigger.addEventListener('click', () => this._setOpen(!this.snapshot().open));
        this.element.appendChild(trigger);

        this._menu = document.createElement('div');
        this._menu.className = 'cl-dropdownmenu-panel';
        this._menu.style.cssText = `
            position: absolute;
            top: 100%;
            left: 0;
            margin-top: 4px;
            min-width: 140px;
            background: var(--cl-bg-primary);
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-md);
            box-shadow: var(--cl-shadow-md);
            padding: 4px;
            z-index: 10;
            display: none;
        `;
        (Array.isArray(this.options.items) ? this.options.items : []).forEach((item) => {
            const link = new Link({
                text: String(item.label ?? ''),
                href: String(item.href ?? ''),
                scope: item.href ? (item.scope ?? 'internal') : 'none',
                onClick: () => { this._setOpen(false); if (typeof item.onClick === 'function') item.onClick(item); }
            });
            link.mount(this._menu);
            const el = link.element;
            if (el) {
                el.classList.add('cl-dropdownmenu-item');
                el.style.setProperty('display', 'block');
                el.style.setProperty('padding', '6px 10px');
                el.style.setProperty('border-radius', 'var(--cl-radius-sm)');
                el.addEventListener('mouseenter', () => el.style.setProperty('background', 'var(--cl-bg-secondary)'));
                el.addEventListener('mouseleave', () => el.style.removeProperty('background'));
                el.addEventListener('focus', () => el.style.setProperty('background', 'var(--cl-bg-secondary)'));
                el.addEventListener('blur', () => el.style.removeProperty('background'));
            }
            this._children.push(link);
        });
        this.element.appendChild(this._menu);
        document.addEventListener('click', this._onDocClick);
    }

    _applyState() {
        if (!this.element) return;
        const s = this.snapshot();
        this.element.classList.toggle('cl-dropdownmenu--open', !!s.open);
        if (this._menu) this._menu.style.display = s.open ? 'block' : 'none';
        this.element.style.display = s.visibility === 'hidden' ? 'none' : 'inline-block';
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
