import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import Locale from '../../ui_components/i18n/index.js';
import { Popover as BasePopover } from '../../ui_components/common/Popover/Popover.js';
import { DatePicker } from '../../ui_components/form/DatePicker/DatePicker.js';

// 記下每個實例，測試失敗提早結束時仍在 afterEach 銷毀
const live = new Set();
class Popover extends BasePopover {
    constructor(options) {
        super(options);
        live.add(this);
    }
}

function rect({ top = 0, left = 0, width = 0, height = 0 }) {
    return { top, left, width, height, right: left + width, bottom: top + height, x: left, y: top, toJSON() {} };
}

/** 面板內容：兩個按鈕 */
function actions() {
    const wrap = document.createElement('div');
    const apply = document.createElement('button');
    apply.type = 'button';
    apply.className = 'apply';
    apply.textContent = 'Apply';
    const reset = document.createElement('button');
    reset.type = 'button';
    reset.className = 'reset';
    reset.textContent = 'Reset';
    wrap.append(apply, reset);
    return wrap;
}

function keydown(target, key, init = {}) {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
    target.dispatchEvent(event);
    return event;
}

function netListeners(addSpy, removeSpy, type) {
    const added = addSpy.mock.calls.filter(([name]) => name === type).length;
    const removed = removeSpy.mock.calls.filter(([name]) => name === type).length;
    return added - removed;
}

describe('Popover', () => {
    let host;
    let anchor;
    let after;

    beforeEach(() => {
        Locale.setLang('zh-TW');
        host = document.createElement('div');
        anchor = document.createElement('button');
        anchor.type = 'button';
        anchor.textContent = 'Filters';
        after = document.createElement('button');
        after.type = 'button';
        after.className = 'after';
        after.textContent = 'Next control';
        host.append(anchor, after);
        document.body.appendChild(host);
    });

    afterEach(() => {
        live.forEach((popover) => popover.destroy());
        live.clear();
        host.remove();
        vi.useRealTimers();
        vi.restoreAllMocks();
        Locale.setLang('zh-TW');
    });

    it('decorates the anchor with ARIA and restores the original attributes on destroy', () => {
        anchor.setAttribute('aria-expanded', 'custom');
        const popover = new Popover({ anchor, content: 'Hello' });
        expect(popover.options).toMatchObject({
            trigger: 'click',
            placement: 'bottom-start',
            offset: 8,
            closeOnOutsideClick: true,
            closeOnEscape: true,
            autoFocus: true,
            trapFocus: false,
            returnFocus: true,
            hoverDelay: { open: 150, close: 200 },
            role: 'dialog',
            width: null,
        });
        expect(anchor.getAttribute('aria-haspopup')).toBe('dialog');
        expect(anchor.getAttribute('aria-expanded')).toBe('false');
        expect(anchor.getAttribute('aria-controls')).toBe(popover.element.id);

        popover.open();
        expect(anchor.getAttribute('aria-expanded')).toBe('true');
        popover.destroy();
        expect(anchor.hasAttribute('aria-haspopup')).toBe(false);
        expect(anchor.getAttribute('aria-expanded')).toBe('custom');
        expect(anchor.hasAttribute('aria-controls')).toBe(false);

        // 不屬於 aria-haspopup 值域的 role 不宣告 haspopup
        const region = new Popover({ anchor, role: 'region', content: 'x' });
        expect(anchor.hasAttribute('aria-haspopup')).toBe(false);
        expect(anchor.getAttribute('aria-expanded')).toBe('false');
        region.destroy();
    });

    it('click trigger toggles a panel portaled to document.body with fixed positioning', () => {
        const onOpen = vi.fn();
        const onClose = vi.fn();
        const popover = new Popover({ anchor, title: 'Filters', content: actions, onOpen, onClose });
        const panel = popover.element;
        expect(document.body.contains(panel)).toBe(false);

        anchor.click();
        expect(popover.isOpen()).toBe(true);
        expect(panel.parentNode).toBe(document.body);
        expect(panel.dataset.portal).toBe('body');
        expect(panel.style.position).toBe('fixed');
        expect(panel.getAttribute('role')).toBe('dialog');
        expect(panel.getAttribute('aria-labelledby')).toBe(panel.querySelector('.popover__title').id);
        expect(onOpen).toHaveBeenCalledTimes(1);
        // autoFocus：以點擊開啟時焦點移到第一個可聚焦元素
        expect(document.activeElement).toBe(panel.querySelector('.apply'));

        anchor.click();
        expect(popover.isOpen()).toBe(false);
        expect(onClose).toHaveBeenCalledWith('trigger');
        expect(document.body.contains(panel)).toBe(false);
        expect(anchor.getAttribute('aria-expanded')).toBe('false');
    });

    it('closes on Escape inside the panel, stops propagation and returns focus to the anchor', () => {
        const outer = vi.fn();
        document.addEventListener('keydown', outer);
        const onClose = vi.fn();
        const popover = new Popover({ anchor, content: actions, onClose });
        anchor.click();
        const apply = popover.element.querySelector('.apply');
        expect(document.activeElement).toBe(apply);

        const event = keydown(apply, 'Escape');
        expect(event.defaultPrevented).toBe(true);
        expect(popover.isOpen()).toBe(false);
        expect(onClose).toHaveBeenCalledWith('escape');
        expect(outer).not.toHaveBeenCalled();
        expect(document.activeElement).toBe(anchor);
        document.removeEventListener('keydown', outer);

        // returnFocus: false 時焦點不送回
        const noReturn = new Popover({ anchor, content: actions, returnFocus: false });
        anchor.click();
        keydown(noReturn.element.querySelector('.apply'), 'Escape');
        expect(document.activeElement).not.toBe(anchor);
    });

    it('closes on Escape pressed elsewhere unless closeOnEscape is false', () => {
        const popover = new Popover({ anchor, content: 'x', trigger: 'manual' });
        popover.open();
        keydown(document.body, 'Escape');
        expect(popover.isOpen()).toBe(false);

        const sticky = new Popover({ anchor, content: 'x', trigger: 'manual', closeOnEscape: false });
        sticky.open();
        keydown(document.body, 'Escape');
        keydown(sticky.element, 'Escape');
        expect(sticky.isOpen()).toBe(true);
    });

    it('closes on outside press without stealing focus; presses on the panel or anchor keep it open', () => {
        const onClose = vi.fn();
        const popover = new Popover({ anchor, content: actions, onClose });
        anchor.click();

        popover.element.querySelector('.reset').dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
        anchor.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
        expect(popover.isOpen()).toBe(true);

        after.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
        expect(popover.isOpen()).toBe(false);
        expect(onClose).toHaveBeenLastCalledWith('outside');
        expect(document.activeElement).not.toBe(anchor);

        const sticky = new Popover({ anchor, content: 'x', trigger: 'manual', closeOnOutsideClick: false });
        sticky.open();
        after.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
        expect(sticky.isOpen()).toBe(true);
    });

    it('treats presses inside other body-portaled layers (a DatePicker calendar, a nested popover) as inside', () => {
        const wrap = document.createElement('div');
        const picker = new DatePicker({});
        picker.mount(wrap);
        const innerAnchor = document.createElement('button');
        innerAnchor.type = 'button';
        innerAnchor.textContent = 'More';
        wrap.appendChild(innerAnchor);

        const outer = new Popover({ anchor, content: wrap });
        anchor.click();
        picker.open();
        expect(picker.calendar.dataset.portal).toBe('body');
        picker.calendar.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
        expect(outer.isOpen()).toBe(true);

        const inner = new Popover({ anchor: innerAnchor, content: 'Nested details' });
        innerAnchor.click();
        inner.element.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
        expect(outer.isOpen()).toBe(true);
        expect(inner.isOpen()).toBe(true);

        after.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
        expect(outer.isOpen()).toBe(false);
        expect(inner.isOpen()).toBe(false);
        picker.destroy();
    });

    it('traps Tab focus inside the panel when trapFocus is true', () => {
        const popover = new Popover({ anchor, content: actions, trapFocus: true });
        anchor.click();
        const panel = popover.element;
        expect(panel.getAttribute('aria-modal')).toBe('true');
        const apply = panel.querySelector('.apply');
        const reset = panel.querySelector('.reset');

        expect(keydown(apply, 'Tab').defaultPrevented).toBe(true);
        expect(document.activeElement).toBe(reset);
        keydown(reset, 'Tab');
        expect(document.activeElement).toBe(apply);
        keydown(apply, 'Tab', { shiftKey: true });
        expect(document.activeElement).toBe(reset);
        expect(popover.isOpen()).toBe(true);
    });

    it('stitches the portaled panel into the tab order right after the anchor', () => {
        const onClose = vi.fn();
        const popover = new Popover({ anchor, content: actions, onClose });
        anchor.click();
        const apply = popover.element.querySelector('.apply');
        const reset = popover.element.querySelector('.reset');
        expect(popover.element.hasAttribute('aria-modal')).toBe(false);

        // Shift+Tab 從第一個元素回到錨點，面板保持開啟
        expect(keydown(apply, 'Tab', { shiftKey: true }).defaultPrevented).toBe(true);
        expect(document.activeElement).toBe(anchor);
        expect(popover.isOpen()).toBe(true);

        // Tab 從錨點進入面板
        expect(keydown(anchor, 'Tab').defaultPrevented).toBe(true);
        expect(document.activeElement).toBe(apply);

        // 面板內一般的 Tab 交給瀏覽器
        expect(keydown(apply, 'Tab').defaultPrevented).toBe(false);

        // 從最後一個元素 Tab 出去：焦點到錨點之後的元素並關閉
        reset.focus();
        expect(keydown(reset, 'Tab').defaultPrevented).toBe(true);
        expect(document.activeElement).toBe(after);
        expect(popover.isOpen()).toBe(false);
        expect(onClose).toHaveBeenCalledWith('blur');
    });

    it('hover trigger honours the open/close delays and stays open while the pointer is over the panel', () => {
        vi.useFakeTimers();
        const onOpen = vi.fn();
        const onClose = vi.fn();
        const popover = new Popover({ anchor, content: 'Details', trigger: 'hover', onOpen, onClose });

        // 快速掠過不會開啟
        anchor.dispatchEvent(new MouseEvent('mouseenter'));
        vi.advanceTimersByTime(100);
        anchor.dispatchEvent(new MouseEvent('mouseleave'));
        vi.advanceTimersByTime(1000);
        expect(onOpen).not.toHaveBeenCalled();

        anchor.dispatchEvent(new MouseEvent('mouseenter'));
        vi.advanceTimersByTime(149);
        expect(popover.isOpen()).toBe(false);
        vi.advanceTimersByTime(1);
        expect(popover.isOpen()).toBe(true);
        // hover 開啟不搬動焦點
        expect(popover.element.contains(document.activeElement)).toBe(false);

        // 指標從錨點移到面板上：保持開啟
        anchor.dispatchEvent(new MouseEvent('mouseleave'));
        vi.advanceTimersByTime(150);
        popover.element.dispatchEvent(new MouseEvent('mouseenter'));
        vi.advanceTimersByTime(1000);
        expect(popover.isOpen()).toBe(true);

        popover.element.dispatchEvent(new MouseEvent('mouseleave'));
        vi.advanceTimersByTime(199);
        expect(popover.isOpen()).toBe(true);
        vi.advanceTimersByTime(1);
        expect(popover.isOpen()).toBe(false);
        expect(onClose).toHaveBeenCalledWith('hover');

        // 自訂延遲
        const fast = new Popover({ anchor, content: 'x', trigger: 'hover', hoverDelay: { open: 0, close: 50 } });
        anchor.dispatchEvent(new MouseEvent('mouseenter'));
        vi.advanceTimersByTime(0);
        expect(fast.isOpen()).toBe(true);
    });

    it('hover trigger also opens on keyboard focus and closes after focus leaves', () => {
        vi.useFakeTimers();
        const popover = new Popover({ anchor, content: 'Details', trigger: 'hover' });
        anchor.focus();
        expect(popover.isOpen()).toBe(true);
        expect(document.activeElement).toBe(anchor);

        after.focus();
        expect(popover.isOpen()).toBe(true);
        vi.advanceTimersByTime(200);
        expect(popover.isOpen()).toBe(false);
    });

    it('focus trigger opens on focus, survives focus moving into the panel, and closes on blur', () => {
        const onClose = vi.fn();
        const popover = new Popover({ anchor, content: actions, trigger: 'focus', onClose });
        anchor.focus();
        expect(popover.isOpen()).toBe(true);
        expect(document.activeElement).toBe(anchor);

        popover.element.querySelector('.apply').focus();
        expect(popover.isOpen()).toBe(true);

        after.focus();
        expect(popover.isOpen()).toBe(false);
        expect(onClose).toHaveBeenCalledWith('blur');

        // Escape 送回焦點到錨點時不會因 focusin 立刻重新開啟
        anchor.focus();
        const apply = popover.element.querySelector('.apply');
        apply.focus();
        keydown(apply, 'Escape');
        expect(document.activeElement).toBe(anchor);
        expect(popover.isOpen()).toBe(false);
        expect(onClose).toHaveBeenLastCalledWith('escape');
    });

    it('manual trigger binds nothing on the anchor and is driven by open/close/toggle', () => {
        const anchorAdd = vi.spyOn(anchor, 'addEventListener');
        const popover = new Popover({ anchor, content: actions, trigger: 'manual' });
        expect(anchorAdd).not.toHaveBeenCalled();

        anchor.click();
        expect(popover.isOpen()).toBe(false);
        popover.toggle();
        expect(popover.isOpen()).toBe(true);
        // 程式開啟預設不移動焦點
        expect(popover.element.contains(document.activeElement)).toBe(false);
        popover.toggle();
        expect(popover.isOpen()).toBe(false);

        popover.open({ focus: true });
        expect(document.activeElement).toBe(popover.element.querySelector('.apply'));
        popover.close();
        expect(popover.isOpen()).toBe(false);
    });

    it('flips to the opposite side and shifts into the viewport', () => {
        // jsdom 視窗為 1024 × 768
        const popover = new Popover({ anchor, content: 'x', trigger: 'manual' });
        const anchorRect = vi.spyOn(anchor, 'getBoundingClientRect');
        const panelRect = vi.spyOn(popover.element, 'getBoundingClientRect');
        const panel = popover.element;

        anchorRect.mockReturnValue(rect({ top: 700, left: 100, width: 80, height: 30 }));
        panelRect.mockReturnValue(rect({ width: 200, height: 150 }));
        popover.open();
        expect(panel.dataset.placement).toBe('top-start');
        expect(panel.style.top).toBe('542px');
        expect(panel.style.left).toBe('100px');
        popover.close();

        popover.options.placement = 'right';
        anchorRect.mockReturnValue(rect({ top: 300, left: 900, width: 100, height: 30 }));
        panelRect.mockReturnValue(rect({ width: 200, height: 100 }));
        popover.open();
        expect(panel.dataset.placement).toBe('left');
        expect(panel.style.left).toBe('692px');
        expect(panel.style.top).toBe('265px');
        popover.close();

        popover.options.placement = 'bottom-end';
        anchorRect.mockReturnValue(rect({ top: 100, left: 10, width: 40, height: 20 }));
        panelRect.mockReturnValue(rect({ width: 300, height: 100 }));
        popover.open();
        expect(panel.dataset.placement).toBe('bottom-end');
        expect(panel.style.top).toBe('128px');
        expect(panel.style.left).toBe('8px');

        // updatePosition() 可在外部變動後手動呼叫
        anchorRect.mockReturnValue(rect({ top: 100, left: 400, width: 40, height: 20 }));
        popover.updatePosition();
        expect(panel.style.left).toBe('140px');
    });

    it('honours offset and autoFocus: false, and tolerates a missing anchor', () => {
        const popover = new Popover({ anchor, content: actions, offset: 20, autoFocus: false });
        vi.spyOn(anchor, 'getBoundingClientRect').mockReturnValue(rect({ top: 100, left: 50, width: 80, height: 30 }));
        vi.spyOn(popover.element, 'getBoundingClientRect').mockReturnValue(rect({ width: 200, height: 100 }));
        anchor.click();
        expect(popover.isOpen()).toBe(true);
        expect(popover.element.style.top).toBe('150px');
        expect(popover.element.contains(document.activeElement)).toBe(false);

        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const orphan = new Popover({ content: 'x' });
        expect(warn).toHaveBeenCalled();
        expect(() => orphan.open().close().toggle()).not.toThrow();
        expect(orphan.isOpen()).toBe(false);
    });

    it('renders strings as text, re-evaluates function content on every open, and supports setContent', () => {
        const popover = new Popover({ anchor, content: '<b>bold</b>', trigger: 'manual', width: 280 });
        popover.open();
        const body = popover.element.querySelector('.popover__body');
        expect(body.textContent).toBe('<b>bold</b>');
        expect(body.querySelector('b')).toBeNull();
        expect(popover.element.style.width).toBe('280px');
        popover.close();

        let renders = 0;
        const factory = vi.fn(() => {
            renders += 1;
            const node = document.createElement('span');
            node.textContent = `render ${renders}`;
            return node;
        });
        popover.setContent(factory);
        expect(factory).not.toHaveBeenCalled();
        popover.open();
        expect(body.textContent).toBe('render 1');
        popover.close();
        popover.open();
        expect(body.textContent).toBe('render 2');

        const node = document.createElement('em');
        node.textContent = 'node';
        popover.setContent(node);
        expect(body.firstChild).toBe(node);
    });

    it('uses ariaLabel as the accessible name when there is no title', () => {
        const popover = new Popover({ anchor, content: 'x', ariaLabel: 'Column settings', role: 'group' });
        expect(popover.element.getAttribute('aria-label')).toBe('Column settings');
        expect(popover.element.getAttribute('role')).toBe('group');
        expect(popover.element.hasAttribute('aria-labelledby')).toBe(false);
    });

    it('keeps the panel in its mount container while closed', () => {
        const home = document.createElement('div');
        host.appendChild(home);
        const popover = new Popover({ anchor, content: 'x', trigger: 'manual' }).mount(home);
        expect(popover.element.parentNode).toBe(home);
        expect(popover.element.style.display).toBe('none');

        popover.open();
        expect(popover.element.parentNode).toBe(document.body);
        popover.close();
        expect(popover.element.parentNode).toBe(home);
        popover.destroy();
        expect(home.contains(popover.element)).toBe(false);
    });

    it('coalesces scroll-driven repositioning into one animation frame', async () => {
        const popover = new Popover({ anchor, content: 'x', trigger: 'manual' });
        popover.open();
        const update = vi.spyOn(popover, 'updatePosition');
        window.dispatchEvent(new Event('scroll'));
        window.dispatchEvent(new Event('resize'));
        await new Promise((resolve) => requestAnimationFrame(() => resolve()));
        expect(update).toHaveBeenCalledTimes(1);
        popover.close();
    });

    it('attaches global listeners only while open and removes everything on close and destroy', () => {
        vi.useFakeTimers();
        const docAdd = vi.spyOn(document, 'addEventListener');
        const docRemove = vi.spyOn(document, 'removeEventListener');
        const winAdd = vi.spyOn(window, 'addEventListener');
        const winRemove = vi.spyOn(window, 'removeEventListener');
        const onClose = vi.fn();
        const popover = new Popover({ anchor, content: actions, onClose });
        expect(docAdd).not.toHaveBeenCalled();
        expect(winAdd).not.toHaveBeenCalled();

        anchor.click();
        expect(netListeners(docAdd, docRemove, 'mousedown')).toBe(1);
        expect(netListeners(docAdd, docRemove, 'keydown')).toBe(1);
        expect(netListeners(winAdd, winRemove, 'scroll')).toBe(1);
        expect(netListeners(winAdd, winRemove, 'resize')).toBe(1);

        anchor.click();
        for (const type of ['mousedown', 'keydown']) expect(netListeners(docAdd, docRemove, type)).toBe(0);
        for (const type of ['scroll', 'resize']) expect(netListeners(winAdd, winRemove, type)).toBe(0);

        // 開啟中銷毀：焦點在面板內時送回錨點、不觸發 onClose、全部卸除
        anchor.click();
        expect(popover.element.contains(document.activeElement)).toBe(true);
        onClose.mockClear();
        popover.destroy();
        expect(document.body.contains(popover.element)).toBe(false);
        expect(document.activeElement).toBe(anchor);
        expect(onClose).not.toHaveBeenCalled();
        for (const type of ['mousedown', 'keydown']) expect(netListeners(docAdd, docRemove, type)).toBe(0);
        for (const type of ['scroll', 'resize']) expect(netListeners(winAdd, winRemove, type)).toBe(0);
        // jsdom 的 focus() 自己會排計時器，這裡改以「時間推進後沒有任何動作」確認
        vi.advanceTimersByTime(1000);
        expect(popover.isOpen()).toBe(false);
        expect(onClose).not.toHaveBeenCalled();

        anchor.click();
        expect(document.body.contains(popover.element)).toBe(false);
        expect(() => {
            popover.open();
            popover.close();
            popover.toggle();
            popover.setContent('y');
            popover.updatePosition();
            popover.mount(host);
            popover.destroy();
        }).not.toThrow();
        expect(popover.isOpen()).toBe(false);

        // hover 計時器在 destroy 時清除
        const hover = new Popover({ anchor, content: 'x', trigger: 'hover' });
        const baseline = vi.getTimerCount();
        anchor.dispatchEvent(new MouseEvent('mouseenter'));
        expect(vi.getTimerCount()).toBe(baseline + 1);
        hover.destroy();
        expect(vi.getTimerCount()).toBe(baseline);
        vi.advanceTimersByTime(1000);
        expect(hover.isOpen()).toBe(false);
    });

    it('follows Locale switching for the close button label', () => {
        const onClose = vi.fn();
        const popover = new Popover({ anchor, content: 'x', trigger: 'manual', closeButton: true, onClose });
        const close = popover.element.querySelector('.popover__close');
        expect(close.getAttribute('aria-label')).toBe('關閉');

        Locale.setLang('en');
        popover.open();
        expect(close.getAttribute('aria-label')).toBe('Close');
        close.click();
        expect(popover.isOpen()).toBe(false);
        expect(onClose).toHaveBeenCalledWith('close-button');
    });
});
