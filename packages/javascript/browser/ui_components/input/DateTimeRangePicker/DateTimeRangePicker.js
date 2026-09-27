/**
 * DateTimeRangePicker — 日期時間範圍選擇器
 *
 * 每一端由一個 DatePicker 與一個 TimePicker 組成，值為
 * { start: 'YYYY-MM-DDTHH:mm' | null, end: 'YYYY-MM-DDTHH:mm' | null }（當地時間，不做時區換算）。
 *
 * 為什麼不直接組合 DateTimeInput：DateTimeInput 沒有 min / max 選項、內部 DatePicker 建立時不帶範圍，
 * 也沒有「日期＋時間」的約束方式，無法表達「結束必須晚於開始」「最長 N 分鐘」這類跨端限制；
 * 因此直接組合 DatePicker ＋ TimePicker，並共用 DateRangePicker / TimeRangePicker 的鍵盤與約束輔助函式。
 *
 * - 兩端互相約束：日期欄的可選日期、時間欄的可選時間都依另一端、min / max、maxSpanMinutes 計算，
 *   已選的時間也會納入（例如結束時間 08:00、開始是 09-10 22:00 時，結束日期不能選 09-10）。
 * - 一端只有日期或只有時間時，該端的值為 null（尚未完整），驗證會回報缺哪一部分。
 * - 順序錯誤一律「拒絕」：setValue 收到結束不晚於開始的值時 console.warn 並保持原值（不會自動對調）；
 *   min / max / maxSpanMinutes 則不設防（保留既有資料），由 isValid() / getValidationError() 回報。
 */
import Locale from '../../i18n/index.js';
import './locale.js';
import { DatePicker } from '../../form/DatePicker/DatePicker.js';
import { TimePicker } from '../../form/TimePicker/TimePicker.js';
import { createComponentState } from '../../utils/component-state.js';
import { setFieldError, clearFieldError, FIELD_ERROR_CONTRACT } from '../../utils/field-error.js';
import { nextUid } from '../../utils/uid.js';
import {
    enhanceDatePicker,
    setDatePickerBounds,
    setDatePickerValue,
    showDatePickerMonth,
    dayNumberOf,
    parseIsoDay,
    partsOfDayNumber,
    formatDayNumber,
    toggleAttr
} from '../../form/DateRangePicker/DateRangePicker.js';
import {
    enhanceTimePicker,
    setTimePickerValue,
    parseTimeOfDay,
    formatTimeOfDay,
    formatDuration,
    normalizeMinuteStep,
    DAY_MINUTES
} from '../../form/TimeRangePicker/TimeRangePicker.js';

const ISO_DATETIME = /^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2})$/;
const INVALID = Symbol('invalid');
const EMPTY_PARTS = Object.freeze({ startDate: null, startTime: null, endDate: null, endTime: null });
const ENDS = ['start', 'end'];

/** 嚴格拆解 'YYYY-MM-DDTHH:mm'（日期必須存在、時間 00:00–23:59）；不合格回傳 null。 */
function splitDateTime(value) {
    const match = typeof value === 'string' ? ISO_DATETIME.exec(value) : null;
    const valid = Boolean(match) && parseIsoDay(match[1]) !== null && parseTimeOfDay(match[2]) !== null;
    return valid ? { date: match[1], time: match[2] } : null;
}

/** 日期＋時間 → 分鐘數（以日序計算，不受時區影響）；任一部分缺少時回傳 null。 */
function minutesOf(date, time) {
    const day = parseIsoDay(date);
    const minutes = parseTimeOfDay(time);
    return day === null || minutes === null ? null : day * DAY_MINUTES + minutes;
}

function joinDateTime(date, time) {
    return date && time ? `${date}T${time}` : null;
}

function parseRangeEnd(raw) {
    const empty = raw === null || raw === undefined || raw === '';
    return empty ? { date: null, time: null } : splitDateTime(raw) || INVALID;
}

export class DateTimeRangePicker {
    constructor(options = {}) {
        this.options = {
            value: null,                  // 初始值 { start: 'YYYY-MM-DDTHH:mm' | null, end: 'YYYY-MM-DDTHH:mm' | null }
            min: null,                    // 最早時間：'YYYY-MM-DDTHH:mm' 或 Date；null 不限
            max: null,                    // 最晚時間：'YYYY-MM-DDTHH:mm' 或 Date；null 不限
            format: 'western',            // 日期顯示格式（同 DatePicker）：'western' 西元 / 'taiwan' 民國
            minuteStep: 15,               // 分鐘欄間隔（同 TimePicker；預設與 DateTimeInput 相同為 15）
            maxSpanMinutes: null,         // 最長時間長度（分鐘）；null 不限
            required: false,              // 必填（只影響 isValid / getValidationError）
            separator: Locale.t('dateTimeRangePicker.separator'), // 兩端之間的文字
            label: '',                    // 可見標籤；群組以 aria-labelledby 指向它
            ariaLabel: '',                // 沒有可見標籤時的群組名稱；空值用 Locale 的 groupLabel
            clearable: true,              // 有值時顯示「清除」按鈕
            disabled: false,              // 停用
            width: '100%',                // 元件寬度（CSS 長度字串或數字 px）
            onChange: null,               // (value) => {}，值真的改變時觸發一次
            ...options
        };

        this._min = this._normalizeBound('min');
        this._max = this._normalizeBound('max');
        this._maxSpan = this._normalizeSpan();
        this._minuteStep = normalizeMinuteStep(this.options.minuteStep, 15, 'DateTimeRangePicker');
        this._offs = [];
        this._destroyed = false;

        this._state = createComponentState({
            lifecycle: 'created',
            availability: this.options.disabled ? 'disabled' : 'enabled',
            ...EMPTY_PARTS
        }, {
            MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
            DESTROY: (state) => ({ ...state, lifecycle: 'destroyed' }),
            SET_PARTS: (state, payload) => ({
                ...state,
                startDate: payload?.startDate ?? null,
                startTime: payload?.startTime ?? null,
                endDate: payload?.endDate ?? null,
                endTime: payload?.endTime ?? null
            }),
            SET_DISABLED: (state, payload) => ({ ...state, availability: payload?.disabled ? 'disabled' : 'enabled' })
        });

        this.element = this._createElement();

        const initial = this._parse(this.options.value);
        if (!initial.ok) console.warn(`[DateTimeRangePicker] invalid initial value: ${initial.reason}; starting empty.`, this.options.value);
        this._applyParts(initial.ok ? initial.parts : EMPTY_PARTS);
        this._applyDisabled();
    }

    _normalizeBound(name) {
        const raw = this.options[name];
        if (raw === null || raw === undefined || raw === '') return null;
        const isDate = raw instanceof Date && !Number.isNaN(raw.getTime());
        const split = isDate ? null : splitDateTime(raw);
        const minutes = isDate
            ? dayNumberOf(raw.getFullYear(), raw.getMonth() + 1, raw.getDate()) * DAY_MINUTES + raw.getHours() * 60 + raw.getMinutes()
            : split && minutesOf(split.date, split.time);
        if (minutes === null) console.warn(`[DateTimeRangePicker] ignoring invalid ${name} option (expected YYYY-MM-DDTHH:mm or Date):`, raw);
        return minutes;
    }

    _normalizeSpan() {
        const raw = this.options.maxSpanMinutes;
        if (raw === null || raw === undefined) return null;
        const span = Number(raw);
        if (Number.isInteger(span) && span >= 1) return span;
        console.warn('[DateTimeRangePicker] ignoring invalid maxSpanMinutes option (expected an integer >= 1):', raw);
        return null;
    }

    _on(target, type, handler) {
        target.addEventListener(type, handler);
        this._offs.push(() => target.removeEventListener(type, handler));
    }

    _createElement() {
        const { label, ariaLabel, required, separator, clearable, width, format } = this.options;
        const t = (key) => Locale.t(`dateTimeRangePicker.${key}`);

        const root = document.createElement('div');
        root.className = 'datetime-range-picker';
        root.setAttribute('role', 'group');
        root.style.cssText = 'display:block;max-width:100%;box-sizing:border-box;font-family:var(--cl-font-family);';
        root.style.width = typeof width === 'number' ? `${width}px` : String(width || '');

        if (label) {
            const labelEl = document.createElement('div');
            labelEl.className = 'datetime-range-picker__label';
            labelEl.id = nextUid('datetime-range-picker-label');
            labelEl.textContent = String(label);
            labelEl.style.cssText = 'display:block;margin-bottom:4px;font-size:var(--cl-font-size-md);font-weight:500;color:var(--cl-text);';
            if (required) {
                const mark = document.createElement('span');
                mark.setAttribute('aria-hidden', 'true');
                mark.textContent = '*';
                mark.style.cssText = 'margin-left:2px;color:var(--cl-danger);';
                labelEl.appendChild(mark);
            }
            root.appendChild(labelEl);
            root.setAttribute('aria-labelledby', labelEl.id);
        } else {
            root.setAttribute('aria-label', String(ariaLabel || t('groupLabel')));
        }

        const row = document.createElement('div');
        row.className = 'datetime-range-picker__row';
        row.style.cssText = 'display:flex;flex-wrap:wrap;align-items:center;gap:8px;min-width:0;';

        this._pickers = {};
        this._bridges = {};
        const createEnd = (which) => {
            const group = document.createElement('div');
            group.className = `datetime-range-picker__${which}`;
            group.style.cssText = 'flex:1 1 260px;display:flex;flex-wrap:wrap;align-items:center;gap:8px;min-width:0;';

            const dateSlot = document.createElement('div');
            dateSlot.className = 'datetime-range-picker__date';
            dateSlot.style.cssText = 'flex:1 1 140px;min-width:0;';
            const datePicker = new DatePicker({
                placeholder: t(`${which}Date`),
                format,
                onChange: () => this._handlePick(which, 'Date')
            });
            datePicker.mount(dateSlot);
            datePicker.element.style.width = '100%';

            const timeSlot = document.createElement('div');
            timeSlot.className = 'datetime-range-picker__time';
            timeSlot.style.cssText = 'flex:1 1 110px;min-width:0;';
            const timePicker = new TimePicker({
                placeholder: t(`${which}Time`),
                minuteStep: this._minuteStep,
                onChange: () => this._handlePick(which, 'Time')
            });
            timePicker.mount(timeSlot);

            group.append(dateSlot, timeSlot);
            row.appendChild(group);

            this._pickers[`${which}Date`] = datePicker;
            this._pickers[`${which}Time`] = timePicker;
            this._bridges[`${which}Date`] = enhanceDatePicker(datePicker, { label: t(`${which}Date`) });
            this._bridges[`${which}Time`] = enhanceTimePicker(timePicker, { label: t(`${which}Time`) });
        };

        createEnd('start');
        const separatorEl = document.createElement('span');
        separatorEl.className = 'datetime-range-picker__separator';
        separatorEl.textContent = String(separator ?? '');
        separatorEl.style.cssText = 'flex:0 0 auto;font-size:var(--cl-font-size-md);color:var(--cl-text-secondary);';
        row.appendChild(separatorEl);
        createEnd('end');

        this.startDatePicker = this._pickers.startDate;
        this.startTimePicker = this._pickers.startTime;
        this.endDatePicker = this._pickers.endDate;
        this.endTimePicker = this._pickers.endTime;
        Object.values(this._bridges).forEach((bridge) => bridge.setRequired(required));

        this._clearButton = null;
        if (clearable) {
            const clearButton = document.createElement('button');
            clearButton.type = 'button';
            clearButton.className = 'datetime-range-picker__clear';
            clearButton.textContent = t('clear');
            clearButton.style.cssText = 'flex:0 0 auto;padding:4px 10px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-sm);background:var(--cl-bg);color:var(--cl-text-secondary);font-family:inherit;font-size:var(--cl-font-size-sm);cursor:pointer;';
            this._on(clearButton, 'click', () => {
                this.clear();
                this.startDatePicker.inputWrapper.focus();
            });
            row.appendChild(clearButton);
            this._clearButton = clearButton;
        }

        root.appendChild(row);
        this._row = row;
        return root;
    }

    _parse(value) {
        if (value === null || value === undefined || value === '') return { ok: true, parts: { ...EMPTY_PARTS } };
        if (typeof value !== 'object' || Array.isArray(value)) return { ok: false, reason: 'expected { start, end }' };
        const start = parseRangeEnd(value.start);
        const end = parseRangeEnd(value.end);
        if (start === INVALID) return { ok: false, reason: `invalid start ${JSON.stringify(value.start)} (expected YYYY-MM-DDTHH:mm)` };
        if (end === INVALID) return { ok: false, reason: `invalid end ${JSON.stringify(value.end)} (expected YYYY-MM-DDTHH:mm)` };
        const startMinutes = minutesOf(start.date, start.time);
        const endMinutes = minutesOf(end.date, end.time);
        if (startMinutes !== null && endMinutes !== null && endMinutes <= startMinutes) {
            return { ok: false, reason: `end ${value.end} is not after start ${value.start}` };
        }
        return { ok: true, parts: { startDate: start.date, startTime: start.time, endDate: end.date, endTime: end.time } };
    }

    _parts() {
        const { startDate, startTime, endDate, endTime } = this._state.snapshot();
        return { startDate, startTime, endDate, endTime };
    }

    _isDisabled() {
        return this._state.snapshot().availability === 'disabled';
    }

    // 另一端（可能只有日期）可能的最早與最晚時間；沒有日期時為 null
    _otherWindow(which, parts) {
        const other = which === 'start' ? 'end' : 'start';
        const day = parseIsoDay(parts[`${other}Date`]);
        if (day === null) return null;
        const time = parseTimeOfDay(parts[`${other}Time`]);
        return {
            earliest: day * DAY_MINUTES + (time ?? 0),
            latest: day * DAY_MINUTES + (time ?? DAY_MINUTES - 1)
        };
    }

    // 候選的一端（分鐘數）相對另一端、min / max、maxSpanMinutes 的問題代碼；可接受時為空字串
    _problemFor(which, candidate, parts) {
        if (this._min !== null && candidate < this._min) return 'beforeMin';
        if (this._max !== null && candidate > this._max) return 'afterMax';
        const other = this._otherWindow(which, parts);
        if (!other) return '';
        if (which === 'end' && candidate <= other.earliest) return 'order';
        if (which === 'start' && candidate >= other.latest) return 'order';
        if (this._maxSpan === null) return '';
        if (which === 'end' && candidate > other.latest + this._maxSpan) return 'maxSpan';
        if (which === 'start' && candidate < other.earliest - this._maxSpan) return 'maxSpan';
        return '';
    }

    // 與 _problemFor 一致的可接受區間 [lo, hi]（分鐘，含端點；±Infinity 表示不限）
    _interval(which, parts) {
        const other = this._otherWindow(which, parts);
        const span = this._maxSpan ?? Infinity;
        const lower = !other ? -Infinity : which === 'end' ? other.earliest + 1 : other.earliest - span;
        const upper = !other ? Infinity : which === 'end' ? other.latest + span : other.latest - 1;
        return {
            lo: Math.max(this._min ?? -Infinity, lower),
            hi: Math.min(this._max ?? Infinity, upper)
        };
    }

    _message(code) {
        if (!code) return '';
        const params = code === 'beforeMin' ? { min: this._format(this._min) }
            : code === 'afterMax' ? { max: this._format(this._max) }
                : code === 'maxSpan' ? { duration: formatDuration(this._maxSpan, 'dateTimeRangePicker') }
                    : undefined;
        return Locale.t(`dateTimeRangePicker.errors.${code}`, params);
    }

    _format(minutes) {
        const day = Math.floor(minutes / DAY_MINUTES);
        const time = minutes - day * DAY_MINUTES;
        return `${formatDayNumber(day, this.options.format === 'taiwan')} ${formatTimeOfDay(time)}`;
    }

    // 日期欄：已選時間時，只開放「該時間落在可接受區間內」的日期；未選時間時，開放區間涵蓋到的日期。
    // 時間欄：已選日期時，只開放該日期上落在區間內的時間；未選日期時不限制。
    _applyConstraints() {
        const parts = this._parts();
        ENDS.forEach((which) => {
            const { lo, hi } = this._interval(which, parts);
            const time = parseTimeOfDay(parts[`${which}Time`]);
            const minDay = time === null ? Math.floor(lo / DAY_MINUTES) : Math.ceil((lo - time) / DAY_MINUTES);
            const maxDay = time === null ? Math.floor(hi / DAY_MINUTES) : Math.floor((hi - time) / DAY_MINUTES);
            setDatePickerBounds(this._pickers[`${which}Date`], minDay, maxDay);

            const day = parseIsoDay(parts[`${which}Date`]);
            const check = day === null ? null : (minutes) => this._problemFor(which, day * DAY_MINUTES + minutes, parts);
            this._bridges[`${which}Time`].setConstraint(
                check && ((minutes) => !check(minutes)),
                check && ((minutes) => this._message(check(minutes)))
            );
        });
    }

    // 一端有日期而另一端沒有時，把另一端的日曆檢視移到同一個月份
    _alignView(which) {
        const date = this._parts()[`${which}Date`];
        const other = this._pickers[`${which === 'start' ? 'end' : 'start'}Date`];
        if (!date || other.snapshot().selectedValue || other.isOpen) return;
        const { year, month } = partsOfDayNumber(parseIsoDay(date));
        showDatePickerMonth(other, year, month - 1);
    }

    _syncControls() {
        const hasAny = Object.values(this._parts()).some(Boolean);
        const usable = !this._isDisabled() && hasAny;
        if (this._clearButton) {
            this._clearButton.disabled = !usable;
            this._clearButton.style.visibility = usable ? 'visible' : 'hidden';
        }
    }

    _applyParts(parts) {
        this._state.send('SET_PARTS', parts);
        ENDS.forEach((which) => {
            setDatePickerValue(this._pickers[`${which}Date`], parts[`${which}Date`]);
            setTimePickerValue(this._pickers[`${which}Time`], parts[`${which}Time`]);
        });
        this._applyConstraints();
        ENDS.forEach((which) => this._alignView(which));
        Object.values(this._bridges).forEach((bridge) => bridge.refresh());
        this._syncControls();
    }

    _applyDisabled() {
        const disabled = this._isDisabled();
        Object.values(this._pickers).forEach((picker) => {
            if (Boolean(picker.options.disabled) !== disabled) picker.setDisabled(disabled);
        });
        Object.values(this._bridges).forEach((bridge) => bridge.setDisabled(disabled));
        toggleAttr(this.element, 'aria-disabled', disabled ? 'true' : null);
        this._syncControls();
    }

    _emitIfChanged(previous) {
        const current = this.getValue();
        if (current.start === previous.start && current.end === previous.end) return;
        if (typeof this.options.onChange === 'function') this.options.onChange(current);
    }

    // 內部 DatePicker / TimePicker 確定了一個部分：日曆與面板已依限制擋下不合規則的選項，
    // 這裡再檢查一次完整的一端，違反時拒絕並還原
    _handlePick(which, part) {
        if (this._destroyed) return;
        const key = `${which}${part}`;
        const picker = this._pickers[key];
        const previousParts = this._parts();
        const previousValue = this.getValue();
        const raw = part === 'Date' ? picker.snapshot().selectedValue : picker.getValue();
        const parts = { ...previousParts, [key]: raw || null };
        const candidate = minutesOf(parts[`${which}Date`], parts[`${which}Time`]);
        if (candidate !== null && this._problemFor(which, candidate, parts)) {
            if (part === 'Date') setDatePickerValue(picker, previousParts[key]);
            else setTimePickerValue(picker, previousParts[key]);
            this._applyConstraints();
            return;
        }
        this._state.send('SET_PARTS', parts);
        this._applyConstraints();
        if (part === 'Date') this._alignView(which);
        this._syncControls();
        this._emitIfChanged(previousValue);
    }

    _validate(parts) {
        const t = (key, params) => Locale.t(`dateTimeRangePicker.errors.${key}`, params);
        const required = Boolean(this.options.required);
        const startEmpty = !parts.startDate && !parts.startTime;
        const endEmpty = !parts.endDate && !parts.endTime;
        if (startEmpty && endEmpty) return required ? t('required') : '';
        if (!startEmpty && !parts.startDate) return t('startDateRequired');
        if (!startEmpty && !parts.startTime) return t('startTimeRequired');
        if (!endEmpty && !parts.endDate) return t('endDateRequired');
        if (!endEmpty && !parts.endTime) return t('endTimeRequired');
        if (required && startEmpty) return t('startRequired');
        if (required && endEmpty) return t('endRequired');
        const start = minutesOf(parts.startDate, parts.startTime);
        const end = minutesOf(parts.endDate, parts.endTime);
        const both = start !== null && end !== null;
        if (both && end <= start) return t('order');
        const values = [start, end].filter((minutes) => minutes !== null);
        if (this._min !== null && values.some((minutes) => minutes < this._min)) return this._message('beforeMin');
        if (this._max !== null && values.some((minutes) => minutes > this._max)) return this._message('afterMax');
        if (both && this._maxSpan !== null && end - start > this._maxSpan) return this._message('maxSpan');
        return '';
    }

    /** 目前的值（新物件）：{ start, end }；只有日期或只有時間的一端為 null。 */
    getValue() {
        const parts = this._parts();
        return { start: joinDateTime(parts.startDate, parts.startTime), end: joinDateTime(parts.endDate, parts.endTime) };
    }

    /**
     * 設定值；不觸發 onChange。null / undefined / '' 代表清空。
     * 格式不合（非 'YYYY-MM-DDTHH:mm' 或不存在的日期時間）或結束不晚於開始時 console.warn 並保持原值。
     */
    setValue(value) {
        if (this._destroyed) return this;
        const parsed = this._parse(value);
        if (!parsed.ok) {
            console.warn(`[DateTimeRangePicker] setValue rejected: ${parsed.reason}; value unchanged.`, value);
            return this;
        }
        this._applyParts(parsed.parts);
        return this;
    }

    /** 清空兩端（含只填一半的部分）；只有值真的改變時才觸發 onChange。 */
    clear() {
        if (this._destroyed) return this;
        const previous = this.getValue();
        this._applyParts({ ...EMPTY_PARTS });
        this._emitIfChanged(previous);
        return this;
    }

    setDisabled(disabled) {
        if (this._destroyed) return this;
        this._state.send('SET_DISABLED', { disabled: Boolean(disabled) });
        this._applyDisabled();
        return this;
    }

    /** 目前的值是否通過驗證（必填、每端完整、順序、min / max、maxSpanMinutes）。 */
    isValid() {
        return this._validate(this._parts()) === '';
    }

    /** 驗證失敗的原因（依目前語系）；通過時為空字串。 */
    getValidationError() {
        return this._validate(this._parts());
    }

    /**
     * 標示欄位錯誤：兩端的日期與時間觸發框都加上紅框與 aria-invalid，訊息只顯示一則、位於範圍列下方。
     * 空訊息等同 clearError()；display:false 只標示狀態、不顯示文字。
     */
    setError(message, { display = true } = {}) {
        if (this._destroyed) return this;
        setFieldError(this, message, {
            target: [this.startDatePicker, this.startTimePicker, this.endDatePicker, this.endTimePicker]
                .map((picker) => picker.inputWrapper),
            after: this._row,
            display
        });
        return this;
    }

    /** 清除 setError 的標示與文字。 */
    clearError() {
        clearFieldError(this);
        return this;
    }

    get [FIELD_ERROR_CONTRACT]() {
        return true;
    }

    snapshot() {
        return this._state.snapshot();
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target || this._destroyed) return this;
        target.appendChild(this.element);
        this._state.send('MOUNT');
        return this;
    }

    destroy() {
        if (this._destroyed) return;
        this._destroyed = true;
        clearFieldError(this);
        Object.values(this._bridges).forEach((bridge) => bridge.destroy());
        Object.values(this._pickers).forEach((picker) => picker.destroy());
        this._offs.splice(0).forEach((off) => off());
        this._state.send('DESTROY');
        this.element.remove();
    }
}

export default DateTimeRangePicker;
