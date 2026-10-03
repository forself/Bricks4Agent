import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { Dropdown } from '../../ui_components/form/Dropdown/Dropdown.js';
import { MultiSelectDropdown } from '../../ui_components/form/MultiSelectDropdown/MultiSelectDropdown.js';
import { DatePicker } from '../../ui_components/form/DatePicker/DatePicker.js';
import { TimePicker } from '../../ui_components/form/TimePicker/TimePicker.js';

const items = [
    { label: 'Alpha', value: 1 },
    { label: 'Beta', value: 2 },
];

function countDocumentClickListeners(spyAdd, spyRemove) {
    const added = spyAdd.mock.calls.filter(([type]) => type === 'click').length;
    const removed = spyRemove.mock.calls.filter(([type]) => type === 'click').length;
    return added - removed;
}

describe('floating popups', () => {
    let host;

    beforeEach(() => {
        host = document.createElement('div');
        document.body.appendChild(host);
    });

    afterEach(() => {
        host.remove();
        vi.restoreAllMocks();
    });

    describe('Dropdown', () => {
        it('builds option nodes only while open and floats the menu with fixed positioning', () => {
            const dropdown = new Dropdown({ items }).mount(host);
            expect(dropdown.menu.children).toHaveLength(0);

            dropdown.open();
            expect(dropdown.menu.querySelectorAll('.dropdown__option:not(.dropdown__option--empty)').length).toBe(2);
            expect(dropdown.menu.style.position).toBe('fixed');
            expect(dropdown.menu.dataset.floating).toBe('fixed');
            expect(host.contains(dropdown.menu)).toBe(true);

            dropdown.close();
            expect(dropdown.menu.children).toHaveLength(0);
            expect(dropdown.menu.style.position).toBe('absolute');
            expect(dropdown.menu.dataset.floating).toBeUndefined();
            dropdown.destroy();
        });

        it('attaches document and viewport listeners only while open', () => {
            const docAdd = vi.spyOn(document, 'addEventListener');
            const docRemove = vi.spyOn(document, 'removeEventListener');
            const winAdd = vi.spyOn(window, 'addEventListener');

            const dropdown = new Dropdown({ items }).mount(host);
            expect(countDocumentClickListeners(docAdd, docRemove)).toBe(0);
            expect(winAdd.mock.calls.filter(([type]) => type === 'scroll')).toHaveLength(0);

            dropdown.open();
            expect(countDocumentClickListeners(docAdd, docRemove)).toBe(1);
            expect(winAdd.mock.calls.filter(([type]) => type === 'scroll')).toHaveLength(1);

            dropdown.close();
            expect(countDocumentClickListeners(docAdd, docRemove)).toBe(0);
            dropdown.destroy();
        });

        it('matches values across number and string forms', () => {
            const dropdown = new Dropdown({ items, value: '2' }).mount(host);
            expect(dropdown.display.textContent).toBe('Beta');
            dropdown.setValue('1');
            expect(dropdown.display.textContent).toBe('Alpha');
            dropdown.open();
            const selected = dropdown.menu.querySelector('.dropdown__option[data-value="1"]');
            expect(selected).not.toBeNull();
            dropdown.destroy();
        });

        it('keeps a click inside the floating menu from closing it', () => {
            const dropdown = new Dropdown({ items }).mount(host);
            dropdown.open();
            dropdown.menu.dispatchEvent(new MouseEvent('click', { bubbles: true }));
            expect(dropdown.snapshot().open).toBe(true);
            document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }));
            expect(dropdown.snapshot().open).toBe(false);
            dropdown.destroy();
        });
    });

    describe('MultiSelectDropdown', () => {
        it('floats the menu while open and restores it when closed', () => {
            const control = new MultiSelectDropdown({ items }).mount(host);
            control.open();
            expect(control._menu.style.position).toBe('fixed');
            expect(control._menu.dataset.floating).toBe('fixed');
            control.close();
            expect(control._menu.style.position).toBe('absolute');
            control.destroy();
        });
    });

    describe('DatePicker', () => {
        it('moves the calendar to document.body while open and back when closed', () => {
            const picker = new DatePicker({});
            picker.mount(host);
            picker.open();
            expect(picker.calendar.parentNode).toBe(document.body);
            expect(picker.calendar.dataset.portal).toBe('body');
            expect(picker.calendar.style.position).toBe('fixed');

            picker.close();
            expect(picker.element.contains(picker.calendar)).toBe(true);
            expect(picker.calendar.dataset.portal).toBeUndefined();
            picker.destroy();
        });

        it('removes a still-open calendar from document.body on destroy', () => {
            const picker = new DatePicker({});
            picker.mount(host);
            picker.open();
            const calendar = picker.calendar;
            picker.destroy();
            expect(document.body.contains(calendar)).toBe(false);
        });
    });

    describe('TimePicker', () => {
        it('moves the panel to document.body while open and back when closed', () => {
            const picker = new TimePicker({});
            picker.mount(host);
            picker.open();
            expect(picker.panel.parentNode).toBe(document.body);
            expect(picker.panel.style.position).toBe('fixed');
            picker.close();
            expect(picker.container.contains(picker.panel)).toBe(true);
            picker.destroy();
        });
    });
});
