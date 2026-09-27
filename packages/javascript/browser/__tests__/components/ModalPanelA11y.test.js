import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { ModalPanel } from '../../ui_components/layout/Panel/ModalPanel.js';
import { Dropdown } from '../../ui_components/form/Dropdown/Dropdown.js';
import { MultiSelectDropdown } from '../../ui_components/form/MultiSelectDropdown/MultiSelectDropdown.js';
import Locale from '../../ui_components/i18n/index.js';

const ITEMS = [{ label: 'A', value: 'a' }, { label: 'B', value: 'b' }];
const modals = [];

function makeModal(options = {}) {
    const modal = new ModalPanel({ title: '標題', ...options });
    modals.push(modal);
    modal.mount();
    return modal;
}

function content(...children) {
    const box = document.createElement('div');
    box.append(...children);
    return box;
}

function button(text) {
    const el = document.createElement('button');
    el.type = 'button';
    el.textContent = text;
    return el;
}

function pressEscape(target = document.body) {
    const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    return event;
}

function pressTab(shiftKey = false) {
    const target = document.activeElement || document.body;
    const event = new KeyboardEvent('keydown', { key: 'Tab', shiftKey, bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    return event;
}

const isOpen = (modal) => modal.options.visibility === 'visible';

describe('ModalPanel accessibility', () => {
    let opener;

    beforeEach(() => {
        opener = button('open');
        document.body.appendChild(opener);
        opener.focus();
    });

    afterEach(() => {
        modals.splice(0).forEach((modal) => modal.destroy());
        opener.remove();
        ModalPanel.defaults.manageFocus = false;
        Locale.setLang('zh-TW');
        vi.restoreAllMocks();
    });

    it('exposes dialog semantics named by the title', () => {
        const modal = makeModal();
        const panel = modal.element;
        expect(panel.getAttribute('role')).toBe('dialog');
        expect(panel.getAttribute('aria-modal')).toBe('true');
        const title = document.getElementById(panel.getAttribute('aria-labelledby'));
        expect(title?.textContent).toBe('標題');
    });

    it('uses ariaLabel when there is no title', () => {
        const modal = makeModal({ title: '', ariaLabel: '設定' });
        expect(modal.element.getAttribute('aria-label')).toBe('設定');
        expect(modal.element.hasAttribute('aria-labelledby')).toBe(false);
    });

    it('labels the close button through Locale', () => {
        expect(makeModal().element.querySelector('.panel__close').getAttribute('aria-label')).toBe('關閉');
        Locale.setLang('en');
        expect(makeModal().element.querySelector('.panel__close').getAttribute('aria-label')).toBe('Close');
    });

    it('closes only the topmost of stacked modals on Escape', () => {
        const lower = makeModal();
        const upper = makeModal();
        lower.open();
        upper.open();

        pressEscape();
        expect(isOpen(upper)).toBe(false);
        expect(isOpen(lower)).toBe(true);

        pressEscape();
        expect(isOpen(lower)).toBe(false);
    });

    it('closes only the topmost modal even when it registered its listener first', () => {
        const upper = makeModal();
        const lower = makeModal();
        lower.open();
        upper.open();

        pressEscape();
        expect(isOpen(upper)).toBe(false);
        expect(isOpen(lower)).toBe(true);
    });

    it('leaves the modal open when an inner control handled Escape', () => {
        const inner = button('inner');
        const modal = makeModal();
        modal.setContent(content(inner));
        inner.addEventListener('keydown', (event) => event.preventDefault());
        modal.open();

        pressEscape(inner);
        expect(isOpen(modal)).toBe(true);
    });

    it('lets an open Dropdown take the first Escape', () => {
        const host = document.createElement('div');
        const dropdown = new Dropdown({ items: ITEMS, variant: 'searchable' });
        dropdown.mount(host);
        const modal = makeModal();
        modal.setContent(content(host));
        modal.open();
        dropdown.open();
        expect(dropdown.isOpen).toBe(true);

        pressEscape(dropdown.input);
        expect(dropdown.isOpen).toBe(false);
        expect(isOpen(modal)).toBe(true);

        pressEscape(dropdown.input);
        expect(isOpen(modal)).toBe(false);
        dropdown.destroy();
    });

    it('does not let a closed MultiSelectDropdown swallow Escape', () => {
        const host = document.createElement('div');
        const select = new MultiSelectDropdown({ items: ITEMS });
        select.mount(host);
        const modal = makeModal();
        modal.setContent(content(host));
        modal.open();

        pressEscape(select._input);
        expect(isOpen(modal)).toBe(false);
        select.destroy();
    });

    it('keeps focus where it was by default', () => {
        const modal = makeModal();
        modal.setContent(content(button('first')));
        modal.open();
        expect(document.activeElement).toBe(opener);
    });

    it('moves focus in, traps Tab and restores focus when manageFocus is on', () => {
        const first = button('first');
        const last = button('last');
        const modal = makeModal({ manageFocus: true });
        modal.setContent(content(first, last));
        modal.open();
        // 初始焦點落在內容區第一個可聚焦元素（不是標題列的關閉鈕）
        expect(document.activeElement).toBe(first);

        // 標題列的關閉鈕在 DOM 順序上排第一：Tab 從最後一個控制項回到它
        const closeButton = modal.element.querySelector('.panel__close');
        last.focus();
        expect(pressTab().defaultPrevented).toBe(true);
        expect(document.activeElement).toBe(closeButton);

        pressTab(true);
        expect(document.activeElement).toBe(last);

        // 中間的元素交給瀏覽器預設的 Tab 行為
        first.focus();
        expect(pressTab().defaultPrevented).toBe(false);

        modal.close();
        expect(document.activeElement).toBe(opener);
    });

    it('wraps Tab from the last control to the first focusable element of the dialog', () => {
        const only = button('only');
        const modal = makeModal({ manageFocus: true, closable: false });
        modal.setContent(content(only));
        modal.open();
        expect(document.activeElement).toBe(only);
        pressTab();
        expect(document.activeElement).toBe(only);
    });

    it('follows ModalPanel.defaults.manageFocus', () => {
        ModalPanel.defaults.manageFocus = true;
        const field = document.createElement('input');
        const modal = makeModal();
        modal.setContent(content(field));
        modal.open();
        expect(document.activeElement).toBe(field);
    });

    it('honours initialFocus and falls back to the dialog itself', () => {
        const a = button('a');
        const b = button('b');
        b.className = 'target';
        const modal = makeModal({ manageFocus: true, initialFocus: '.target' });
        modal.setContent(content(a, b));
        modal.open();
        expect(document.activeElement).toBe(b);

        const bare = makeModal({ manageFocus: true, title: '', closable: false, showHeader: false });
        bare.setContent(content(document.createTextNode('text only')));
        bare.open();
        expect(document.activeElement).toBe(bare.element);
        expect(bare.element.getAttribute('tabindex')).toBe('-1');
    });

    it('restores focus and removes the Tab trap on destroy', () => {
        const remove = vi.spyOn(document, 'removeEventListener');
        const modal = makeModal({ manageFocus: true });
        modal.setContent(content(button('x')));
        modal.open();
        modal.destroy();
        expect(document.activeElement).toBe(opener);
        expect(remove.mock.calls.some(([type, , capture]) => type === 'keydown' && capture === true)).toBe(true);
    });
});
