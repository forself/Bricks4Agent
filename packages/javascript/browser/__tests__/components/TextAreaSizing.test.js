import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import {
    TextArea,
    TEXTAREA_SIZING_STORAGE_KEY,
    preferredTextAreaSizing,
} from '../../ui_components/form/TextArea/TextArea.js';

describe('TextArea sizing', () => {
    let host;

    beforeEach(() => {
        localStorage.removeItem(TEXTAREA_SIZING_STORAGE_KEY);
        host = document.createElement('div');
        document.body.appendChild(host);
    });

    afterEach(() => {
        host.remove();
        localStorage.removeItem(TEXTAREA_SIZING_STORAGE_KEY);
    });

    it('starts as a fixed five-row box with the in-box toggle', () => {
        const area = new TextArea({}).mount(host);
        expect(area.getSizing()).toBe('fixed');
        expect(area.textarea.rows).toBe(5);
        expect(area.textarea.dataset.sizing).toBe('fixed');
        expect(area.toggle).not.toBeNull();
        expect(area.toggle.getAttribute('aria-pressed')).toBe('false');
        expect(area.toggle.title).toContain('5');
        area.destroy();
    });

    it('honours explicit rows, sizing and the legacy autoResize flag', () => {
        const tall = new TextArea({ rows: 8 }).mount(host);
        expect(tall.textarea.rows).toBe(8);
        const auto = new TextArea({ sizing: 'auto' }).mount(host);
        expect(auto.getSizing()).toBe('auto');
        const legacy = new TextArea({ autoResize: true }).mount(host);
        expect(legacy.getSizing()).toBe('auto');
        const noToggle = new TextArea({ sizingToggle: false }).mount(host);
        expect(noToggle.toggle).toBeNull();
        [tall, auto, legacy, noToggle].forEach(area => area.destroy());
    });

    it('grows with content in auto mode and reports the change', () => {
        const onSizingChange = vi.fn();
        const area = new TextArea({ onSizingChange }).mount(host);
        Object.defineProperty(area.textarea, 'scrollHeight', { configurable: true, value: 120 });
        area.setSizing('auto');
        area.setValue('content');
        expect(area.textarea.style.height).toBe('120px');
        expect(onSizingChange).toHaveBeenLastCalledWith('auto');
        area.destroy();
    });

    it('remembers the toggled mode as the default for later instances', () => {
        const first = new TextArea({}).mount(host);
        first.toggle.click();
        expect(first.getSizing()).toBe('auto');
        expect(preferredTextAreaSizing()).toBe('auto');

        const second = new TextArea({}).mount(host);
        expect(second.getSizing()).toBe('auto');
        [first, second].forEach(area => area.destroy());
    });

    it('hides the toggle while disabled or readonly', () => {
        const area = new TextArea({}).mount(host);
        area.setReadonly(true);
        expect(area.toggle.style.display).toBe('none');
        area.setReadonly(false);
        expect(area.toggle.style.display).toBe('');
        area.destroy();
    });
});
