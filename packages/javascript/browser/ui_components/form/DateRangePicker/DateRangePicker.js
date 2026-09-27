/**
 * DateRangePicker — 日期範圍選擇器
 *
 * 組合兩個 DatePicker（開始／結束），值為 { start: 'YYYY-MM-DD' | null, end: 'YYYY-MM-DD' | null }。
 *
 * - 兩端互相約束：選了開始日，結束日曆只開放「開始日之後（allowSameDay 時含當天）」且不超過
 *   maxSpanDays 的日期；反之亦然。介面上無法選出順序顛倒或超出限制的範圍。
 * - 順序顛倒一律「拒絕」：setValue 收到結束早於開始的值、或快速選擇回傳這種值時，
 *   console.warn 並保持原值（不會自動對調）。
 * - setValue 對 min / max / maxSpanDays / allowSameDay 不設防（保留既有資料），
 *   違反時由 isValid() / getValidationError() 回報原因。
 * - 鍵盤：兩個觸發框依序可 Tab 到達；Enter / Space / ↓ 開啟日曆並把焦點移入，
 *   方向鍵移動日期、PageUp / PageDown 換月、Enter 選取、Esc 關閉並回到觸發框。
 *   DatePicker 本身沒有鍵盤操作，這些行為由 enhanceDatePicker() 從外部補上，不改 DatePicker 原始碼。
 */
import Locale from '../../i18n/index.js';
import './locale.js';
import { DatePicker } from '../DatePicker/index.js';
import { createComponentState } from '../../utils/component-state.js';
import { setFieldError, clearFieldError, FIELD_ERROR_CONTRACT } from '../../utils/field-error.js';
import { nextUid } from '../../utils/uid.js';

const MS_PER_DAY = 86400000;
const ISO_DAY = /^(\d{4})-(\d{2})-(\d{2})$/;
const DAY_SELECTOR = '.dp-day';
const DAY_KEYS = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 };
const OPEN_KEYS = new Set(['Enter', ' ', 'Spacebar', 'ArrowDown']);
const INVALID = Symbol('invalid');

// 觸發框取得焦點時的外框（用 box-shadow，避免覆蓋 setError 使用的 outline）
const FOCUS_RING = '0 0 0 3px rgba(var(--cl-primary-rgb), 0.25)';

const pad = (value, length = 2) => String(value).padStart(length, '0');

/** 設定或移除屬性；value 為 null / undefined 時移除。 */
export function toggleAttr(element, name, value) {
    return value === null || value === undefined
        ? element.removeAttribute(name)
        : element.setAttribute(name, value);
}

/**
 * 由年月日（月份 1–12）算出日序（1970-01-01 起算的天數）；日期不存在時回傳 null。
 * 以 UTC 計算，日光節約時間不會讓日期位移。
 */
export function dayNumberOf(year, month, day) {
    const date = new Date(0);
    date.setUTCFullYear(year, month - 1, day);
    const exists = date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
    return exists ? Math.round(date.getTime() / MS_PER_DAY) : null;
}

/** 嚴格解析 'YYYY-MM-DD'（必須是存在的日期）為日序；不合格回傳 null。 */
export function parseIsoDay(value) {
    const match = typeof value === 'string' ? ISO_DAY.exec(value) : null;
    return match ? dayNumberOf(Number(match[1]), Number(match[2]), Number(match[3])) : null;
}

/** 日序 → { year, month (1–12), day }。 */
export function partsOfDayNumber(dayNumber) {
    const date = new Date(dayNumber * MS_PER_DAY);
    return { year: date.getUTCFullYear(), month: date.getUTCMonth() + 1, day: date.getUTCDate() };
}

/** 日序 → 當地午夜的 Date（DatePicker 的 minDate / maxDate 以當地日期比較）。 */
function localDateOfDayNumber(dayNumber) {
    const { year, month, day } = partsOfDayNumber(dayNumber);
    const date = new Date(2000, 0, 1);
    date.setFullYear(year, month - 1, day);
    return date;
}

/** 當地 Date → 'YYYY-MM-DD'。 */
function isoOfLocalDate(date) {
    return `${pad(date.getFullYear(), 4)}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** min / max 選項（'YYYY-MM-DD' 或 Date）→ 日序；空值或不合格回傳 null。 */
function dayNumberOfOption(value) {
    const isDate = value instanceof Date && !Number.isNaN(value.getTime());
    return isDate ? dayNumberOf(value.getFullYear(), value.getMonth() + 1, value.getDate()) : parseIsoDay(value);
}

/** 與 DatePicker 顯示一致的日期文字：西元 YYYY/MM/DD，民國 YYY/MM/DD。 */
export function formatDayNumber(dayNumber, useROC = false) {
    const { year, month, day } = partsOfDayNumber(dayNumber);
    return `${useROC ? year - 1911 : year}/${pad(month)}/${pad(day)}`;
}

// 允許 null 的最大／最小值（null 代表不限制）
const maxOf = (a, b) => (a === null ? b : b === null ? a : Math.max(a, b));
const minOf = (a, b) => (a === null ? b : b === null ? a : Math.min(a, b));

/**
 * @internal 更新 DatePicker 的可選範圍（DatePicker README 記載的 minDate / maxDate 用法）。
 * minDay / maxDay 為日序，null 或非有限數表示不限制。日曆展開中時立即重繪；
 * 關閉中且沒有值、而目前檢視的整個月份都在範圍外時，把檢視移到最近的可選月份，
 * 避免一打開就是整個月都不能選。
 */
export function setDatePickerBounds(picker, minDay, maxDay) {
    picker.minDate = Number.isFinite(minDay) ? localDateOfDayNumber(minDay) : null;
    picker.maxDate = Number.isFinite(maxDay) ? localDateOfDayNumber(maxDay) : null;
    // 以不改變狀態的 SET_MONTH 觸發 DatePicker 重繪（只在展開時需要）
    if (picker.isOpen) picker.send('SET_MONTH', { month: picker.snapshot().currentMonth });
    else keepViewInBounds(picker, minDay, maxDay);
}

function keepViewInBounds(picker, minDay, maxDay) {
    const { currentYear, currentMonth, selectedValue } = picker.snapshot();
    const first = dayNumberOf(currentYear, currentMonth + 1, 1);
    const last = first + new Date(currentYear, currentMonth + 1, 0).getDate() - 1;
    const target = Number.isFinite(minDay) && last < minDay ? minDay
        : Number.isFinite(maxDay) && first > maxDay ? maxDay
            : null;
    if (selectedValue || target === null) return;
    const { year, month } = partsOfDayNumber(target);
    showDatePickerMonth(picker, year, month - 1);
}

/**
 * @internal 程式設定 DatePicker 的值。DatePicker.setValue 會略過超出 minDate / maxDate 的日期，
 * 這裡暫時解除範圍再設定，讓既有資料即使違反限制也能顯示（由驗證回報）。
 */
export function setDatePickerValue(picker, iso) {
    if (picker.snapshot().selectedValue === (iso || null)) return;
    const { minDate, maxDate } = picker;
    picker.minDate = null;
    picker.maxDate = null;
    picker.setValue(iso || null);
    picker.minDate = minDate;
    picker.maxDate = maxDate;
}

/** @internal 把 DatePicker 的日曆檢視移到指定年月（monthIndex 0–11），不改變選取值；有移動時回傳 true。 */
export function showDatePickerMonth(picker, year, monthIndex) {
    const state = picker.snapshot();
    if (state.currentYear === year && state.currentMonth === monthIndex) return false;
    picker.send('SET_YEAR', { year });
    picker.send('SET_MONTH', { month: monthIndex });
    return true;
}

function shiftedDate(date, key, shiftKey) {
    const year = date.getFullYear();
    const month = date.getMonth();
    const day = date.getDate();
    const pageMonths = { PageUp: shiftKey ? -12 : -1, PageDown: shiftKey ? 12 : 1 }[key];
    const clampedDay = (monthIndex) => Math.min(day, new Date(year, monthIndex + 1, 0).getDate());
    return key in DAY_KEYS ? new Date(year, month, day + DAY_KEYS[key])
        : key === 'Home' ? new Date(year, month, 1)
            : key === 'End' ? new Date(year, month + 1, 0)
                : pageMonths ? new Date(year, month + pageMonths, clampedDay(month + pageMonths))
                    : null;
}

const dayLabelFormatters = new Map();

// 日期按鈕的 aria-label：依目前語系輸出完整日期（民國格式用 ROC 曆）；Intl 不支援時退回數字格式
function dayLabelFormatter(useROC) {
    const lang = Locale.getLang();
    const key = `${lang}|${useROC}`;
    const fallback = (date) => formatDayNumber(dayNumberOf(date.getFullYear(), date.getMonth() + 1, date.getDate()), useROC);
    try {
        const formatter = dayLabelFormatters.get(key) || new Intl.DateTimeFormat(useROC ? `${lang}-u-ca-roc` : lang, {
            year: 'numeric', month: 'long', day: 'numeric', weekday: 'long'
        });
        dayLabelFormatters.set(key, formatter);
        return (date) => formatter.format(date);
    } catch (error) {
        return fallback;
    }
}

/**
 * @internal 從外部補上 DatePicker 的鍵盤操作與 ARIA（不修改 DatePicker 原始碼），
 * 供 DateRangePicker 與 DateTimeRangePicker 共用；不是公開 API。
 *
 * - 觸發框：tabindex、role="combobox"、aria-haspopup="dialog"、aria-expanded、aria-controls、aria-label、焦點外框。
 *   Enter / Space / ↓ 開啟並把焦點移入日曆；Esc 關閉；焦點以 Tab 離開觸發框（不是移進日曆）時關閉日曆。
 * - 日曆：role="dialog"；每個日期按鈕補上完整日期的 aria-label、不可選日期 aria-disabled、
 *   已選日期 aria-pressed。方向鍵 ±1 / ±7 天、Home / End 月初月底、PageUp / PageDown 換月
 *   （加 Shift 換年）、Enter / Space 選取、Esc 關閉；Tab 在「上月、年、月、下月、目前日期」間循環。
 * - 日曆關閉時（選取日期後）焦點回到觸發框。
 *
 * @param {DatePicker} picker
 * @param {{ label?: string }} options
 * @returns {{ setLabel(text: string): void, setDisabled(disabled: boolean): void, setRequired(required: boolean): void, refresh(): void, destroy(): void }}
 */
export function enhanceDatePicker(picker, { label = '' } = {}) {
    const trigger = picker.inputWrapper;
    const calendar = picker.calendar;
    const grid = picker.daysGrid;
    const offs = [];
    const on = (target, type, handler, options) => {
        target.addEventListener(type, handler, options);
        offs.push(() => target.removeEventListener(type, handler, options));
    };
    const state = { open: Boolean(picker.isOpen), returnFocus: false, disabled: false };

    const dayButtons = () => [...grid.querySelectorAll(DAY_SELECTOR)];
    const dateOfButton = (button) => {
        const { currentYear, currentMonth } = picker.snapshot();
        return new Date(currentYear, currentMonth, Number(button.dataset.day));
    };
    // 只在日曆展開時標註（關閉中的重繪不需要；開啟時 syncOpen 會補做）
    const decorateDays = () => {
        if (!picker.isOpen) return;
        const selected = picker.snapshot().selectedValue;
        const describe = dayLabelFormatter(Boolean(picker.options.useROC));
        dayButtons().forEach((button) => {
            const date = dateOfButton(button);
            button.setAttribute('aria-label', describe(date));
            toggleAttr(button, 'aria-disabled', button.dataset.disabled === 'true' ? 'true' : null);
            toggleAttr(button, 'aria-pressed', isoOfLocalDate(date) === selected ? 'true' : null);
        });
    };
    const initialDay = () => {
        const buttons = dayButtons();
        const enabled = buttons.filter((button) => button.dataset.disabled !== 'true');
        const selected = picker.snapshot().selectedValue;
        const today = isoOfLocalDate(new Date());
        const withIso = (iso) => (button) => isoOfLocalDate(dateOfButton(button)) === iso;
        return buttons.find(withIso(selected)) || enabled.find(withIso(today)) || enabled[0] || buttons[0] || null;
    };
    const focusDay = (dayOfMonth) => grid.querySelector(`${DAY_SELECTOR}[data-day="${dayOfMonth}"]`)?.focus();

    // 同步 aria-expanded；日曆由開轉關且需要時，把焦點還給觸發框
    const syncOpen = () => {
        const open = Boolean(picker.isOpen);
        const closed = state.open && !open;
        state.open = open;
        trigger.setAttribute('aria-expanded', String(open));
        if (open) decorateDays();
        if (closed && state.returnFocus && trigger.isConnected) trigger.focus();
        if (!open) state.returnFocus = false;
    };
    const close = () => {
        picker.close();
        syncOpen();
    };
    const openAndFocus = () => {
        if (state.disabled) return;
        if (!picker.isOpen) picker.open();
        syncOpen();
        if (picker.isOpen) (initialDay() || picker.prevButton)?.focus();
    };
    const trapTab = (event) => {
        const active = document.activeElement;
        const onDay = Boolean(active?.closest?.(DAY_SELECTOR)) && grid.contains(active);
        const stops = [picker.prevButton, picker.yearSelect, picker.monthSelect, picker.nextButton, onDay ? active : initialDay()]
            .filter(Boolean);
        const index = stops.indexOf(active);
        const next = event.shiftKey ? stops[(index <= 0 ? stops.length : index) - 1] : stops[(index + 1) % stops.length];
        event.preventDefault();
        next.focus();
    };

    const onTriggerKeydown = (event) => {
        if (OPEN_KEYS.has(event.key)) {
            event.preventDefault();
            openAndFocus();
        } else if (event.key === 'Escape' && picker.isOpen) {
            // 只關日曆，不讓外層（例如對話框）也因同一個 Esc 關閉
            event.preventDefault();
            event.stopPropagation();
            close();
        }
    };
    const onTriggerFocusout = (event) => {
        const next = event.relatedTarget;
        if (picker.isOpen && next && !calendar.contains(next)) close();
    };
    const onCalendarKeydown = (event) => {
        const key = event.key;
        if (key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            close();
            trigger.focus();
            return;
        }
        if (key === 'Tab') {
            trapTab(event);
            return;
        }
        const button = event.target?.closest?.(DAY_SELECTOR);
        if (!button || !grid.contains(button)) return;
        if (key === 'Enter' || key === ' ' || key === 'Spacebar') {
            event.preventDefault();
            if (button.dataset.disabled === 'true') return;
            state.returnFocus = true;
            button.click();
            syncOpen();
            return;
        }
        const target = shiftedDate(dateOfButton(button), key, event.shiftKey);
        if (!target) return;
        event.preventDefault();
        if (showDatePickerMonth(picker, target.getFullYear(), target.getMonth())) decorateDays();
        focusDay(target.getDate());
    };
    // 捕獲階段記下「點了可選日期」：DatePicker 的日期按鈕會 stopPropagation，冒泡階段收不到
    const onCalendarClickCapture = (event) => {
        const button = event.target?.closest?.(DAY_SELECTOR);
        if (button && grid.contains(button) && button.dataset.disabled !== 'true') state.returnFocus = true;
    };

    const calendarId = calendar.id || nextUid('b4a-date-calendar');
    calendar.id = calendarId;
    calendar.setAttribute('role', 'dialog');
    trigger.setAttribute('role', 'combobox');
    trigger.setAttribute('aria-haspopup', 'dialog');
    trigger.setAttribute('aria-controls', calendarId);
    trigger.setAttribute('aria-expanded', String(state.open));

    const applyLabel = (text) => {
        toggleAttr(trigger, 'aria-label', text ? String(text) : null);
        toggleAttr(calendar, 'aria-label', text ? String(text) : null);
    };
    const applyDisabled = (disabled) => {
        state.disabled = Boolean(disabled);
        trigger.tabIndex = state.disabled ? -1 : 0;
        toggleAttr(trigger, 'aria-disabled', state.disabled ? 'true' : null);
    };
    applyLabel(label);
    applyDisabled(picker.options.disabled);

    on(trigger, 'keydown', onTriggerKeydown);
    on(trigger, 'focusout', onTriggerFocusout);
    on(trigger, 'click', syncOpen);
    on(trigger, 'focus', () => { trigger.style.boxShadow = FOCUS_RING; });
    on(trigger, 'blur', () => { trigger.style.boxShadow = ''; });
    on(calendar, 'keydown', onCalendarKeydown);
    on(calendar, 'click', onCalendarClickCapture, true);

    // DatePicker 沒有開關事件：觀察它開啟時設定、關閉時移除的 data-portal，以及日期格重繪
    const portalObserver = new MutationObserver(syncOpen);
    portalObserver.observe(calendar, { attributes: true, attributeFilter: ['data-portal'] });
    const daysObserver = new MutationObserver(decorateDays);
    daysObserver.observe(grid, { childList: true });

    return {
        setLabel(text) {
            applyLabel(text);
        },
        setDisabled(disabled) {
            applyDisabled(disabled);
        },
        setRequired(required) {
            toggleAttr(trigger, 'aria-required', required ? 'true' : null);
        },
        refresh() {
            syncOpen();
        },
        destroy() {
            portalObserver.disconnect();
            daysObserver.disconnect();
            offs.splice(0).forEach((off) => off());
        }
    };
}

const EMPTY_RANGE = Object.freeze({ start: null, end: null });

function parseRangeEnd(raw) {
    const empty = raw === null || raw === undefined || raw === '';
    return empty ? null : parseIsoDay(raw) !== null ? raw : INVALID;
}

export class DateRangePicker {
    constructor(options = {}) {
        this.options = {
            value: null,                  // 初始值 { start: 'YYYY-MM-DD' | null, end: 'YYYY-MM-DD' | null }
            min: null,                    // 最早可選日期：'YYYY-MM-DD' 或 Date；null 不限
            max: null,                    // 最晚可選日期：'YYYY-MM-DD' 或 Date；null 不限
            format: 'western',            // 顯示格式（同 DatePicker）：'western' 西元 / 'taiwan' 民國
            allowSameDay: true,           // 開始與結束可為同一天
            maxSpanDays: null,            // 最長天數（含開始與結束兩天）；null 不限
            required: false,              // 必填（只影響 isValid / getValidationError）
            presets: [],                  // 快速選擇：[{ label, range: () => ({ start, end }) }]
            startPlaceholder: Locale.t('dateRangePicker.startPlaceholder'), // 開始日期提示文字（也是無障礙名稱）
            endPlaceholder: Locale.t('dateRangePicker.endPlaceholder'),     // 結束日期提示文字（也是無障礙名稱）
            separator: Locale.t('dateRangePicker.separator'),               // 兩端之間的文字
            label: '',                    // 可見標籤；群組以 aria-labelledby 指向它
            ariaLabel: '',                // 沒有可見標籤時的群組名稱；空值用 Locale 的 groupLabel
            clearable: true,              // 有值時顯示「清除」按鈕
            disabled: false,              // 停用
            width: '100%',                // 元件寬度（CSS 長度字串或數字 px）
            onChange: null,               // (value) => {}，使用者確定變更時觸發一次
            ...options
        };

        this._minDay = this._normalizeBound('min');
        this._maxDay = this._normalizeBound('max');
        this._maxSpanDays = this._normalizeSpan();
        this._offs = [];
        this._presetButtons = [];
        this._destroyed = false;

        this._state = createComponentState({
            lifecycle: 'created',
            availability: this.options.disabled ? 'disabled' : 'enabled',
            start: null,
            end: null
        }, {
            MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
            DESTROY: (state) => ({ ...state, lifecycle: 'destroyed' }),
            SET_RANGE: (state, payload) => ({ ...state, start: payload?.start ?? null, end: payload?.end ?? null }),
            SET_DISABLED: (state, payload) => ({ ...state, availability: payload?.disabled ? 'disabled' : 'enabled' })
        });

        this.element = this._createElement();

        const initial = this._parse(this.options.value);
        if (!initial.ok) console.warn(`[DateRangePicker] invalid initial value: ${initial.reason}; starting empty.`, this.options.value);
        this._applyValue(initial.ok ? initial.value : EMPTY_RANGE);
        this._applyDisabled();
    }

    _normalizeBound(name) {
        const raw = this.options[name];
        if (raw === null || raw === undefined || raw === '') return null;
        const day = dayNumberOfOption(raw);
        if (day === null) console.warn(`[DateRangePicker] ignoring invalid ${name} option (expected YYYY-MM-DD or Date):`, raw);
        return day;
    }

    _normalizeSpan() {
        const raw = this.options.maxSpanDays;
        if (raw === null || raw === undefined) return null;
        const span = Number(raw);
        if (Number.isInteger(span) && span >= 1) return span;
        console.warn('[DateRangePicker] ignoring invalid maxSpanDays option (expected an integer >= 1):', raw);
        return null;
    }

    _on(target, type, handler) {
        target.addEventListener(type, handler);
        this._offs.push(() => target.removeEventListener(type, handler));
    }

    _createElement() {
        const { label, ariaLabel, required, separator, clearable, presets, width, format } = this.options;

        const root = document.createElement('div');
        root.className = 'date-range-picker';
        root.setAttribute('role', 'group');
        root.style.cssText = 'display:block;max-width:100%;box-sizing:border-box;font-family:var(--cl-font-family);';
        root.style.width = typeof width === 'number' ? `${width}px` : String(width || '');

        if (label) {
            const labelEl = document.createElement('div');
            labelEl.className = 'date-range-picker__label';
            labelEl.id = nextUid('date-range-picker-label');
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
            root.setAttribute('aria-label', String(ariaLabel || Locale.t('dateRangePicker.groupLabel')));
        }

        const row = document.createElement('div');
        row.className = 'date-range-picker__row';
        row.style.cssText = 'display:flex;flex-wrap:wrap;align-items:center;gap:8px;min-width:0;';

        const createEnd = (which, placeholder) => {
            const slot = document.createElement('div');
            slot.className = `date-range-picker__${which}`;
            slot.style.cssText = 'flex:1 1 140px;min-width:0;';
            const picker = new DatePicker({
                placeholder,
                format,
                onChange: () => this._handlePick(which)
            });
            picker.mount(slot);
            picker.element.style.width = '100%';
            row.appendChild(slot);
            return picker;
        };

        this.startPicker = createEnd('start', this.options.startPlaceholder);

        const separatorEl = document.createElement('span');
        separatorEl.className = 'date-range-picker__separator';
        separatorEl.textContent = String(separator ?? '');
        separatorEl.style.cssText = 'flex:0 0 auto;font-size:var(--cl-font-size-md);color:var(--cl-text-secondary);';
        row.appendChild(separatorEl);

        this.endPicker = createEnd('end', this.options.endPlaceholder);

        this._startBridge = enhanceDatePicker(this.startPicker, { label: this.options.startPlaceholder });
        this._endBridge = enhanceDatePicker(this.endPicker, { label: this.options.endPlaceholder });
        this._startBridge.setRequired(required);
        this._endBridge.setRequired(required);

        this._clearButton = null;
        if (clearable) {
            const clearButton = document.createElement('button');
            clearButton.type = 'button';
            clearButton.className = 'date-range-picker__clear';
            clearButton.textContent = Locale.t('dateRangePicker.clear');
            clearButton.style.cssText = 'flex:0 0 auto;padding:4px 10px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-sm);background:var(--cl-bg);color:var(--cl-text-secondary);font-family:inherit;font-size:var(--cl-font-size-sm);cursor:pointer;';
            this._on(clearButton, 'click', () => {
                this.clear();
                this.startPicker.inputWrapper.focus();
            });
            row.appendChild(clearButton);
            this._clearButton = clearButton;
        }

        root.appendChild(row);
        this._row = row;

        const presetList = Array.isArray(presets) ? presets.filter((preset) => preset && preset.label !== undefined) : [];
        this._presetsEl = null;
        if (presetList.length) {
            const presetsEl = document.createElement('div');
            presetsEl.className = 'date-range-picker__presets';
            presetsEl.setAttribute('role', 'group');
            presetsEl.setAttribute('aria-label', Locale.t('dateRangePicker.presetsLabel'));
            presetsEl.style.cssText = 'display:flex;flex-wrap:wrap;gap:6px;margin-top:8px;';
            presetList.forEach((preset, index) => {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'date-range-picker__preset';
                button.dataset.index = String(index);
                button.textContent = String(preset.label);
                button.style.cssText = 'padding:2px 10px;border:1px solid var(--cl-border-light);border-radius:var(--cl-radius-pill);background:var(--cl-bg-secondary);color:var(--cl-primary);font-family:inherit;font-size:var(--cl-font-size-sm);cursor:pointer;';
                this._on(button, 'click', () => this._applyPreset(preset));
                presetsEl.appendChild(button);
                this._presetButtons.push(button);
            });
            root.appendChild(presetsEl);
            this._presetsEl = presetsEl;
        }

        return root;
    }

    _parse(value) {
        if (value === null || value === undefined || value === '') return { ok: true, value: { ...EMPTY_RANGE } };
        if (typeof value !== 'object' || Array.isArray(value)) return { ok: false, reason: 'expected { start, end }' };
        const start = parseRangeEnd(value.start);
        const end = parseRangeEnd(value.end);
        if (start === INVALID) return { ok: false, reason: `invalid start ${JSON.stringify(value.start)} (expected YYYY-MM-DD)` };
        if (end === INVALID) return { ok: false, reason: `invalid end ${JSON.stringify(value.end)} (expected YYYY-MM-DD)` };
        if (start && end && end < start) return { ok: false, reason: `end ${end} is before start ${start}` };
        return { ok: true, value: { start, end } };
    }

    _isDisabled() {
        return this._state.snapshot().availability === 'disabled';
    }

    // 結束日可選範圍：開始日（不允許同一天時為隔天）起，最多 maxSpanDays 天，再與 min / max 取交集
    _endBounds(startDay) {
        const hasStart = startDay !== null;
        const lower = hasStart ? startDay + (this.options.allowSameDay ? 0 : 1) : null;
        const upper = hasStart && this._maxSpanDays !== null ? startDay + this._maxSpanDays - 1 : null;
        return { min: maxOf(this._minDay, lower), max: minOf(this._maxDay, upper) };
    }

    _startBounds(endDay) {
        const hasEnd = endDay !== null;
        const upper = hasEnd ? endDay - (this.options.allowSameDay ? 0 : 1) : null;
        const lower = hasEnd && this._maxSpanDays !== null ? endDay - this._maxSpanDays + 1 : null;
        return { min: maxOf(this._minDay, lower), max: minOf(this._maxDay, upper) };
    }

    _applyConstraints() {
        const { start, end } = this._state.snapshot();
        const startBounds = this._startBounds(parseIsoDay(end));
        const endBounds = this._endBounds(parseIsoDay(start));
        setDatePickerBounds(this.startPicker, startBounds.min, startBounds.max);
        setDatePickerBounds(this.endPicker, endBounds.min, endBounds.max);
    }

    // 一端有值而另一端空白時，把另一端的日曆檢視移到同一個月份
    _alignView(which) {
        const value = this._state.snapshot()[which];
        const other = which === 'start' ? this.endPicker : this.startPicker;
        if (!value || other.snapshot().selectedValue || other.isOpen) return;
        const { year, month } = partsOfDayNumber(parseIsoDay(value));
        showDatePickerMonth(other, year, month - 1);
    }

    _syncControls() {
        const { start, end } = this._state.snapshot();
        const usable = !this._isDisabled() && Boolean(start || end);
        if (this._clearButton) {
            this._clearButton.disabled = !usable;
            this._clearButton.style.visibility = usable ? 'visible' : 'hidden';
        }
    }

    _applyValue(range) {
        this._state.send('SET_RANGE', range);
        setDatePickerValue(this.startPicker, range.start);
        setDatePickerValue(this.endPicker, range.end);
        this._applyConstraints();
        this._alignView('start');
        this._alignView('end');
        this._startBridge.refresh();
        this._endBridge.refresh();
        this._syncControls();
    }

    _applyDisabled() {
        const disabled = this._isDisabled();
        // 狀態相同時不呼叫，省下 DatePicker 一次整月重繪
        [this.startPicker, this.endPicker].forEach((picker) => {
            if (Boolean(picker.options.disabled) !== disabled) picker.setDisabled(disabled);
        });
        this._startBridge.setDisabled(disabled);
        this._endBridge.setDisabled(disabled);
        this._presetButtons.forEach((button) => { button.disabled = disabled; });
        toggleAttr(this.element, 'aria-disabled', disabled ? 'true' : null);
        this._syncControls();
    }

    _emitIfChanged(previous) {
        const current = this.getValue();
        if (current.start === previous.start && current.end === previous.end) return;
        if (typeof this.options.onChange === 'function') this.options.onChange(current);
    }

    // 內部 DatePicker 選了日期：日曆已依另一端約束，這裡再檢查一次，違反時拒絕並還原
    _handlePick(which) {
        if (this._destroyed) return;
        const picker = which === 'start' ? this.startPicker : this.endPicker;
        const previous = this.getValue();
        const next = { ...previous, [which]: picker.snapshot().selectedValue || null };
        const day = parseIsoDay(next[which]);
        const bounds = which === 'start' ? this._startBounds(parseIsoDay(next.end)) : this._endBounds(parseIsoDay(next.start));
        const allowed = day === null || ((bounds.min === null || day >= bounds.min) && (bounds.max === null || day <= bounds.max));
        if (!allowed) {
            setDatePickerValue(picker, previous[which]);
            this._applyConstraints();
            return;
        }
        this._state.send('SET_RANGE', next);
        this._applyConstraints();
        this._alignView(which);
        this._syncControls();
        this._emitIfChanged(previous);
    }

    _applyPreset(preset) {
        if (this._destroyed || this._isDisabled()) return;
        const name = String(preset.label);
        let raw;
        try {
            raw = typeof preset.range === 'function' ? preset.range() : preset.range;
        } catch (error) {
            console.warn(`[DateRangePicker] preset "${name}" range() threw; ignored.`, error);
            return;
        }
        const parsed = this._parse(raw);
        const problem = parsed.ok ? this._validate(parsed.value) : parsed.reason;
        if (problem) {
            console.warn(`[DateRangePicker] preset "${name}" rejected: ${problem}`, raw);
            return;
        }
        const previous = this.getValue();
        this._applyValue(parsed.value);
        this._emitIfChanged(previous);
    }

    _formatDay(day) {
        return formatDayNumber(day, this.options.format === 'taiwan');
    }

    _validate({ start, end }) {
        const t = (key, params) => Locale.t(`dateRangePicker.errors.${key}`, params);
        const required = Boolean(this.options.required);
        if (!start && !end) return required ? t('required') : '';
        if (required && !start) return t('startRequired');
        if (required && !end) return t('endRequired');
        const startDay = parseIsoDay(start);
        const endDay = parseIsoDay(end);
        const both = startDay !== null && endDay !== null;
        if (both && endDay < startDay) return t('order');
        if (both && endDay === startDay && !this.options.allowSameDay) return t('sameDay');
        const days = [startDay, endDay].filter((day) => day !== null);
        if (this._minDay !== null && days.some((day) => day < this._minDay)) return t('beforeMin', { min: this._formatDay(this._minDay) });
        if (this._maxDay !== null && days.some((day) => day > this._maxDay)) return t('afterMax', { max: this._formatDay(this._maxDay) });
        if (both && this._maxSpanDays !== null && endDay - startDay + 1 > this._maxSpanDays) return t('maxSpan', { days: this._maxSpanDays });
        return '';
    }

    /** 目前的值（新物件）：{ start, end }。 */
    getValue() {
        const { start, end } = this._state.snapshot();
        return { start, end };
    }

    /**
     * 設定值；不觸發 onChange。null / undefined / '' 代表清空。
     * 格式不合（非 'YYYY-MM-DD' 或不存在的日期）或結束早於開始時 console.warn 並保持原值。
     */
    setValue(value) {
        if (this._destroyed) return this;
        const parsed = this._parse(value);
        if (!parsed.ok) {
            console.warn(`[DateRangePicker] setValue rejected: ${parsed.reason}; value unchanged.`, value);
            return this;
        }
        this._applyValue(parsed.value);
        return this;
    }

    /** 清空兩端；只有值真的改變時才觸發 onChange。 */
    clear() {
        if (this._destroyed) return this;
        const previous = this.getValue();
        this._applyValue({ ...EMPTY_RANGE });
        this._emitIfChanged(previous);
        return this;
    }

    setDisabled(disabled) {
        if (this._destroyed) return this;
        this._state.send('SET_DISABLED', { disabled: Boolean(disabled) });
        this._applyDisabled();
        return this;
    }

    /** 目前的值是否通過驗證（必填、順序、同一天、min / max、maxSpanDays）。 */
    isValid() {
        return this._validate(this._state.snapshot()) === '';
    }

    /** 驗證失敗的原因（依目前語系）；通過時為空字串。 */
    getValidationError() {
        return this._validate(this._state.snapshot());
    }

    /**
     * 標示欄位錯誤：兩端觸發框都加上紅框與 aria-invalid，訊息只顯示一則、位於範圍列下方。
     * 空訊息等同 clearError()；display:false 只標示狀態、不顯示文字。
     */
    setError(message, { display = true } = {}) {
        if (this._destroyed) return this;
        setFieldError(this, message, {
            target: [this.startPicker.inputWrapper, this.endPicker.inputWrapper],
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
        this._startBridge.destroy();
        this._endBridge.destroy();
        this.startPicker.destroy();
        this.endPicker.destroy();
        this._offs.splice(0).forEach((off) => off());
        this._state.send('DESTROY');
        this.element.remove();
    }
}

export default DateRangePicker;
