import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import Locale from '../../ui_components/i18n/index.js';
import { DateTimeRangePicker } from '../../ui_components/input/DateTimeRangePicker/DateTimeRangePicker.js';
import * as DateTimeRangePickerModule from '../../ui_components/input/DateTimeRangePicker/index.js';
import { FIELD_ERROR_CONTRACT } from '../../ui_components/utils/field-error.js';

let host;

beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 5, 15, 10, 0, 0));
    Locale.setLang('zh-TW');
    host = document.createElement('div');
    document.body.appendChild(host);
});

afterEach(() => {
    host.remove();
    document.body.innerHTML = '';
    Locale.setLang('zh-TW');
    vi.useRealTimers();
    vi.restoreAllMocks();
});

const create = (options = {}) => new DateTimeRangePicker(options).mount(host);
const iso = (date) => (date
    ? `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
    : null);
const dayButton = (picker, day) => picker.calendar.querySelector(`.dp-day[data-day="${day}"]`);
const hourItem = (picker, hour) => picker.hourColumn.items.find((item) => Number(item.dataset.value) === hour);
const minuteItem = (picker, minute) => picker.minuteColumn.items.find((item) => Number(item.dataset.value) === minute);
const disabledHours = (picker) => picker.hourColumn.items
    .filter((item) => item.getAttribute('aria-disabled') === 'true')
    .map((item) => Number(item.dataset.value));
const disabledMinutes = (picker) => picker.minuteColumn.items
    .filter((item) => item.getAttribute('aria-disabled') === 'true')
    .map((item) => Number(item.dataset.value));
const range = (from, to) => Array.from({ length: to - from + 1 }, (_, index) => from + index);
const press = (element, key, extra = {}) => element.dispatchEvent(
    new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...extra })
);
const messagesIn = (element) => [...element.querySelectorAll('.b4a-field-error')];
const pickTime = (picker, hour, minute) => {
    picker.inputWrapper.click();
    hourItem(picker, hour).click();
    minuteItem(picker, minute).click();
    picker.confirmButton.click();
};
const pickDay = (picker, day) => {
    picker.inputWrapper.click();
    dayButton(picker, day).click();
};
const triggersOf = (picker) => [picker.startDatePicker, picker.startTimePicker, picker.endDatePicker, picker.endTimePicker]
    .map((inner) => inner.inputWrapper);

function isHidden(element, root) {
    for (let node = element; node && node !== root.parentNode; node = node.parentElement) {
        if (node.style?.display === 'none' || node.style?.visibility === 'hidden') return true;
    }
    return false;
}

function tabbables(root) {
    return [...root.querySelectorAll('button, select, input, textarea, a[href], [tabindex]')]
        .filter((element) => element.tabIndex >= 0 && !element.disabled && !isHidden(element, root));
}

function leakedListeners(addSpy, removeSpy) {
    const capture = (options) => (typeof options === 'boolean' ? options : Boolean(options?.capture));
    const removed = removeSpy.mock.calls.map(([type, handler, options]) => ({ type, handler, capture: capture(options) }));
    return addSpy.mock.calls
        .map(([type, handler, options]) => ({ type, handler, capture: capture(options) }))
        .filter((added) => {
            const index = removed.findIndex((entry) => entry.type === added.type
                && entry.handler === added.handler && entry.capture === added.capture);
            if (index === -1) return true;
            removed.splice(index, 1);
            return false;
        })
        .map((entry) => entry.type);
}

describe('DateTimeRangePicker basics', () => {
    it('exports the class as named and default export', () => {
        expect(DateTimeRangePickerModule.DateTimeRangePicker).toBe(DateTimeRangePicker);
        expect(DateTimeRangePickerModule.default).toBe(DateTimeRangePicker);
    });

    it('renders a named group with the four parts reachable by keyboard in order', () => {
        const picker = create();
        const root = picker.element;
        const triggers = triggersOf(picker);
        expect(root.getAttribute('role')).toBe('group');
        expect(root.getAttribute('aria-label')).toBe('日期時間範圍');
        expect(triggers.map((trigger) => trigger.getAttribute('aria-label'))).toEqual(['開始日期', '開始時間', '結束日期', '結束時間']);
        expect(picker.startDatePicker.display.textContent).toBe('開始日期');
        expect(picker.endTimePicker.display.textContent).toBe('結束時間');
        triggers.forEach((trigger) => {
            expect(trigger.tabIndex).toBe(0);
            expect(trigger.getAttribute('role')).toBe('combobox');
        });
        expect(tabbables(root)).toEqual(triggers);
        picker.setValue({ start: '2026-03-10T09:00', end: null });
        expect(tabbables(root)).toEqual([...triggers, root.querySelector('.datetime-range-picker__clear')]);
        // 分鐘欄預設間隔 15（與 DateTimeInput 相同）
        expect(picker.startTimePicker.minuteColumn.items).toHaveLength(4);
        picker.destroy();
    });

    it('accepts a custom separator, ariaLabel, width and clearable: false', () => {
        const picker = create({
            separator: '→',
            ariaLabel: '系統維護時段',
            width: 640,
            clearable: false,
            value: { start: '2026-03-10T09:00', end: '2026-03-10T10:00' }
        });
        expect(picker.element.getAttribute('aria-label')).toBe('系統維護時段');
        expect(picker.element.querySelector('.datetime-range-picker__separator').textContent).toBe('→');
        expect(picker.element.style.width).toBe('640px');
        expect(picker.element.querySelector('.datetime-range-picker__clear')).toBeNull();
        picker.destroy();
    });

    it('switches strings with the active locale', () => {
        Locale.setLang('en');
        const picker = create({ label: 'Maintenance window' });
        expect(picker.element.getAttribute('aria-labelledby')).toBe(picker.element.querySelector('.datetime-range-picker__label').id);
        expect(triggersOf(picker).map((trigger) => trigger.getAttribute('aria-label'))).toEqual(['Start date', 'Start time', 'End date', 'End time']);
        expect(picker.element.querySelector('.datetime-range-picker__separator').textContent).toBe('to');
        picker.destroy();
    });

    it('shows the Republic-of-China calendar with format: taiwan', () => {
        const picker = create({ format: 'taiwan', value: { start: '2026-03-10T09:00', end: '2026-03-11T18:00' } });
        expect(picker.startDatePicker.display.textContent).toBe('115/03/10');
        expect(picker.endTimePicker.display.textContent).toBe('18:00');
        picker.destroy();
    });
});

describe('DateTimeRangePicker value', () => {
    it('round-trips values and hands out copies', () => {
        const picker = create({ value: { start: '2026-03-10T09:00', end: '2026-03-12T17:30' } });
        expect(picker.getValue()).toEqual({ start: '2026-03-10T09:00', end: '2026-03-12T17:30' });
        expect(picker.startDatePicker.getFormattedValue()).toBe('2026/03/10');
        expect(picker.startTimePicker.getValue()).toBe('09:00');
        expect(picker.endDatePicker.getFormattedValue()).toBe('2026/03/12');
        expect(picker.endTimePicker.getValue()).toBe('17:30');
        const copy = picker.getValue();
        copy.start = null;
        expect(picker.getValue().start).toBe('2026-03-10T09:00');
        picker.setValue({ start: '2026-04-01T08:00' });
        expect(picker.getValue()).toEqual({ start: '2026-04-01T08:00', end: null });
        picker.setValue(null);
        expect(picker.getValue()).toEqual({ start: null, end: null });
        expect(picker.startTimePicker.getValue()).toBe('');
        picker.destroy();
    });

    it('rejects malformed values with console.warn and keeps the value', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const picker = create({ value: { start: '2026-03-10T09:00', end: '2026-03-10T10:00' } });
        const bad = [
            { start: '2026-03-10 09:00' },
            { start: '2026-03-10T9:00' },
            { start: '2026-02-30T09:00' },
            { start: '2026-03-10T24:00' },
            { end: '2026-03-10T10:00:00' },
            { end: '2026-03-10T10:00Z' },
            { end: new Date(2026, 2, 10, 10, 0) },
            '2026-03-10T09:00',
            42
        ];
        bad.forEach((value) => picker.setValue(value));
        expect(picker.getValue()).toEqual({ start: '2026-03-10T09:00', end: '2026-03-10T10:00' });
        expect(warn).toHaveBeenCalledTimes(bad.length);
        picker.destroy();
    });

    it('rejects an end that is not after the start instead of swapping', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const picker = create({ value: { start: '2026-03-10T09:00', end: '2026-03-10T10:00' } });
        picker.setValue({ start: '2026-03-10T10:00', end: '2026-03-10T09:00' });
        picker.setValue({ start: '2026-03-10T10:00', end: '2026-03-10T10:00' });
        expect(picker.getValue()).toEqual({ start: '2026-03-10T09:00', end: '2026-03-10T10:00' });
        expect(warn).toHaveBeenCalledTimes(2);
        expect(warn.mock.calls[0][0]).toContain('not after start');
        picker.destroy();
    });

    it('accepts min / max as Date objects and ignores invalid bounds', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const picker = create({ min: new Date(2026, 2, 10, 8, 0), max: '2026-03-12T18:00' });
        expect(iso(picker.startDatePicker.minDate)).toBe('2026-03-10');
        expect(iso(picker.endDatePicker.maxDate)).toBe('2026-03-12');
        const loose = create({ min: '2026-03-10', maxSpanMinutes: -5, minuteStep: 0 });
        expect(loose.startDatePicker.minDate).toBeNull();
        expect(loose.startTimePicker.minuteColumn.items).toHaveLength(4);
        expect(warn).toHaveBeenCalledTimes(3);
        picker.destroy();
        loose.destroy();
    });
});

describe('DateTimeRangePicker constraints', () => {
    it('limits the end date and the end time on the same day by the start', () => {
        const onChange = vi.fn();
        const picker = create({ onChange, value: { start: '2026-03-10T22:00', end: null } });
        expect(iso(picker.endDatePicker.minDate)).toBe('2026-03-10');

        pickDay(picker.endDatePicker, 10);
        expect(picker.getValue()).toEqual({ start: '2026-03-10T22:00', end: null });
        expect(onChange).not.toHaveBeenCalled();

        const endTime = picker.endTimePicker;
        endTime.inputWrapper.click();
        expect(disabledHours(endTime)).toEqual(range(0, 21));
        hourItem(endTime, 22).click();
        expect(disabledMinutes(endTime)).toEqual([0]);
        minuteItem(endTime, 30).click();
        endTime.confirmButton.click();
        expect(picker.getValue()).toEqual({ start: '2026-03-10T22:00', end: '2026-03-10T22:30' });
        expect(onChange).toHaveBeenCalledTimes(1);

        // 反向：開始日期最晚到結束日期，當天開始時間要早於 22:30
        expect(iso(picker.startDatePicker.maxDate)).toBe('2026-03-10');
        picker.startTimePicker.inputWrapper.click();
        expect(disabledHours(picker.startTimePicker)).toEqual([23]);
        picker.destroy();
    });

    it('takes an already chosen time into account when limiting dates', () => {
        const picker = create({ value: { start: '2026-03-10T22:00', end: null } });
        pickTime(picker.endTimePicker, 8, 0);
        expect(picker.getValue().end).toBeNull();
        // 08:00 在 03-10 會早於 22:00，所以結束日期最早是 03-11
        expect(iso(picker.endDatePicker.minDate)).toBe('2026-03-11');
        picker.endDatePicker.inputWrapper.click();
        expect(dayButton(picker.endDatePicker, 10).dataset.disabled).toBe('true');
        dayButton(picker.endDatePicker, 11).click();
        expect(picker.getValue()).toEqual({ start: '2026-03-10T22:00', end: '2026-03-11T08:00' });
        picker.destroy();
    });

    it('applies maxSpanMinutes across midnight', () => {
        const picker = create({ maxSpanMinutes: 180, value: { start: '2026-03-10T22:00', end: null } });
        expect(iso(picker.endDatePicker.minDate)).toBe('2026-03-10');
        expect(iso(picker.endDatePicker.maxDate)).toBe('2026-03-11');
        pickDay(picker.endDatePicker, 11);
        picker.endTimePicker.inputWrapper.click();
        // 最晚 03-11 01:00
        expect(disabledHours(picker.endTimePicker)).toEqual(range(2, 23));
        hourItem(picker.endTimePicker, 1).click();
        expect(disabledMinutes(picker.endTimePicker)).toEqual([15, 30, 45]);
        picker.destroy();
    });

    it('applies min / max to dates and to times on the boundary days', () => {
        const picker = create({ min: '2026-03-10T08:00', max: '2026-03-12T18:00' });
        expect(iso(picker.startDatePicker.minDate)).toBe('2026-03-10');
        expect(iso(picker.startDatePicker.maxDate)).toBe('2026-03-12');
        pickDay(picker.startDatePicker, 10);
        picker.startTimePicker.inputWrapper.click();
        expect(disabledHours(picker.startTimePicker)).toEqual(range(0, 7));
        picker.startTimePicker.close();
        pickDay(picker.endDatePicker, 12);
        picker.endTimePicker.inputWrapper.click();
        expect(disabledHours(picker.endTimePicker)).toEqual(range(19, 23));
        hourItem(picker.endTimePicker, 18).click();
        expect(disabledMinutes(picker.endTimePicker)).toEqual([15, 30, 45]);
        picker.destroy();
    });

    it('blocks confirming a disallowed draft time and explains why', () => {
        const onChange = vi.fn();
        const picker = create({ onChange, value: { start: '2026-03-10T09:00', end: '2026-03-10T12:00' } });
        const endTime = picker.endTimePicker;
        endTime.inputWrapper.click();
        hourItem(endTime, 9).click();
        expect(endTime.snapshot().draftMinute).toBe(0);
        endTime.confirmButton.click();
        const hint = endTime.panel.querySelector('.b4a-range-hint');
        expect(endTime.isOpen).toBe(true);
        expect(hint.textContent).toBe('結束必須晚於開始');
        expect(picker.getValue().end).toBe('2026-03-10T12:00');
        expect(onChange).not.toHaveBeenCalled();
        picker.destroy();
    });
});

describe('DateTimeRangePicker partial ends and validation', () => {
    it('keeps an end null until both its date and time are chosen', () => {
        const onChange = vi.fn();
        const picker = create({ onChange });
        pickDay(picker.startDatePicker, 20);
        expect(picker.getValue()).toEqual({ start: null, end: null });
        expect(onChange).not.toHaveBeenCalled();
        expect(picker.isValid()).toBe(false);
        expect(picker.getValidationError()).toBe('請選擇開始時間');
        // 結束日曆跟著開始日期跳到同一個月
        expect(picker.endDatePicker.snapshot().currentMonth).toBe(5);

        pickTime(picker.startTimePicker, 9, 30);
        expect(picker.getValue()).toEqual({ start: '2026-06-20T09:30', end: null });
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(picker.isValid()).toBe(true);

        pickTime(picker.endTimePicker, 18, 0);
        expect(picker.getValidationError()).toBe('請選擇結束日期');
        picker.destroy();
    });

    it('reports the reason in zh-TW and en', () => {
        const picker = create({ required: true, min: '2026-03-10T08:00', max: '2026-03-12T18:00', maxSpanMinutes: 180 });
        expect(picker.getValidationError()).toBe('請選擇日期時間範圍');
        picker.setValue({ start: '2026-03-10T09:00', end: null });
        expect(picker.getValidationError()).toBe('請選擇結束日期與時間');
        picker.setValue({ start: null, end: '2026-03-10T09:00' });
        expect(picker.getValidationError()).toBe('請選擇開始日期與時間');
        picker.setValue({ start: '2026-03-10T07:00', end: '2026-03-10T09:00' });
        expect(picker.getValidationError()).toBe('不可早於 2026/03/10 08:00');
        picker.setValue({ start: '2026-03-12T17:00', end: '2026-03-12T19:00' });
        expect(picker.getValidationError()).toBe('不可晚於 2026/03/12 18:00');
        picker.setValue({ start: '2026-03-10T09:00', end: '2026-03-10T13:00' });
        expect(picker.getValidationError()).toBe('範圍不可超過 3 小時');
        picker.setValue({ start: '2026-03-10T09:00', end: '2026-03-10T12:00' });
        expect(picker.getValidationError()).toBe('');
        expect(picker.isValid()).toBe(true);

        Locale.setLang('en');
        picker.setValue({ start: '2026-03-10T09:00', end: '2026-03-10T13:00' });
        expect(picker.getValidationError()).toBe('The range cannot exceed 3 h');
        picker.setValue({ start: '2026-03-10T07:00', end: '2026-03-10T09:00' });
        expect(picker.getValidationError()).toBe('Cannot be earlier than 2026/03/10 08:00');
        picker.clear();
        expect(picker.getValidationError()).toBe('Please select a date and time range');
        picker.destroy();
    });

    it('formats bounds in the Republic-of-China calendar with format: taiwan', () => {
        const picker = create({ format: 'taiwan', max: '2026-03-12T18:00', value: { start: '2026-03-13T09:00', end: null } });
        expect(picker.getValidationError()).toBe('不可晚於 115/03/12 18:00');
        picker.destroy();
    });
});

describe('DateTimeRangePicker onChange and clear', () => {
    it('fires only for committed value changes, never for setValue', () => {
        const onChange = vi.fn();
        const picker = create({ onChange, value: { start: '2026-03-10T09:00', end: '2026-03-10T10:00' } });
        picker.setValue({ start: '2026-03-11T09:00', end: '2026-03-11T10:00' });
        expect(onChange).not.toHaveBeenCalled();
        pickDay(picker.endDatePicker, 12);
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(onChange).toHaveBeenLastCalledWith({ start: '2026-03-11T09:00', end: '2026-03-12T10:00' });
        picker.clear();
        picker.clear();
        expect(onChange).toHaveBeenCalledTimes(2);
        expect(onChange).toHaveBeenLastCalledWith({ start: null, end: null });
        picker.destroy();
    });

    it('clear() also empties half-filled parts without firing onChange', () => {
        const onChange = vi.fn();
        const picker = create({ onChange });
        pickDay(picker.startDatePicker, 20);
        const clearButton = picker.element.querySelector('.datetime-range-picker__clear');
        expect(clearButton.style.visibility).toBe('visible');
        clearButton.click();
        expect(picker.startDatePicker.getValue()).toBeNull();
        expect(onChange).not.toHaveBeenCalled();
        expect(clearButton.style.visibility).toBe('hidden');
        expect(document.activeElement).toBe(picker.startDatePicker.inputWrapper);
        picker.destroy();
    });
});

describe('DateTimeRangePicker field-error contract', () => {
    it('marks all four parts and shows one message below the range row', () => {
        const picker = create();
        const triggers = triggersOf(picker);
        expect(picker[FIELD_ERROR_CONTRACT]).toBe(true);
        expect(picker.setError('請選擇日期時間範圍')).toBe(picker);
        const messages = messagesIn(host);
        expect(messages).toHaveLength(1);
        expect(picker.element.querySelector('.datetime-range-picker__row').nextElementSibling).toBe(messages[0]);
        triggers.forEach((trigger) => {
            expect(trigger.getAttribute('aria-invalid')).toBe('true');
            expect(trigger.getAttribute('aria-describedby')).toContain(messages[0].id);
        });
        picker.setError('請選擇日期時間範圍', { display: false });
        expect(messagesIn(host)).toHaveLength(0);
        expect(picker.clearError()).toBe(picker);
        triggers.forEach((trigger) => expect(trigger.hasAttribute('aria-invalid')).toBe(false));
        picker.setError('x');
        picker.destroy();
        expect(messagesIn(document.body)).toHaveLength(0);
    });
});

describe('DateTimeRangePicker keyboard', () => {
    it('picks a whole end with the keyboard and returns focus to each trigger', () => {
        const onChange = vi.fn();
        const picker = create({ onChange, value: { start: '2026-03-10T09:00', end: null } });
        const dateTrigger = picker.endDatePicker.inputWrapper;
        dateTrigger.focus();
        press(dateTrigger, 'Enter');
        expect(document.activeElement).toBe(dayButton(picker.endDatePicker, 10));
        press(document.activeElement, 'ArrowRight');
        press(document.activeElement, 'Enter');
        expect(document.activeElement).toBe(dateTrigger);

        const timeTrigger = picker.endTimePicker.inputWrapper;
        timeTrigger.focus();
        press(timeTrigger, 'ArrowDown');
        const hours = picker.endTimePicker.hourColumn.scrollContainer;
        expect(document.activeElement).toBe(hours);
        press(hours, 'ArrowDown');
        // 隔天沒有下限 → 第一個可選的小時是 0
        expect(picker.endTimePicker.snapshot().draftHour).toBe(0);
        press(hours, 'ArrowRight');
        press(document.activeElement, 'ArrowDown');
        press(document.activeElement, 'Enter');
        expect(picker.getValue()).toEqual({ start: '2026-03-10T09:00', end: '2026-03-11T00:00' });
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(document.activeElement).toBe(timeTrigger);
        picker.destroy();
    });
});

describe('DateTimeRangePicker disabled', () => {
    it('setDisabled blocks all four parts and the clear button', () => {
        const picker = create({ value: { start: '2026-03-10T09:00', end: '2026-03-10T10:00' } });
        picker.setDisabled(true);
        expect(picker.element.getAttribute('aria-disabled')).toBe('true');
        triggersOf(picker).forEach((trigger) => {
            expect(trigger.tabIndex).toBe(-1);
            expect(trigger.getAttribute('aria-disabled')).toBe('true');
        });
        expect(tabbables(picker.element)).toEqual([]);
        press(picker.startTimePicker.inputWrapper, 'Enter');
        expect(picker.startTimePicker.isOpen).toBe(false);
        picker.setDisabled(false);
        expect(tabbables(picker.element)).toHaveLength(5);
        picker.destroy();
    });
});

describe('DateTimeRangePicker destroy', () => {
    it('removes its DOM, every portaled calendar and panel, and every listener', () => {
        const docAdd = vi.spyOn(document, 'addEventListener');
        const docRemove = vi.spyOn(document, 'removeEventListener');
        const winAdd = vi.spyOn(window, 'addEventListener');
        const winRemove = vi.spyOn(window, 'removeEventListener');

        const picker = create({ value: { start: '2026-03-10T09:00', end: '2026-03-10T10:00' } });
        expect(docAdd).not.toHaveBeenCalled();
        picker.startDatePicker.inputWrapper.click();
        picker.endTimePicker.inputWrapper.click();
        const floating = [picker.startDatePicker.calendar, picker.endTimePicker.panel];
        expect(picker.endTimePicker.panel.parentNode).toBe(document.body);
        picker.setError('x');

        picker.destroy();
        expect(host.children).toHaveLength(0);
        floating.forEach((element) => expect(document.body.contains(element)).toBe(false));
        expect(document.body.querySelectorAll('.datepicker__calendar, .timepicker__panel, .b4a-field-error')).toHaveLength(0);
        expect(leakedListeners(docAdd, docRemove)).toEqual([]);
        expect(leakedListeners(winAdd, winRemove)).toEqual([]);
        expect(() => {
            picker.setValue({ start: '2026-01-01T00:00', end: null });
            picker.clear();
            picker.setDisabled(true);
            picker.setError('x');
            picker.clearError();
            picker.getValue();
            picker.destroy();
        }).not.toThrow();
    });
});
