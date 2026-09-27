import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import Locale from '../../ui_components/i18n/index.js';
import { DateRangePicker } from '../../ui_components/form/DateRangePicker/DateRangePicker.js';
import * as DateRangePickerModule from '../../ui_components/form/DateRangePicker/index.js';
import { FIELD_ERROR_CONTRACT } from '../../ui_components/utils/field-error.js';

let host;

beforeEach(() => {
    // 固定「今天」為 2026-06-15，讓日曆初始焦點與年份清單可預期
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

const create = (options = {}) => new DateRangePicker(options).mount(host);
const iso = (date) => (date
    ? `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
    : null);
const dayButton = (picker, day) => picker.calendar.querySelector(`.dp-day[data-day="${day}"]`);
const press = (element, key, extra = {}) => element.dispatchEvent(
    new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...extra })
);
const flush = () => new Promise((resolve) => setTimeout(resolve, 0));
const messagesIn = (element) => [...element.querySelectorAll('.b4a-field-error')];

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

// 每個 addEventListener 都要有對應的 removeEventListener（同型別、同 handler、同 capture）
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

describe('DateRangePicker basics', () => {
    it('exports the class as named and default export', () => {
        expect(DateRangePickerModule.DateRangePicker).toBe(DateRangePicker);
        expect(DateRangePickerModule.default).toBe(DateRangePicker);
    });

    it('renders a named group with both ends reachable by keyboard in order', () => {
        const range = create();
        const root = range.element;
        const start = range.startPicker.inputWrapper;
        const end = range.endPicker.inputWrapper;

        expect(root.getAttribute('role')).toBe('group');
        expect(root.getAttribute('aria-label')).toBe('日期範圍');
        expect(range.getValue()).toEqual({ start: null, end: null });
        expect(range.startPicker.display.textContent).toBe('開始日期');
        expect(range.endPicker.display.textContent).toBe('結束日期');
        expect(root.querySelector('.date-range-picker__separator').textContent).toBe('至');

        for (const [trigger, name] of [[start, '開始日期'], [end, '結束日期']]) {
            expect(trigger.tabIndex).toBe(0);
            expect(trigger.getAttribute('role')).toBe('combobox');
            expect(trigger.getAttribute('aria-haspopup')).toBe('dialog');
            expect(trigger.getAttribute('aria-expanded')).toBe('false');
            expect(trigger.getAttribute('aria-label')).toBe(name);
        }
        expect(start.getAttribute('aria-controls')).toBe(range.startPicker.calendar.id);

        // 空白時「清除」按鈕不在 Tab 順序中
        expect(tabbables(root)).toEqual([start, end]);
        range.setValue({ start: '2026-03-10', end: '2026-03-12' });
        const clearButton = root.querySelector('.date-range-picker__clear');
        expect(tabbables(root)).toEqual([start, end, clearButton]);
        range.destroy();
    });

    it('links a visible label with aria-labelledby and marks required ends', () => {
        const range = create({ label: '租借期間', required: true });
        const labelEl = range.element.querySelector('.date-range-picker__label');
        expect(labelEl.textContent).toBe('租借期間*');
        expect(range.element.getAttribute('aria-labelledby')).toBe(labelEl.id);
        expect(range.element.hasAttribute('aria-label')).toBe(false);
        expect(range.startPicker.inputWrapper.getAttribute('aria-required')).toBe('true');
        expect(range.endPicker.inputWrapper.getAttribute('aria-required')).toBe('true');
        range.destroy();
    });

    it('uses ariaLabel for the group when there is no visible label', () => {
        const range = create({ ariaLabel: '會議室借用期間' });
        expect(range.element.getAttribute('aria-label')).toBe('會議室借用期間');
        range.destroy();
    });

    it('accepts custom placeholders, separator and width', () => {
        const range = create({ startPlaceholder: '借出日', endPlaceholder: '歸還日', separator: '→', width: 480 });
        expect(range.startPicker.display.textContent).toBe('借出日');
        expect(range.startPicker.inputWrapper.getAttribute('aria-label')).toBe('借出日');
        expect(range.endPicker.calendar.getAttribute('aria-label')).toBe('歸還日');
        expect(range.element.querySelector('.date-range-picker__separator').textContent).toBe('→');
        expect(range.element.style.width).toBe('480px');
        range.destroy();

        const fluid = create({ width: '60%' });
        expect(fluid.element.style.width).toBe('60%');
        expect(fluid.element.style.maxWidth).toBe('100%');
        fluid.destroy();
    });

    it('switches strings with the active locale', () => {
        Locale.setLang('en');
        const range = create({ value: { start: '2026-03-10', end: null } });
        expect(range.element.getAttribute('aria-label')).toBe('Date range');
        expect(range.endPicker.display.textContent).toBe('End date');
        expect(range.element.querySelector('.date-range-picker__separator').textContent).toBe('to');
        expect(range.element.querySelector('.date-range-picker__clear').textContent).toBe('Clear');
        range.destroy();
    });

    it('shows the Republic-of-China calendar with format: taiwan', () => {
        const range = create({ format: 'taiwan', value: { start: '2026-03-10', end: '2026-03-12' } });
        expect(range.startPicker.display.textContent).toBe('115/03/10');
        expect(range.endPicker.getFormattedValue()).toBe('115/03/12');
        range.destroy();
    });
});

describe('DateRangePicker value', () => {
    it('round-trips values and hands out copies', () => {
        const range = create({ value: { start: '2026-03-10', end: '2026-03-20' } });
        expect(range.getValue()).toEqual({ start: '2026-03-10', end: '2026-03-20' });
        expect(range.startPicker.getFormattedValue()).toBe('2026/03/10');
        expect(range.endPicker.getFormattedValue()).toBe('2026/03/20');

        const copy = range.getValue();
        copy.start = '1999-01-01';
        expect(range.getValue().start).toBe('2026-03-10');

        expect(range.setValue({ start: '2026-04-01' })).toBe(range);
        expect(range.getValue()).toEqual({ start: '2026-04-01', end: null });
        range.setValue(null);
        expect(range.getValue()).toEqual({ start: null, end: null });
        range.setValue({ start: '2026-04-01', end: '2026-04-02' });
        range.setValue('');
        expect(range.getValue()).toEqual({ start: null, end: null });
        expect(range.startPicker.getValue()).toBeNull();
        range.destroy();
    });

    it('rejects malformed values with console.warn and keeps the value', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const range = create({ value: { start: '2026-03-10', end: '2026-03-20' } });
        const bad = [
            { start: '2026-3-10', end: null },
            { start: '2026-02-30' },
            { start: '2026/03/10' },
            { start: 20260310 },
            { end: new Date(2026, 2, 21) },
            '2026-03-10',
            ['2026-03-10', '2026-03-11'],
            42
        ];
        bad.forEach((value) => range.setValue(value));
        expect(range.getValue()).toEqual({ start: '2026-03-10', end: '2026-03-20' });
        expect(range.startPicker.getFormattedValue()).toBe('2026/03/10');
        expect(warn).toHaveBeenCalledTimes(bad.length);
        expect(warn.mock.calls[0][0]).toContain('[DateRangePicker]');
        range.destroy();
    });

    it('rejects a reversed range instead of swapping the ends', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const range = create({ value: { start: '2026-03-10', end: '2026-03-12' } });
        range.setValue({ start: '2026-03-20', end: '2026-03-01' });
        expect(range.getValue()).toEqual({ start: '2026-03-10', end: '2026-03-12' });
        expect(warn).toHaveBeenCalledTimes(1);
        expect(warn.mock.calls[0][0]).toContain('before start');

        const reversed = create({ value: { start: '2026-03-20', end: '2026-03-01' } });
        expect(reversed.getValue()).toEqual({ start: null, end: null });
        expect(warn).toHaveBeenCalledTimes(2);
        range.destroy();
        reversed.destroy();
    });

    it('accepts min / max as Date objects and ignores invalid bounds', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const range = create({ min: new Date(2026, 2, 1), max: '2026-03-31' });
        expect(iso(range.startPicker.minDate)).toBe('2026-03-01');
        expect(iso(range.endPicker.maxDate)).toBe('2026-03-31');

        const loose = create({ min: 'yesterday', maxSpanDays: 0 });
        expect(loose.startPicker.minDate).toBeNull();
        expect(warn).toHaveBeenCalledTimes(2);
        range.destroy();
        loose.destroy();
    });
});

describe('DateRangePicker constraints', () => {
    it('constrains each end by the other end, min / max and maxSpanDays', () => {
        const range = create({ min: '2026-03-01', max: '2026-03-31', maxSpanDays: 7 });
        expect(iso(range.startPicker.minDate)).toBe('2026-03-01');
        expect(iso(range.startPicker.maxDate)).toBe('2026-03-31');
        expect(iso(range.endPicker.minDate)).toBe('2026-03-01');

        range.setValue({ start: '2026-03-10', end: null });
        // 7 天含首尾：03-10 ~ 03-16
        expect(iso(range.endPicker.minDate)).toBe('2026-03-10');
        expect(iso(range.endPicker.maxDate)).toBe('2026-03-16');
        expect(iso(range.startPicker.minDate)).toBe('2026-03-01');
        expect(iso(range.startPicker.maxDate)).toBe('2026-03-31');

        range.setValue({ start: null, end: '2026-03-05' });
        expect(iso(range.startPicker.minDate)).toBe('2026-03-01');
        expect(iso(range.startPicker.maxDate)).toBe('2026-03-05');
        expect(iso(range.endPicker.minDate)).toBe('2026-03-01');
        range.destroy();
    });

    it('disables out-of-range days in the calendar and propagates a pick to the other end', () => {
        const onChange = vi.fn();
        const range = create({ maxSpanDays: 7, onChange, value: { start: '2026-03-10', end: null } });
        range.endPicker.inputWrapper.click();
        // 結束端沒有值時，日曆跟著開始日跳到同一個月
        expect(range.endPicker.snapshot().currentMonth).toBe(2);
        expect(dayButton(range.endPicker, 9).dataset.disabled).toBe('true');
        expect(dayButton(range.endPicker, 9).getAttribute('aria-disabled')).toBe('true');
        expect(dayButton(range.endPicker, 10).dataset.disabled).toBe('false');
        expect(dayButton(range.endPicker, 17).dataset.disabled).toBe('true');

        // 早於開始日的日期點了也不會生效（拒絕，不對調）
        dayButton(range.endPicker, 9).click();
        expect(range.getValue()).toEqual({ start: '2026-03-10', end: null });
        expect(onChange).not.toHaveBeenCalled();

        dayButton(range.endPicker, 12).click();
        expect(range.getValue()).toEqual({ start: '2026-03-10', end: '2026-03-12' });
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(onChange).toHaveBeenCalledWith({ start: '2026-03-10', end: '2026-03-12' });

        // 反向約束：開始端最晚到結束日，最早到結束日往前 6 天
        expect(iso(range.startPicker.maxDate)).toBe('2026-03-12');
        expect(iso(range.startPicker.minDate)).toBe('2026-03-06');
        range.startPicker.inputWrapper.click();
        expect(dayButton(range.startPicker, 13).dataset.disabled).toBe('true');
        expect(dayButton(range.startPicker, 5).dataset.disabled).toBe('true');
        expect(dayButton(range.startPicker, 6).dataset.disabled).toBe('false');
        range.destroy();
    });

    it('decorates days and aria-expanded after a programmatic open()', async () => {
        const range = create({ value: { start: '2026-03-10', end: null } });
        range.endPicker.open();
        await flush();
        expect(range.endPicker.inputWrapper.getAttribute('aria-expanded')).toBe('true');
        expect(dayButton(range.endPicker, 9).getAttribute('aria-disabled')).toBe('true');
        expect(dayButton(range.endPicker, 10).getAttribute('aria-label')).toContain('10');
        range.endPicker.close();
        await flush();
        expect(range.endPicker.inputWrapper.getAttribute('aria-expanded')).toBe('false');
        range.destroy();
    });

    it('redraws an open calendar when the other end changes', () => {
        const range = create({ value: { start: '2026-03-10', end: null } });
        range.endPicker.inputWrapper.click();
        expect(dayButton(range.endPicker, 12).dataset.disabled).toBe('false');
        range.setValue({ start: '2026-03-14', end: null });
        expect(dayButton(range.endPicker, 12).dataset.disabled).toBe('true');
        range.destroy();
    });

    it('opens an empty calendar on the nearest allowed month', () => {
        const range = create({ min: '2026-09-01', max: '2026-09-30' });
        // 今天（06-15）所在的月份整個在範圍外 → 檢視移到 9 月
        expect(range.startPicker.snapshot().currentMonth).toBe(8);
        expect(range.endPicker.snapshot().currentMonth).toBe(8);
        range.startPicker.inputWrapper.click();
        expect(dayButton(range.startPicker, 1).dataset.disabled).toBe('false');
        range.destroy();

        const past = create({ max: '2026-01-31' });
        expect(past.startPicker.snapshot().currentYear).toBe(2026);
        expect(past.startPicker.snapshot().currentMonth).toBe(0);
        past.destroy();
    });

    it('stops Escape at an open calendar so an enclosing dialog stays open', () => {
        const outer = vi.fn();
        host.addEventListener('keydown', outer);
        const range = create();
        const trigger = range.startPicker.inputWrapper;
        trigger.click();
        press(trigger, 'Escape');
        expect(range.startPicker.isOpen).toBe(false);
        expect(outer).not.toHaveBeenCalled();
        press(trigger, 'Escape');
        expect(outer).toHaveBeenCalledTimes(1);
        range.destroy();
    });

    it('allowSameDay: false keeps the ends on different days', () => {
        const range = create({ allowSameDay: false, value: { start: '2026-03-10', end: null } });
        expect(iso(range.endPicker.minDate)).toBe('2026-03-11');
        range.setValue({ start: null, end: '2026-03-10' });
        expect(iso(range.startPicker.maxDate)).toBe('2026-03-09');

        // setValue 保留違反限制的既有資料，由驗證回報
        range.setValue({ start: '2026-03-10', end: '2026-03-10' });
        expect(range.getValue()).toEqual({ start: '2026-03-10', end: '2026-03-10' });
        expect(range.isValid()).toBe(false);
        expect(range.getValidationError()).toBe('結束日期必須晚於開始日期');
        range.destroy();
    });
});

describe('DateRangePicker presets', () => {
    const presets = [
        { label: '三月上旬', range: () => ({ start: '2026-03-01', end: '2026-03-10' }) },
        { label: '顛倒', range: () => ({ start: '2026-03-10', end: '2026-03-01' }) },
        { label: '太長', range: () => ({ start: '2026-03-01', end: '2026-03-31' }) },
        { label: '格式錯', range: () => ({ start: '2026/03/01', end: null }) },
        { label: '拋錯', range: () => { throw new Error('boom'); } },
        { label: '<b>粗體</b>', range: () => ({ start: '2026-04-01', end: '2026-04-02' }) }
    ];

    it('renders preset buttons that apply a range and fire onChange once', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const onChange = vi.fn();
        const range = create({ presets, maxSpanDays: 14, onChange });
        const group = range.element.querySelector('.date-range-picker__presets');
        expect(group.getAttribute('role')).toBe('group');
        expect(group.getAttribute('aria-label')).toBe('快速選擇');
        const buttons = [...group.querySelectorAll('.date-range-picker__preset')];
        expect(buttons.map((button) => button.textContent)).toEqual(presets.map((preset) => preset.label));
        expect(group.querySelector('b')).toBeNull();

        buttons[0].click();
        expect(range.getValue()).toEqual({ start: '2026-03-01', end: '2026-03-10' });
        expect(onChange).toHaveBeenCalledTimes(1);
        buttons[0].click();
        expect(onChange).toHaveBeenCalledTimes(1);

        // 顛倒、超過 maxSpanDays、格式錯、拋錯都拒絕並警告
        [1, 2, 3, 4].forEach((index) => buttons[index].click());
        expect(range.getValue()).toEqual({ start: '2026-03-01', end: '2026-03-10' });
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(warn).toHaveBeenCalledTimes(4);

        buttons[5].click();
        expect(onChange).toHaveBeenLastCalledWith({ start: '2026-04-01', end: '2026-04-02' });
        range.destroy();
    });

    it('renders no preset row without presets', () => {
        const range = create();
        expect(range.element.querySelector('.date-range-picker__presets')).toBeNull();
        range.destroy();
    });
});

describe('DateRangePicker validation', () => {
    it('reports the reason in zh-TW and en', () => {
        const range = create({ required: true, min: '2026-03-01', max: '2026-03-31', maxSpanDays: 5 });
        expect(range.isValid()).toBe(false);
        expect(range.getValidationError()).toBe('請選擇日期範圍');
        range.setValue({ start: '2026-03-10', end: null });
        expect(range.getValidationError()).toBe('請選擇結束日期');
        range.setValue({ start: null, end: '2026-03-10' });
        expect(range.getValidationError()).toBe('請選擇開始日期');
        range.setValue({ start: '2026-02-20', end: '2026-02-22' });
        expect(range.getValidationError()).toBe('日期不可早於 2026/03/01');
        range.setValue({ start: '2026-04-01', end: '2026-04-02' });
        expect(range.getValidationError()).toBe('日期不可晚於 2026/03/31');
        range.setValue({ start: '2026-03-01', end: '2026-03-10' });
        expect(range.getValidationError()).toBe('日期範圍不可超過 5 天');
        range.setValue({ start: '2026-03-01', end: '2026-03-05' });
        expect(range.getValidationError()).toBe('');
        expect(range.isValid()).toBe(true);

        Locale.setLang('en');
        range.setValue({ start: '2026-03-01', end: '2026-03-10' });
        expect(range.getValidationError()).toBe('The range cannot exceed 5 days');
        range.setValue({ start: '2026-02-20', end: '2026-02-22' });
        expect(range.getValidationError()).toBe('Dates cannot be earlier than 2026/03/01');
        range.setValue(null);
        expect(range.getValidationError()).toBe('Please select a date range');
        range.destroy();
    });

    it('formats bounds in the Republic-of-China calendar with format: taiwan', () => {
        const range = create({ format: 'taiwan', min: '2026-03-01', value: { start: '2026-02-01', end: null } });
        expect(range.getValidationError()).toBe('日期不可早於 115/03/01');
        range.destroy();
    });

    it('treats an open-ended range as valid when not required', () => {
        const range = create({ value: { start: '2026-03-10', end: null } });
        expect(range.isValid()).toBe(true);
        range.clear();
        expect(range.isValid()).toBe(true);
        range.destroy();
    });
});

describe('DateRangePicker onChange', () => {
    it('fires once per committed user change, never for setValue', () => {
        const onChange = vi.fn();
        const range = create({ onChange, value: { start: '2026-03-10', end: '2026-03-12' } });
        expect(onChange).not.toHaveBeenCalled();
        range.setValue({ start: '2026-03-01', end: '2026-03-02' });
        expect(onChange).not.toHaveBeenCalled();

        range.clear();
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(onChange).toHaveBeenLastCalledWith({ start: null, end: null });
        range.clear();
        expect(onChange).toHaveBeenCalledTimes(1);

        range.startPicker.open();
        dayButton(range.startPicker, 5).click();
        expect(onChange).toHaveBeenCalledTimes(2);
        expect(onChange).toHaveBeenLastCalledWith({ start: '2026-03-05', end: null });
        range.destroy();
    });

    it('the clear button empties both ends, fires once and moves focus to the start', () => {
        const onChange = vi.fn();
        const range = create({ onChange, value: { start: '2026-03-10', end: '2026-03-12' } });
        const clearButton = range.element.querySelector('.date-range-picker__clear');
        expect(clearButton.style.visibility).toBe('visible');
        clearButton.focus();
        clearButton.click();
        expect(range.getValue()).toEqual({ start: null, end: null });
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(clearButton.style.visibility).toBe('hidden');
        expect(clearButton.disabled).toBe(true);
        expect(document.activeElement).toBe(range.startPicker.inputWrapper);
        range.destroy();
    });

    it('clearable: false renders no clear button', () => {
        const range = create({ clearable: false, value: { start: '2026-03-10', end: null } });
        expect(range.element.querySelector('.date-range-picker__clear')).toBeNull();
        range.destroy();
    });
});

describe('DateRangePicker field-error contract', () => {
    it('marks both ends and shows one message below the range row', () => {
        const range = create({ presets: [{ label: '本週', range: () => ({ start: '2026-06-15', end: '2026-06-21' }) }] });
        const triggers = [range.startPicker.inputWrapper, range.endPicker.inputWrapper];
        expect(range[FIELD_ERROR_CONTRACT]).toBe(true);

        expect(range.setError('請選擇日期範圍')).toBe(range);
        const messages = messagesIn(host);
        expect(messages).toHaveLength(1);
        expect(messages[0].textContent).toBe('請選擇日期範圍');
        expect(messages[0].getAttribute('role')).toBe('alert');
        expect(range.element.querySelector('.date-range-picker__row').nextElementSibling).toBe(messages[0]);
        expect(range.element.querySelector('.date-range-picker__presets').previousElementSibling).toBe(messages[0]);
        triggers.forEach((trigger) => {
            expect(trigger.getAttribute('aria-invalid')).toBe('true');
            expect(trigger.getAttribute('aria-describedby')).toContain(messages[0].id);
            expect(trigger.style.outline).toContain('var(--cl-danger)');
        });

        range.setError('請選擇日期範圍', { display: false });
        expect(messagesIn(host)).toHaveLength(0);
        triggers.forEach((trigger) => expect(trigger.getAttribute('aria-invalid')).toBe('true'));

        expect(range.clearError()).toBe(range);
        triggers.forEach((trigger) => {
            expect(trigger.hasAttribute('aria-invalid')).toBe(false);
            expect(trigger.style.outline).toBe('');
        });

        range.setError('x');
        range.setError('');
        expect(messagesIn(host)).toHaveLength(0);
        range.setError('y');
        range.destroy();
        expect(messagesIn(document.body)).toHaveLength(0);
    });
});

describe('DateRangePicker keyboard', () => {
    it('opens with Enter, moves with arrow keys, selects with Enter and returns focus', () => {
        const onChange = vi.fn();
        const range = create({ onChange, value: { start: '2026-03-10', end: null } });
        const trigger = range.endPicker.inputWrapper;
        trigger.focus();
        press(trigger, 'Enter');

        expect(range.endPicker.isOpen).toBe(true);
        expect(trigger.getAttribute('aria-expanded')).toBe('true');
        expect(range.endPicker.calendar.getAttribute('role')).toBe('dialog');
        expect(range.endPicker.calendar.getAttribute('aria-label')).toBe('結束日期');
        // 沒有已選日期、今天不在檢視月份 → 第一個可選日期（開始日 03-10）
        expect(document.activeElement).toBe(dayButton(range.endPicker, 10));
        expect(document.activeElement.getAttribute('aria-label')).toContain('10');

        press(document.activeElement, 'ArrowRight');
        expect(document.activeElement).toBe(dayButton(range.endPicker, 11));
        press(document.activeElement, 'ArrowDown');
        expect(document.activeElement).toBe(dayButton(range.endPicker, 18));
        press(document.activeElement, 'ArrowLeft');
        press(document.activeElement, 'ArrowUp');
        expect(document.activeElement).toBe(dayButton(range.endPicker, 10));
        press(document.activeElement, 'End');
        expect(document.activeElement).toBe(dayButton(range.endPicker, 31));
        press(document.activeElement, 'Home');
        expect(document.activeElement).toBe(dayButton(range.endPicker, 1));
        press(document.activeElement, 'Enter');
        // 03-01 早於開始日：不可選，日曆保持開啟
        expect(range.endPicker.isOpen).toBe(true);
        expect(onChange).not.toHaveBeenCalled();

        press(document.activeElement, 'ArrowDown', {});
        press(document.activeElement, 'ArrowDown');
        press(document.activeElement, 'ArrowDown');
        expect(document.activeElement).toBe(dayButton(range.endPicker, 22));
        press(document.activeElement, 'Enter');
        expect(range.getValue()).toEqual({ start: '2026-03-10', end: '2026-03-22' });
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(range.endPicker.isOpen).toBe(false);
        expect(trigger.getAttribute('aria-expanded')).toBe('false');
        expect(document.activeElement).toBe(trigger);
        range.destroy();
    });

    it('focuses the selected day, pages months and closes with Escape', () => {
        const range = create({ value: { start: '2026-03-10', end: '2026-03-20' } });
        const trigger = range.startPicker.inputWrapper;
        trigger.focus();
        press(trigger, ' ');
        expect(document.activeElement).toBe(dayButton(range.startPicker, 10));
        expect(dayButton(range.startPicker, 10).getAttribute('aria-pressed')).toBe('true');

        press(document.activeElement, 'PageUp');
        expect(range.startPicker.snapshot().currentMonth).toBe(1);
        expect(document.activeElement).toBe(dayButton(range.startPicker, 10));
        press(document.activeElement, 'PageDown');
        expect(range.startPicker.snapshot().currentMonth).toBe(2);
        // 月初往左跨到上個月底
        press(document.activeElement, 'Home');
        press(document.activeElement, 'ArrowLeft');
        expect(range.startPicker.snapshot().currentMonth).toBe(1);
        expect(document.activeElement).toBe(dayButton(range.startPicker, 28));

        press(document.activeElement, 'Escape');
        expect(range.startPicker.isOpen).toBe(false);
        expect(document.activeElement).toBe(trigger);
        expect(range.getValue()).toEqual({ start: '2026-03-10', end: '2026-03-20' });
        range.destroy();
    });

    it('keeps Tab inside the open calendar', () => {
        const range = create({ value: { start: '2026-03-10', end: null } });
        const picker = range.startPicker;
        picker.inputWrapper.focus();
        press(picker.inputWrapper, 'ArrowDown');
        const day = document.activeElement;
        expect(day).toBe(dayButton(picker, 10));
        press(day, 'Tab');
        expect(document.activeElement).toBe(picker.prevButton);
        press(document.activeElement, 'Tab');
        expect(document.activeElement).toBe(picker.yearSelect);
        press(document.activeElement, 'Tab', { shiftKey: true });
        press(document.activeElement, 'Tab', { shiftKey: true });
        expect(document.activeElement).toBe(day);
        range.destroy();
    });

    it('closes an open calendar when Tab moves focus to the other end', () => {
        const range = create();
        const start = range.startPicker.inputWrapper;
        start.focus();
        start.click();
        expect(range.startPicker.isOpen).toBe(true);
        range.endPicker.inputWrapper.focus();
        expect(range.startPicker.isOpen).toBe(false);
        expect(start.getAttribute('aria-expanded')).toBe('false');
        range.destroy();
    });

    it('keeps aria-expanded in sync when the calendar closes from an outside click', async () => {
        const range = create();
        const start = range.startPicker.inputWrapper;
        start.click();
        expect(start.getAttribute('aria-expanded')).toBe('true');
        document.body.click();
        expect(range.startPicker.isOpen).toBe(false);
        await flush();
        expect(start.getAttribute('aria-expanded')).toBe('false');
        range.destroy();
    });

    it('returns focus to the trigger after a mouse pick', async () => {
        const range = create({ value: { start: '2026-03-10', end: null } });
        range.endPicker.inputWrapper.click();
        dayButton(range.endPicker, 15).click();
        await flush();
        expect(range.getValue().end).toBe('2026-03-15');
        expect(document.activeElement).toBe(range.endPicker.inputWrapper);
        range.destroy();
    });
});

describe('DateRangePicker disabled', () => {
    it('setDisabled blocks both ends, the clear button and presets', () => {
        const range = create({
            value: { start: '2026-03-10', end: '2026-03-12' },
            presets: [{ label: '本週', range: () => ({ start: '2026-06-15', end: '2026-06-21' }) }]
        });
        const triggers = [range.startPicker.inputWrapper, range.endPicker.inputWrapper];
        range.setDisabled(true);
        expect(range.element.getAttribute('aria-disabled')).toBe('true');
        triggers.forEach((trigger) => {
            expect(trigger.tabIndex).toBe(-1);
            expect(trigger.getAttribute('aria-disabled')).toBe('true');
        });
        expect(range.element.querySelector('.date-range-picker__clear').disabled).toBe(true);
        expect(range.element.querySelector('.date-range-picker__preset').disabled).toBe(true);
        expect(tabbables(range.element)).toEqual([]);

        triggers[0].click();
        press(triggers[0], 'Enter');
        expect(range.startPicker.isOpen).toBe(false);

        range.setDisabled(false);
        expect(triggers[0].tabIndex).toBe(0);
        press(triggers[0], 'Enter');
        expect(range.startPicker.isOpen).toBe(true);
        range.destroy();
    });

    it('honours disabled: true at construction', () => {
        const range = create({ disabled: true });
        expect(range.startPicker.inputWrapper.tabIndex).toBe(-1);
        expect(range.snapshot().availability).toBe('disabled');
        range.destroy();
    });
});

describe('DateRangePicker destroy', () => {
    it('removes its DOM, portaled calendars, error message and every listener', () => {
        const docAdd = vi.spyOn(document, 'addEventListener');
        const docRemove = vi.spyOn(document, 'removeEventListener');
        const winAdd = vi.spyOn(window, 'addEventListener');
        const winRemove = vi.spyOn(window, 'removeEventListener');

        const range = create({ value: { start: '2026-03-10', end: '2026-03-12' } });
        expect(docAdd).not.toHaveBeenCalled();
        range.startPicker.inputWrapper.click();
        const calendar = range.startPicker.calendar;
        expect(calendar.parentNode).toBe(document.body);
        expect(docAdd).toHaveBeenCalled();
        range.setError('請選擇日期範圍');

        range.destroy();
        expect(host.children).toHaveLength(0);
        expect(document.body.contains(calendar)).toBe(false);
        expect(document.body.querySelectorAll('.datepicker__calendar')).toHaveLength(0);
        expect(messagesIn(document.body)).toHaveLength(0);
        expect(leakedListeners(docAdd, docRemove)).toEqual([]);
        expect(leakedListeners(winAdd, winRemove)).toEqual([]);
        expect(range.snapshot().lifecycle).toBe('destroyed');

        expect(() => {
            range.setValue({ start: '2026-01-01', end: null });
            range.clear();
            range.setDisabled(true);
            range.setError('x');
            range.clearError();
            range.getValue();
            range.isValid();
            range.getValidationError();
            range.mount(host);
            range.destroy();
        }).not.toThrow();
        expect(host.children).toHaveLength(0);
    });
});
