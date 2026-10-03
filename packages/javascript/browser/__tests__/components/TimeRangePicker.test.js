import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import Locale from '../../ui_components/i18n/index.js';
import { TimeRangePicker } from '../../ui_components/form/TimeRangePicker/TimeRangePicker.js';
import * as TimeRangePickerModule from '../../ui_components/form/TimeRangePicker/index.js';
import { FIELD_ERROR_CONTRACT } from '../../ui_components/utils/field-error.js';

let host;

beforeEach(() => {
    Locale.setLang('zh-TW');
    host = document.createElement('div');
    document.body.appendChild(host);
});

afterEach(() => {
    host.remove();
    document.body.innerHTML = '';
    Locale.setLang('zh-TW');
    vi.restoreAllMocks();
});

const create = (options = {}) => new TimeRangePicker(options).mount(host);
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
const flush = () => new Promise((resolve) => setTimeout(resolve, 0));
const messagesIn = (element) => [...element.querySelectorAll('.b4a-field-error')];
const hintOf = (picker) => picker.panel.querySelector('.b4a-range-hint');

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

describe('TimeRangePicker basics', () => {
    it('exports the class as named and default export', () => {
        expect(TimeRangePickerModule.TimeRangePicker).toBe(TimeRangePicker);
        expect(TimeRangePickerModule.default).toBe(TimeRangePicker);
    });

    it('renders a named group with both ends reachable by keyboard in order', () => {
        const picker = create();
        const root = picker.element;
        const start = picker.startPicker.inputWrapper;
        const end = picker.endPicker.inputWrapper;
        expect(root.getAttribute('role')).toBe('group');
        expect(root.getAttribute('aria-label')).toBe('時間範圍');
        expect(picker.getValue()).toEqual({ start: null, end: null });
        expect(picker.startPicker.display.textContent).toBe('開始時間');
        expect(picker.endPicker.display.textContent).toBe('結束時間');
        expect(root.querySelector('.time-range-picker__separator').textContent).toBe('至');
        [[start, '開始時間'], [end, '結束時間']].forEach(([trigger, name]) => {
            expect(trigger.tabIndex).toBe(0);
            expect(trigger.getAttribute('role')).toBe('combobox');
            expect(trigger.getAttribute('aria-haspopup')).toBe('dialog');
            expect(trigger.getAttribute('aria-expanded')).toBe('false');
            expect(trigger.getAttribute('aria-label')).toBe(name);
        });
        expect(tabbables(root)).toEqual([start, end]);
        picker.setValue({ start: '09:00', end: '10:00' });
        expect(tabbables(root)).toEqual([start, end, root.querySelector('.time-range-picker__clear')]);
        picker.destroy();
    });

    it('links a visible label and marks required ends', () => {
        const picker = create({ label: '營業時間', required: true });
        const labelEl = picker.element.querySelector('.time-range-picker__label');
        expect(picker.element.getAttribute('aria-labelledby')).toBe(labelEl.id);
        expect(labelEl.textContent).toBe('營業時間*');
        expect(picker.startPicker.inputWrapper.getAttribute('aria-required')).toBe('true');
        picker.destroy();
    });

    it('accepts custom texts, ariaLabel, width and clearable: false', () => {
        const picker = create({
            startPlaceholder: '上班',
            endPlaceholder: '下班',
            separator: '～',
            ariaLabel: '排班時段',
            width: '320px',
            clearable: false,
            value: { start: '09:00', end: '18:00' }
        });
        expect(picker.element.getAttribute('aria-label')).toBe('排班時段');
        expect(picker.startPicker.inputWrapper.getAttribute('aria-label')).toBe('上班');
        expect(picker.endPicker.panel.getAttribute('aria-label')).toBe('下班');
        expect(picker.element.querySelector('.time-range-picker__separator').textContent).toBe('～');
        expect(picker.element.style.width).toBe('320px');
        expect(picker.element.querySelector('.time-range-picker__clear')).toBeNull();
        picker.destroy();
    });

    it('switches strings with the active locale', () => {
        Locale.setLang('en');
        const picker = create();
        expect(picker.element.getAttribute('aria-label')).toBe('Time range');
        expect(picker.startPicker.display.textContent).toBe('Start time');
        expect(picker.element.querySelector('.time-range-picker__separator').textContent).toBe('to');
        picker.destroy();
    });

    it('passes minuteStep to both TimePickers and rejects a step that would hang TimePicker', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const stepped = create({ minuteStep: 15 });
        expect(stepped.startPicker.minuteColumn.items.map((item) => item.dataset.value)).toEqual(['0', '15', '30', '45']);
        expect(stepped.endPicker.minuteColumn.items).toHaveLength(4);
        const guarded = create({ minuteStep: 0 });
        expect(guarded.startPicker.minuteColumn.items).toHaveLength(60);
        expect(warn).toHaveBeenCalledTimes(1);
        stepped.destroy();
        guarded.destroy();
    });
});

describe('TimeRangePicker value', () => {
    it('round-trips values and hands out copies', () => {
        const picker = create({ value: { start: '09:00', end: '17:30' } });
        expect(picker.getValue()).toEqual({ start: '09:00', end: '17:30' });
        expect(picker.startPicker.getValue()).toBe('09:00');
        expect(picker.endPicker.display.textContent).toBe('17:30');
        const copy = picker.getValue();
        copy.end = '23:00';
        expect(picker.getValue().end).toBe('17:30');
        expect(picker.setValue({ start: '08:00' })).toBe(picker);
        expect(picker.getValue()).toEqual({ start: '08:00', end: null });
        picker.setValue('');
        expect(picker.getValue()).toEqual({ start: null, end: null });
        expect(picker.startPicker.getValue()).toBe('');
        picker.destroy();
    });

    it('rejects malformed values with console.warn and keeps the value', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const picker = create({ value: { start: '09:00', end: '17:30' } });
        const bad = [
            { start: '9:00', end: null },
            { start: '24:00' },
            { start: '12:60' },
            { end: '12:5' },
            { end: 1730 },
            { start: '09:00:00' },
            '09:00'
        ];
        bad.forEach((value) => picker.setValue(value));
        expect(picker.getValue()).toEqual({ start: '09:00', end: '17:30' });
        expect(warn).toHaveBeenCalledTimes(bad.length);
        picker.destroy();
    });

    it('rejects an end that is not after the start instead of swapping', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const picker = create({ value: { start: '09:00', end: '10:00' } });
        picker.setValue({ start: '10:00', end: '09:00' });
        picker.setValue({ start: '10:00', end: '10:00' });
        expect(picker.getValue()).toEqual({ start: '09:00', end: '10:00' });
        expect(warn).toHaveBeenCalledTimes(2);
        expect(warn.mock.calls[0][0]).toContain('not after start');
        picker.destroy();
    });
});

describe('TimeRangePicker constraints', () => {
    it('disables times in the other end and blocks picking them', () => {
        const onChange = vi.fn();
        const picker = create({ minuteStep: 15, onChange, value: { start: '09:30', end: null } });
        const end = picker.endPicker;
        end.inputWrapper.click();
        expect(end.isOpen).toBe(true);
        // 09:45 仍晚於 09:30，所以 9 點可選；0–8 點全部不可選
        expect(disabledHours(end)).toEqual(range(0, 8));

        hourItem(end, 8).click();
        expect(end.snapshot().draftHour).toBeNull();
        hourItem(end, 9).click();
        expect(end.snapshot().draftHour).toBe(9);
        expect(disabledMinutes(end)).toEqual([0, 15, 30]);
        minuteItem(end, 15).click();
        expect(end.snapshot().draftMinute).toBeNull();
        minuteItem(end, 45).click();
        end.confirmButton.click();

        expect(picker.getValue()).toEqual({ start: '09:30', end: '09:45' });
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(onChange).toHaveBeenCalledWith({ start: '09:30', end: '09:45' });

        // 反向約束：開始端只能早於 09:45
        picker.startPicker.inputWrapper.click();
        expect(disabledHours(picker.startPicker)).toEqual(range(10, 23));
        picker.destroy();
    });

    it('applies minimum and maximum durations to the other end', () => {
        const picker = create({ minuteStep: 30, minDurationMinutes: 30, maxDurationMinutes: 120, value: { start: '09:00', end: null } });
        const end = picker.endPicker;
        end.inputWrapper.click();
        // 允許 09:30 ~ 11:00
        expect(disabledHours(end)).toEqual([...range(0, 8), ...range(12, 23)]);
        hourItem(end, 11).click();
        expect(disabledMinutes(end)).toEqual([30]);
        hourItem(end, 9).click();
        expect(disabledMinutes(end)).toEqual([0]);
        picker.destroy();
    });

    it('blocks confirming a disallowed draft and explains why inside the panel', () => {
        const onChange = vi.fn();
        const picker = create({ minuteStep: 15, onChange, value: { start: '09:30', end: '10:00' } });
        const end = picker.endPicker;
        end.inputWrapper.click();
        hourItem(end, 9).click();
        // 草稿 09:00 不晚於 09:30
        expect(end.snapshot().draftHour).toBe(9);
        expect(end.snapshot().draftMinute).toBe(0);
        end.confirmButton.click();
        expect(end.isOpen).toBe(true);
        expect(picker.getValue()).toEqual({ start: '09:30', end: '10:00' });
        expect(onChange).not.toHaveBeenCalled();
        const hint = hintOf(end);
        expect(hint.getAttribute('role')).toBe('alert');
        expect(hint.style.display).toBe('block');
        expect(hint.textContent).toBe('結束時間必須晚於開始時間');

        minuteItem(end, 45).click();
        expect(hint.style.display).toBe('none');
        end.confirmButton.click();
        expect(end.isOpen).toBe(false);
        expect(picker.getValue()).toEqual({ start: '09:30', end: '09:45' });
        expect(onChange).toHaveBeenCalledTimes(1);
        picker.destroy();
    });

    it('supports overnight ranges when allowOvernight is true', () => {
        const picker = create({ allowOvernight: true, minuteStep: 30, maxDurationMinutes: 480, value: { start: '22:00', end: null } });
        const end = picker.endPicker;
        end.inputWrapper.click();
        // 22:00 起最多 8 小時：22:30 ~ 06:00
        expect(disabledHours(end)).toEqual(range(7, 21));
        hourItem(end, 6).click();
        expect(disabledMinutes(end)).toEqual([30]);
        minuteItem(end, 0).click();
        end.confirmButton.click();
        expect(picker.getValue()).toEqual({ start: '22:00', end: '06:00' });
        expect(picker.isValid()).toBe(true);
        const nextDay = picker.element.querySelector('.time-range-picker__next-day');
        expect(nextDay.style.display).toBe('');
        expect(nextDay.textContent).toBe('隔日');
        expect(end.inputWrapper.getAttribute('aria-label')).toBe('結束時間（隔日）');

        picker.setValue({ start: '08:00', end: '12:00' });
        expect(nextDay.style.display).toBe('none');
        expect(end.inputWrapper.getAttribute('aria-label')).toBe('結束時間');
        picker.destroy();
    });

    it('rejects an equal start and end even when overnight is allowed', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const picker = create({ allowOvernight: true, value: { start: '22:00', end: '06:00' } });
        picker.setValue({ start: '07:00', end: '07:00' });
        expect(picker.getValue()).toEqual({ start: '22:00', end: '06:00' });
        expect(warn.mock.calls[0][0]).toContain('equals start');
        picker.endPicker.inputWrapper.click();
        expect(disabledHours(picker.endPicker)).toEqual([]);
        hourItem(picker.endPicker, 22).click();
        expect(disabledMinutes(picker.endPicker)).toEqual([0]);
        picker.destroy();
    });
});

describe('TimeRangePicker validation', () => {
    it('reports the reason in zh-TW and en', () => {
        const picker = create({ required: true, minDurationMinutes: 30, maxDurationMinutes: 120 });
        expect(picker.isValid()).toBe(false);
        expect(picker.getValidationError()).toBe('請選擇時間範圍');
        picker.setValue({ start: '09:00', end: null });
        expect(picker.getValidationError()).toBe('請選擇結束時間');
        picker.setValue({ start: null, end: '09:00' });
        expect(picker.getValidationError()).toBe('請選擇開始時間');
        // setValue 保留違反時間長度的既有資料，由驗證回報
        picker.setValue({ start: '09:00', end: '09:10' });
        expect(picker.getValue()).toEqual({ start: '09:00', end: '09:10' });
        expect(picker.getValidationError()).toBe('時間長度不可少於 30 分鐘');
        picker.setValue({ start: '09:00', end: '12:30' });
        expect(picker.getValidationError()).toBe('時間長度不可超過 2 小時');
        picker.setValue({ start: '09:00', end: '10:30' });
        expect(picker.getValidationError()).toBe('');
        expect(picker.isValid()).toBe(true);

        Locale.setLang('en');
        picker.setValue({ start: '09:00', end: '12:30' });
        expect(picker.getValidationError()).toBe('The duration cannot exceed 2 h');
        picker.setValue({ start: '09:00', end: '09:10' });
        expect(picker.getValidationError()).toBe('The duration must be at least 30 min');
        picker.clear();
        expect(picker.getValidationError()).toBe('Please select a time range');
        picker.destroy();
    });

    it('formats mixed durations', () => {
        const picker = create({ maxDurationMinutes: 90, value: { start: '09:00', end: '11:00' } });
        expect(picker.getValidationError()).toBe('時間長度不可超過 1 小時 30 分鐘');
        picker.destroy();
    });

    it('treats an open-ended range as valid when not required', () => {
        const picker = create({ value: { start: '09:00', end: null } });
        expect(picker.isValid()).toBe(true);
        picker.destroy();
    });
});

describe('TimeRangePicker onChange', () => {
    it('fires once per committed user change, never for setValue', () => {
        const onChange = vi.fn();
        const picker = create({ onChange, value: { start: '09:00', end: '10:00' } });
        picker.setValue({ start: '08:00', end: '09:00' });
        expect(onChange).not.toHaveBeenCalled();
        picker.clear();
        picker.clear();
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(onChange).toHaveBeenLastCalledWith({ start: null, end: null });

        const start = picker.startPicker;
        start.inputWrapper.click();
        hourItem(start, 7).click();
        minuteItem(start, 30).click();
        expect(onChange).toHaveBeenCalledTimes(1);
        start.confirmButton.click();
        expect(onChange).toHaveBeenCalledTimes(2);
        expect(onChange).toHaveBeenLastCalledWith({ start: '07:30', end: null });
        picker.destroy();
    });

    it('the clear button empties both ends, fires once and moves focus to the start', () => {
        const onChange = vi.fn();
        const picker = create({ onChange, value: { start: '09:00', end: '10:00' } });
        const clearButton = picker.element.querySelector('.time-range-picker__clear');
        clearButton.click();
        expect(picker.getValue()).toEqual({ start: null, end: null });
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(clearButton.style.visibility).toBe('hidden');
        expect(document.activeElement).toBe(picker.startPicker.inputWrapper);
        picker.destroy();
    });
});

describe('TimeRangePicker field-error contract', () => {
    it('marks both ends and shows one message below the range row', () => {
        const picker = create();
        const triggers = [picker.startPicker.inputWrapper, picker.endPicker.inputWrapper];
        expect(picker[FIELD_ERROR_CONTRACT]).toBe(true);
        expect(picker.setError('請選擇時間範圍')).toBe(picker);
        const messages = messagesIn(host);
        expect(messages).toHaveLength(1);
        expect(picker.element.querySelector('.time-range-picker__row').nextElementSibling).toBe(messages[0]);
        triggers.forEach((trigger) => {
            expect(trigger.getAttribute('aria-invalid')).toBe('true');
            expect(trigger.getAttribute('aria-describedby')).toContain(messages[0].id);
            expect(trigger.style.outline).toContain('var(--cl-danger)');
        });
        picker.setError('請選擇時間範圍', { display: false });
        expect(messagesIn(host)).toHaveLength(0);
        expect(triggers[1].getAttribute('aria-invalid')).toBe('true');
        expect(picker.clearError()).toBe(picker);
        triggers.forEach((trigger) => expect(trigger.hasAttribute('aria-invalid')).toBe(false));
        picker.setError('x');
        picker.destroy();
        expect(messagesIn(document.body)).toHaveLength(0);
    });
});

describe('TimeRangePicker keyboard', () => {
    it('opens with Enter, picks hour and minute with arrow keys and confirms with Enter', () => {
        const onChange = vi.fn();
        const picker = create({ minuteStep: 15, onChange });
        const start = picker.startPicker;
        const trigger = start.inputWrapper;
        trigger.focus();
        press(trigger, 'Enter');
        expect(start.isOpen).toBe(true);
        expect(trigger.getAttribute('aria-expanded')).toBe('true');
        expect(start.panel.getAttribute('role')).toBe('dialog');
        const [hours, minutes] = [start.hourColumn.scrollContainer, start.minuteColumn.scrollContainer];
        expect(hours.getAttribute('role')).toBe('listbox');
        expect(hours.getAttribute('aria-label')).toBe('小時');
        expect(document.activeElement).toBe(hours);

        press(hours, 'ArrowDown');
        expect(start.snapshot().draftHour).toBe(0);
        for (let i = 0; i < 8; i += 1) press(hours, 'ArrowDown');
        expect(start.snapshot().draftHour).toBe(8);
        expect(hourItem(start, 8).getAttribute('aria-selected')).toBe('true');
        expect(hours.getAttribute('aria-activedescendant')).toBe(hourItem(start, 8).id);
        press(hours, 'PageDown');
        expect(start.snapshot().draftHour).toBe(13);
        press(hours, 'PageUp');
        press(hours, 'ArrowUp');
        expect(start.snapshot().draftHour).toBe(7);

        press(hours, 'ArrowRight');
        expect(document.activeElement).toBe(minutes);
        press(minutes, 'End');
        expect(start.snapshot().draftMinute).toBe(45);
        press(minutes, 'Home');
        press(minutes, 'ArrowDown');
        expect(start.snapshot().draftMinute).toBe(15);
        expect(onChange).not.toHaveBeenCalled();

        press(minutes, 'Enter');
        expect(start.isOpen).toBe(false);
        expect(picker.getValue()).toEqual({ start: '07:15', end: null });
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(document.activeElement).toBe(trigger);
        expect(trigger.getAttribute('aria-expanded')).toBe('false');
        picker.destroy();
    });

    it('skips disallowed values with the arrow keys', () => {
        const picker = create({ minuteStep: 30, value: { start: '09:30', end: null } });
        const end = picker.endPicker;
        end.inputWrapper.focus();
        press(end.inputWrapper, 'ArrowDown');
        const hours = end.hourColumn.scrollContainer;
        press(hours, 'ArrowDown');
        // 0–9 點都沒有晚於 09:30 的分鐘（間隔 30）→ 第一個可選是 10 點
        expect(end.snapshot().draftHour).toBe(10);
        press(hours, 'ArrowUp');
        expect(end.snapshot().draftHour).toBe(10);
        picker.destroy();
    });

    it('keeps Tab inside the panel and closes with Escape', () => {
        const picker = create();
        const start = picker.startPicker;
        start.inputWrapper.focus();
        press(start.inputWrapper, ' ');
        const hours = start.hourColumn.scrollContainer;
        const minutes = start.minuteColumn.scrollContainer;
        press(hours, 'Tab');
        expect(document.activeElement).toBe(minutes);
        press(minutes, 'Tab');
        expect(document.activeElement).toBe(start.confirmButton);
        press(start.confirmButton, 'Tab');
        expect(document.activeElement).toBe(hours);
        press(hours, 'Tab', { shiftKey: true });
        expect(document.activeElement).toBe(start.confirmButton);
        press(start.confirmButton, 'Escape');
        expect(start.isOpen).toBe(false);
        expect(document.activeElement).toBe(start.inputWrapper);
        picker.destroy();
    });

    it('closes an open panel when Tab moves focus to the other end', async () => {
        const picker = create();
        const start = picker.startPicker.inputWrapper;
        start.focus();
        start.click();
        expect(picker.startPicker.isOpen).toBe(true);
        picker.endPicker.inputWrapper.focus();
        expect(picker.startPicker.isOpen).toBe(false);
        expect(start.getAttribute('aria-expanded')).toBe('false');

        start.click();
        document.body.click();
        await flush();
        expect(start.getAttribute('aria-expanded')).toBe('false');
        picker.destroy();
    });
});

describe('TimeRangePicker disabled', () => {
    it('setDisabled blocks both ends and the clear button', () => {
        const picker = create({ value: { start: '09:00', end: '10:00' } });
        picker.setDisabled(true);
        const trigger = picker.startPicker.inputWrapper;
        expect(picker.element.getAttribute('aria-disabled')).toBe('true');
        expect(trigger.tabIndex).toBe(-1);
        expect(trigger.getAttribute('aria-disabled')).toBe('true');
        expect(tabbables(picker.element)).toEqual([]);
        press(trigger, 'Enter');
        trigger.click();
        expect(picker.startPicker.isOpen).toBe(false);
        picker.setDisabled(false);
        press(trigger, 'Enter');
        expect(picker.startPicker.isOpen).toBe(true);
        picker.destroy();
    });
});

describe('TimeRangePicker destroy', () => {
    it('removes its DOM, portaled panels, hints and every listener', () => {
        const docAdd = vi.spyOn(document, 'addEventListener');
        const docRemove = vi.spyOn(document, 'removeEventListener');
        const winAdd = vi.spyOn(window, 'addEventListener');
        const winRemove = vi.spyOn(window, 'removeEventListener');

        const picker = create({ value: { start: '09:00', end: '10:00' } });
        expect(docAdd).not.toHaveBeenCalled();
        picker.endPicker.inputWrapper.click();
        const panel = picker.endPicker.panel;
        expect(panel.parentNode).toBe(document.body);
        // 草稿 09:00 不晚於開始 09:00 → 確認被擋下、面板顯示提示
        hourItem(picker.endPicker, 9).click();
        picker.endPicker.confirmButton.click();
        expect(hintOf(picker.endPicker).style.display).toBe('block');
        picker.setError('x');

        picker.destroy();
        expect(host.children).toHaveLength(0);
        expect(document.body.contains(panel)).toBe(false);
        expect(document.body.querySelectorAll('.timepicker__panel, .b4a-range-hint, .b4a-field-error')).toHaveLength(0);
        expect(leakedListeners(docAdd, docRemove)).toEqual([]);
        expect(leakedListeners(winAdd, winRemove)).toEqual([]);
        expect(() => {
            picker.setValue({ start: '01:00', end: '02:00' });
            picker.clear();
            picker.setDisabled(false);
            picker.setError('x');
            picker.clearError();
            picker.getValidationError();
            picker.destroy();
        }).not.toThrow();
    });
});
