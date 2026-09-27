/**
 * TimeRangePicker — 時間範圍選擇器
 *
 * 組合兩個 TimePicker（開始／結束），值為 { start: 'HH:mm' | null, end: 'HH:mm' | null }。
 *
 * - 兩端互相約束：選了開始時間，結束端面板中不符合規則（順序、最短／最長時間長度）的
 *   小時與分鐘會標成不可選，無法點選，也無法按「確認」送出；反之亦然。
 * - allowOvernight: false（預設）時結束必須晚於開始；true 時結束早於開始代表隔日，
 *   並在結束端旁顯示「隔日」；兩種模式下結束都不可等於開始。
 * - 順序錯誤一律「拒絕」：setValue 收到不合規則的順序時 console.warn 並保持原值（不會自動對調）。
 *   時間長度限制則不設防（保留既有資料），由 isValid() / getValidationError() 回報。
 * - TimePicker 本身沒有鍵盤操作與可選範圍，這些由 enhanceTimePicker() 從外部補上，不改 TimePicker 原始碼。
 */
import Locale from '../../i18n/index.js';
import './locale.js';
import { TimePicker } from '../TimePicker/TimePicker.js';
import { createComponentState } from '../../utils/component-state.js';
import { setFieldError, clearFieldError, FIELD_ERROR_CONTRACT } from '../../utils/field-error.js';
import { nextUid } from '../../utils/uid.js';

/** 一天的分鐘數。 */
export const DAY_MINUTES = 1440;

const ISO_TIME = /^([01]\d|2[0-3]):([0-5]\d)$/;
const ITEM_SELECTOR = '.timepicker__item';
const OPEN_KEYS = new Set(['Enter', ' ', 'Spacebar', 'ArrowDown']);
const LIST_KEYS = new Set(['ArrowUp', 'ArrowDown', 'Home', 'End', 'PageUp', 'PageDown']);
const FOCUS_RING = '0 0 0 3px rgba(var(--cl-primary-rgb), 0.25)';
const INVALID = Symbol('invalid');
const EMPTY_RANGE = Object.freeze({ start: null, end: null });

function toggleAttr(element, name, value) {
    return value === null || value === undefined
        ? element.removeAttribute(name)
        : element.setAttribute(name, value);
}

/** 嚴格解析 'HH:mm'（00:00–23:59，兩位數小時）為當日分鐘數；不合格回傳 null。 */
export function parseTimeOfDay(value) {
    const match = typeof value === 'string' ? ISO_TIME.exec(value) : null;
    return match ? Number(match[1]) * 60 + Number(match[2]) : null;
}

/** 當日分鐘數 → 'HH:mm'。 */
export function formatTimeOfDay(minutes) {
    return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
}

/** 分鐘數 → 「1 小時 30 分鐘」這類文字；單位字串取自 `${namespace}.units.*`。 */
export function formatDuration(totalMinutes, namespace) {
    const days = Math.floor(totalMinutes / DAY_MINUTES);
    const hours = Math.floor((totalMinutes % DAY_MINUTES) / 60);
    const minutes = totalMinutes % 60;
    return [
        days ? Locale.t(`${namespace}.units.day`, { n: days }) : '',
        hours ? Locale.t(`${namespace}.units.hour`, { n: hours }) : '',
        minutes || !(days || hours) ? Locale.t(`${namespace}.units.minute`, { n: minutes }) : ''
    ].filter(Boolean).join(' ');
}

/** @internal minuteStep 須為 1–60 的整數（TimePicker 遇到 0 會無限迴圈）；不合格時警告並用 fallback。 */
export function normalizeMinuteStep(value, fallback, owner) {
    const step = Number(value);
    const valid = Number.isInteger(step) && step >= 1 && step <= 60;
    if (!valid) console.warn(`[${owner}] ignoring invalid minuteStep (expected an integer 1-60):`, value);
    return valid ? step : fallback;
}

/** @internal 程式設定 TimePicker 的值；值相同時不動作（省下一次重繪）。 */
export function setTimePickerValue(picker, value) {
    const next = value || null;
    if ((picker.getValue() || null) === next) return;
    picker.setValue(next);
}

/**
 * @internal 從外部補上 TimePicker 的鍵盤操作、ARIA 與可選時間限制（不修改 TimePicker 原始碼），
 * 供 TimeRangePicker 與 DateTimeRangePicker 共用；不是公開 API。
 *
 * - 觸發框：tabindex、role="combobox"、aria-haspopup="dialog"、aria-expanded、aria-controls、aria-label、焦點外框。
 *   Enter / Space / ↓ 開啟並把焦點移到「小時」清單；Esc 關閉；以 Tab 離開觸發框時關閉面板。
 * - 面板：role="dialog"；小時、分鐘兩欄為 role="listbox"（可 Tab 到達），項目為 role="option"，
 *   以 aria-selected / aria-activedescendant 表示目前選擇、aria-disabled 表示不可選。
 *   ↑ ↓ 移到上／下一個可選值、Home / End 第一個／最後一個、PageUp / PageDown 跳 5 個、
 *   ← → 切換小時與分鐘欄、Enter 確認、Esc 關閉；Tab 在「小時、分鐘、確認」之間循環。
 * - isAllowed(minutes) 回傳 false 的時間：該小時（所有分鐘都不可選時）與分鐘標成不可選且點不了；
 *   草稿時間不可選時「確認」被擋下，面板內以 role="alert" 顯示 describe(minutes) 的原因。
 *
 * @param {TimePicker} picker
 * @param {{ label?: string, isAllowed?: ((minutes: number) => boolean) | null, describe?: ((minutes: number) => string) | null }} options
 */
export function enhanceTimePicker(picker, { label = '', isAllowed = null, describe = null } = {}) {
    const trigger = picker.inputWrapper;
    const panel = picker.panel;
    const confirmButton = picker.confirmButton;
    const columns = { hour: picker.hourColumn, minute: picker.minuteColumn };
    const lists = [columns.hour.scrollContainer, columns.minute.scrollContainer];
    const offs = [];
    const on = (target, type, handler, options) => {
        target.addEventListener(type, handler, options);
        offs.push(() => target.removeEventListener(type, handler, options));
    };
    const state = { open: Boolean(picker.isOpen), returnFocus: false, disabled: false, isAllowed, describe };

    const hint = document.createElement('div');
    hint.className = 'b4a-range-hint';
    hint.setAttribute('role', 'alert');
    hint.style.cssText = 'display:none;margin-top:8px;font-size:var(--cl-font-size-sm);line-height:1.4;color:var(--cl-danger);';
    panel.appendChild(hint);

    const hourValues = columns.hour.items.map((item) => Number(item.dataset.value));
    const minuteValues = columns.minute.items.map((item) => Number(item.dataset.value));
    const allowedAt = (hour, minute) => !state.isAllowed || Boolean(state.isAllowed(hour * 60 + minute));
    const hourAllowed = (hour) => minuteValues.some((minute) => allowedAt(hour, minute));
    const minuteAllowed = (minute, hour) => (hour === null
        ? hourValues.some((candidate) => allowedAt(candidate, minute))
        : allowedAt(hour, minute));
    const allowedValues = (type, draftHour) => (type === 'hour'
        ? hourValues.filter(hourAllowed)
        : minuteValues.filter((minute) => minuteAllowed(minute, draftHour)));

    const decorateColumn = (type, allowed, selectedValue) => {
        let activeId = null;
        columns[type].items.forEach((item) => {
            const value = Number(item.dataset.value);
            const ok = allowed(value);
            item.setAttribute('aria-selected', String(value === selectedValue));
            toggleAttr(item, 'aria-disabled', ok ? null : 'true');
            item.style.opacity = ok ? '' : '0.35';
            item.style.cursor = ok ? 'pointer' : 'not-allowed';
            if (value === selectedValue) activeId = item.id;
        });
        toggleAttr(columns[type].scrollContainer, 'aria-activedescendant', activeId);
    };
    // 只在面板展開時標註（開啟時 syncOpen 會補做）
    const decorate = () => {
        if (!picker.isOpen) return;
        const { draftHour, draftMinute } = picker.snapshot();
        decorateColumn('hour', hourAllowed, draftHour);
        decorateColumn('minute', (minute) => minuteAllowed(minute, draftHour), draftMinute);
    };
    // 把目前選擇捲進清單可視範圍（只捲清單本身，不捲動頁面）
    const reveal = (type) => {
        const list = columns[type].scrollContainer;
        const item = list.querySelector('[aria-selected="true"]');
        if (!item) return;
        const top = item.offsetTop - list.offsetTop;
        const bottom = top + item.offsetHeight;
        if (top < list.scrollTop) list.scrollTop = top;
        else if (bottom > list.scrollTop + list.clientHeight) list.scrollTop = bottom - list.clientHeight;
    };
    const showHint = (message) => {
        hint.textContent = message || '';
        hint.style.display = message ? 'block' : 'none';
    };
    const blockedReason = () => {
        const { draftHour, draftMinute } = picker.snapshot();
        if (draftHour === null || draftMinute === null || allowedAt(draftHour, draftMinute)) return '';
        const reason = state.describe ? state.describe(draftHour * 60 + draftMinute) : '';
        return reason || Locale.t('timeRangePicker.unavailable');
    };

    // 同步 aria-expanded；面板由開轉關且需要時，把焦點還給觸發框
    const syncOpen = () => {
        const open = Boolean(picker.isOpen);
        const closed = state.open && !open;
        state.open = open;
        trigger.setAttribute('aria-expanded', String(open));
        if (open) decorate();
        if (closed) showHint('');
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
        if (!picker.isOpen) return;
        reveal('hour');
        reveal('minute');
        lists[0].focus();
    };
    const targetValue = (type, key) => {
        const { draftHour, draftMinute } = picker.snapshot();
        const current = type === 'hour' ? draftHour : draftMinute;
        const allowed = allowedValues(type, draftHour);
        const after = allowed.filter((value) => current === null || value > current);
        const before = allowed.filter((value) => current === null || value < current);
        const first = allowed[0];
        const last = allowed[allowed.length - 1];
        return {
            ArrowDown: current === null ? first : after[0],
            ArrowUp: current === null ? last : before[before.length - 1],
            Home: first,
            End: last,
            PageDown: current === null ? first : after[Math.min(4, after.length - 1)],
            PageUp: current === null ? last : before[Math.max(0, before.length - 5)]
        }[key];
    };
    const select = (type, value) => {
        if (value === undefined) return;
        picker.send(type === 'hour' ? 'SELECT_HOUR' : 'SELECT_MINUTE', { value });
        showHint('');
        decorate();
        reveal(type);
    };
    const trapTab = (event) => {
        const stops = [...lists, confirmButton];
        const index = stops.indexOf(document.activeElement);
        const next = event.shiftKey ? stops[(index <= 0 ? stops.length : index) - 1] : stops[(index + 1) % stops.length];
        event.preventDefault();
        next.focus();
    };

    const onTriggerKeydown = (event) => {
        if (OPEN_KEYS.has(event.key)) {
            event.preventDefault();
            openAndFocus();
        } else if (event.key === 'Escape' && picker.isOpen) {
            // 只關面板，不讓外層（例如對話框）也因同一個 Esc 關閉
            event.preventDefault();
            event.stopPropagation();
            close();
        }
    };
    const onTriggerFocusout = (event) => {
        const next = event.relatedTarget;
        if (picker.isOpen && next && !panel.contains(next)) close();
    };
    const onListKeydown = (event) => {
        const type = event.currentTarget === lists[0] ? 'hour' : 'minute';
        if (LIST_KEYS.has(event.key)) {
            event.preventDefault();
            select(type, targetValue(type, event.key));
        } else if (event.key === 'ArrowRight' || event.key === 'ArrowLeft') {
            event.preventDefault();
            lists[event.key === 'ArrowRight' ? 1 : 0].focus();
        } else if (event.key === 'Enter') {
            event.preventDefault();
            confirmButton.click();
            syncOpen();
        }
    };
    const onPanelKeydown = (event) => {
        if (event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            close();
            trigger.focus();
        } else if (event.key === 'Tab') {
            trapTab(event);
        }
    };
    // 捕獲階段：擋下不可選的項目與不合規則的「確認」（在 TimePicker 自己的 click 處理之前）
    const onPanelClickCapture = (event) => {
        const item = event.target?.closest?.(ITEM_SELECTOR);
        if (item && panel.contains(item)) {
            const isHour = columns.hour.items.includes(item);
            const value = Number(item.dataset.value);
            const ok = isHour ? hourAllowed(value) : minuteAllowed(value, picker.snapshot().draftHour);
            if (!ok) {
                event.stopPropagation();
                event.preventDefault();
            }
            return;
        }
        if (!confirmButton.contains(event.target)) return;
        const reason = blockedReason();
        state.returnFocus = !reason;
        if (!reason) return;
        event.stopPropagation();
        event.preventDefault();
        showHint(reason);
    };
    const onPanelClick = () => {
        showHint('');
        syncOpen();
    };

    const panelId = panel.id || nextUid('b4a-time-panel');
    panel.id = panelId;
    panel.setAttribute('role', 'dialog');
    trigger.setAttribute('role', 'combobox');
    trigger.setAttribute('aria-haspopup', 'dialog');
    trigger.setAttribute('aria-controls', panelId);
    trigger.setAttribute('aria-expanded', String(state.open));
    [['hour', Locale.t('timePicker.hour')], ['minute', Locale.t('timePicker.minute')]].forEach(([type, name]) => {
        const list = columns[type].scrollContainer;
        list.setAttribute('role', 'listbox');
        list.setAttribute('aria-label', name);
        list.tabIndex = 0;
        columns[type].items.forEach((item) => {
            item.setAttribute('role', 'option');
            item.id = item.id || nextUid(`b4a-time-${type}`);
        });
    });

    const applyLabel = (text) => {
        toggleAttr(trigger, 'aria-label', text ? String(text) : null);
        toggleAttr(panel, 'aria-label', text ? String(text) : null);
    };
    const applyDisabled = (disabled) => {
        state.disabled = Boolean(disabled);
        trigger.tabIndex = state.disabled ? -1 : 0;
        toggleAttr(trigger, 'aria-disabled', state.disabled ? 'true' : null);
    };
    applyLabel(label);
    applyDisabled(picker.options.disabled);

    [trigger, ...lists].forEach((element) => {
        on(element, 'focus', () => { element.style.boxShadow = FOCUS_RING; });
        on(element, 'blur', () => { element.style.boxShadow = ''; });
    });
    on(trigger, 'keydown', onTriggerKeydown);
    on(trigger, 'focusout', onTriggerFocusout);
    on(trigger, 'click', syncOpen);
    lists.forEach((list) => on(list, 'keydown', onListKeydown));
    on(panel, 'keydown', onPanelKeydown);
    on(panel, 'click', onPanelClickCapture, true);
    on(panel, 'click', onPanelClick);

    // TimePicker 沒有開關事件：觀察它開啟時設定、關閉時移除的 data-portal
    const portalObserver = new MutationObserver(syncOpen);
    portalObserver.observe(panel, { attributes: true, attributeFilter: ['data-portal'] });

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
        setConstraint(nextIsAllowed, nextDescribe) {
            state.isAllowed = typeof nextIsAllowed === 'function' ? nextIsAllowed : null;
            state.describe = typeof nextDescribe === 'function' ? nextDescribe : null;
            decorate();
        },
        refresh() {
            syncOpen();
        },
        destroy() {
            portalObserver.disconnect();
            offs.splice(0).forEach((off) => off());
            hint.remove();
        }
    };
}

function parseRangeEnd(raw) {
    const empty = raw === null || raw === undefined || raw === '';
    return empty ? null : parseTimeOfDay(raw) !== null ? raw : INVALID;
}

export class TimeRangePicker {
    constructor(options = {}) {
        this.options = {
            value: null,                  // 初始值 { start: 'HH:mm' | null, end: 'HH:mm' | null }
            minuteStep: 1,                // 分鐘欄間隔（同 TimePicker），1–60 的整數
            allowOvernight: false,        // true：結束早於開始代表隔日（跨午夜）
            minDurationMinutes: 0,        // 最短時間長度（分鐘）
            maxDurationMinutes: null,     // 最長時間長度（分鐘）；null 不限
            required: false,              // 必填（只影響 isValid / getValidationError）
            startPlaceholder: Locale.t('timeRangePicker.startPlaceholder'), // 開始時間提示文字（也是無障礙名稱）
            endPlaceholder: Locale.t('timeRangePicker.endPlaceholder'),     // 結束時間提示文字（也是無障礙名稱）
            separator: Locale.t('timeRangePicker.separator'),               // 兩端之間的文字
            label: '',                    // 可見標籤；群組以 aria-labelledby 指向它
            ariaLabel: '',                // 沒有可見標籤時的群組名稱；空值用 Locale 的 groupLabel
            clearable: true,              // 有值時顯示「清除」按鈕
            disabled: false,              // 停用
            width: '100%',                // 元件寬度（CSS 長度字串或數字 px）
            onChange: null,               // (value) => {}，使用者確定變更時觸發一次
            ...options
        };

        this._minuteStep = normalizeMinuteStep(this.options.minuteStep, 1, 'TimeRangePicker');
        this._minDuration = this._normalizeDuration('minDurationMinutes', 0, 0);
        this._maxDuration = this._normalizeDuration('maxDurationMinutes', null, 1);
        this._offs = [];
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
        if (!initial.ok) console.warn(`[TimeRangePicker] invalid initial value: ${initial.reason}; starting empty.`, this.options.value);
        this._applyValue(initial.ok ? initial.value : EMPTY_RANGE);
        this._applyDisabled();
    }

    _normalizeDuration(name, fallback, lowest) {
        const raw = this.options[name];
        if (raw === null || raw === undefined) return fallback;
        const minutes = Number(raw);
        if (Number.isInteger(minutes) && minutes >= lowest) return minutes;
        console.warn(`[TimeRangePicker] ignoring invalid ${name} (expected an integer >= ${lowest}):`, raw);
        return fallback;
    }

    _on(target, type, handler) {
        target.addEventListener(type, handler);
        this._offs.push(() => target.removeEventListener(type, handler));
    }

    _createElement() {
        const { label, ariaLabel, required, separator, clearable, width } = this.options;

        const root = document.createElement('div');
        root.className = 'time-range-picker';
        root.setAttribute('role', 'group');
        root.style.cssText = 'display:block;max-width:100%;box-sizing:border-box;font-family:var(--cl-font-family);';
        root.style.width = typeof width === 'number' ? `${width}px` : String(width || '');

        if (label) {
            const labelEl = document.createElement('div');
            labelEl.className = 'time-range-picker__label';
            labelEl.id = nextUid('time-range-picker-label');
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
            root.setAttribute('aria-label', String(ariaLabel || Locale.t('timeRangePicker.groupLabel')));
        }

        const row = document.createElement('div');
        row.className = 'time-range-picker__row';
        row.style.cssText = 'display:flex;flex-wrap:wrap;align-items:center;gap:8px;min-width:0;';

        const createEnd = (which, placeholder) => {
            const slot = document.createElement('div');
            slot.className = `time-range-picker__${which}`;
            slot.style.cssText = 'flex:1 1 120px;min-width:0;';
            const picker = new TimePicker({
                placeholder,
                minuteStep: this._minuteStep,
                onChange: () => this._handlePick(which)
            });
            picker.mount(slot);
            row.appendChild(slot);
            return picker;
        };

        this.startPicker = createEnd('start', this.options.startPlaceholder);

        const separatorEl = document.createElement('span');
        separatorEl.className = 'time-range-picker__separator';
        separatorEl.textContent = String(separator ?? '');
        separatorEl.style.cssText = 'flex:0 0 auto;font-size:var(--cl-font-size-md);color:var(--cl-text-secondary);';
        row.appendChild(separatorEl);

        this.endPicker = createEnd('end', this.options.endPlaceholder);

        const nextDay = document.createElement('span');
        nextDay.className = 'time-range-picker__next-day';
        nextDay.textContent = Locale.t('timeRangePicker.nextDay');
        nextDay.style.cssText = 'flex:0 0 auto;padding:1px 8px;border-radius:var(--cl-radius-pill);background:var(--cl-bg-secondary);color:var(--cl-text-secondary);font-size:var(--cl-font-size-sm);';
        nextDay.style.display = 'none';
        row.appendChild(nextDay);
        this._nextDayEl = nextDay;

        this._startBridge = enhanceTimePicker(this.startPicker, { label: this.options.startPlaceholder });
        this._endBridge = enhanceTimePicker(this.endPicker, { label: this.options.endPlaceholder });
        this._startBridge.setRequired(required);
        this._endBridge.setRequired(required);

        this._clearButton = null;
        if (clearable) {
            const clearButton = document.createElement('button');
            clearButton.type = 'button';
            clearButton.className = 'time-range-picker__clear';
            clearButton.textContent = Locale.t('timeRangePicker.clear');
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
        return root;
    }

    _parse(value) {
        if (value === null || value === undefined || value === '') return { ok: true, value: { ...EMPTY_RANGE } };
        if (typeof value !== 'object' || Array.isArray(value)) return { ok: false, reason: 'expected { start, end }' };
        const start = parseRangeEnd(value.start);
        const end = parseRangeEnd(value.end);
        if (start === INVALID) return { ok: false, reason: `invalid start ${JSON.stringify(value.start)} (expected HH:mm)` };
        if (end === INVALID) return { ok: false, reason: `invalid end ${JSON.stringify(value.end)} (expected HH:mm)` };
        const problem = start && end ? this._problem(parseTimeOfDay(start), parseTimeOfDay(end)) : '';
        if (problem === 'order') return { ok: false, reason: `end ${end} is not after start ${start} (allowOvernight is false)` };
        if (problem === 'sameTime') return { ok: false, reason: `end ${end} equals start ${start}` };
        return { ok: true, value: { start, end } };
    }

    _isDisabled() {
        return this._state.snapshot().availability === 'disabled';
    }

    // 兩端時間（當日分鐘數）的規則檢查；回傳錯誤代碼或空字串
    _problem(start, end) {
        const overnight = Boolean(this.options.allowOvernight);
        if (!overnight && end <= start) return 'order';
        if (overnight && end === start) return 'sameTime';
        const duration = overnight ? (end - start + DAY_MINUTES) % DAY_MINUTES : end - start;
        if (duration < this._minDuration) return 'minDuration';
        if (this._maxDuration !== null && duration > this._maxDuration) return 'maxDuration';
        return '';
    }

    _message(code) {
        if (!code) return '';
        const limit = code === 'minDuration' ? this._minDuration : code === 'maxDuration' ? this._maxDuration : null;
        const params = limit === null ? undefined : { duration: formatDuration(limit, 'timeRangePicker') };
        return Locale.t(`timeRangePicker.errors.${code}`, params);
    }

    // 依另一端限制本端面板可選的時間；check(minutes) 回傳錯誤代碼（空字串 = 可選）
    _applyConstraints() {
        const { start, end } = this._state.snapshot();
        const startMinutes = parseTimeOfDay(start);
        const endMinutes = parseTimeOfDay(end);
        const constrain = (bridge, check) => bridge.setConstraint(
            check && ((minutes) => !check(minutes)),
            check && ((minutes) => this._message(check(minutes)))
        );
        constrain(this._startBridge, endMinutes === null ? null : (minutes) => this._problem(minutes, endMinutes));
        constrain(this._endBridge, startMinutes === null ? null : (minutes) => this._problem(startMinutes, minutes));
    }

    _syncControls() {
        const { start, end } = this._state.snapshot();
        const usable = !this._isDisabled() && Boolean(start || end);
        if (this._clearButton) {
            this._clearButton.disabled = !usable;
            this._clearButton.style.visibility = usable ? 'visible' : 'hidden';
        }
        const overnight = Boolean(start && end && end < start);
        const endLabel = this.options.endPlaceholder;
        this._nextDayEl.style.display = overnight ? '' : 'none';
        this._endBridge.setLabel(overnight ? Locale.t('timeRangePicker.nextDayLabel', { label: endLabel }) : endLabel);
    }

    _applyValue(range) {
        this._state.send('SET_RANGE', range);
        setTimePickerValue(this.startPicker, range.start);
        setTimePickerValue(this.endPicker, range.end);
        this._applyConstraints();
        this._startBridge.refresh();
        this._endBridge.refresh();
        this._syncControls();
    }

    _applyDisabled() {
        const disabled = this._isDisabled();
        [this.startPicker, this.endPicker].forEach((picker) => {
            if (Boolean(picker.options.disabled) !== disabled) picker.setDisabled(disabled);
        });
        this._startBridge.setDisabled(disabled);
        this._endBridge.setDisabled(disabled);
        toggleAttr(this.element, 'aria-disabled', disabled ? 'true' : null);
        this._syncControls();
    }

    _emitIfChanged(previous) {
        const current = this.getValue();
        if (current.start === previous.start && current.end === previous.end) return;
        if (typeof this.options.onChange === 'function') this.options.onChange(current);
    }

    // 內部 TimePicker 確認了時間：面板已依另一端擋下不合規則的時間，這裡再檢查一次，違反時拒絕並還原
    _handlePick(which) {
        if (this._destroyed) return;
        const picker = which === 'start' ? this.startPicker : this.endPicker;
        const previous = this.getValue();
        const next = { ...previous, [which]: picker.getValue() || null };
        const startMinutes = parseTimeOfDay(next.start);
        const endMinutes = parseTimeOfDay(next.end);
        if (startMinutes !== null && endMinutes !== null && this._problem(startMinutes, endMinutes)) {
            setTimePickerValue(picker, previous[which]);
            this._applyConstraints();
            return;
        }
        this._state.send('SET_RANGE', next);
        this._applyConstraints();
        this._syncControls();
        this._emitIfChanged(previous);
    }

    _validate({ start, end }) {
        const t = (key) => Locale.t(`timeRangePicker.errors.${key}`);
        const required = Boolean(this.options.required);
        if (!start && !end) return required ? t('required') : '';
        if (required && !start) return t('startRequired');
        if (required && !end) return t('endRequired');
        if (!start || !end) return '';
        return this._message(this._problem(parseTimeOfDay(start), parseTimeOfDay(end)));
    }

    /** 目前的值（新物件）：{ start, end }。 */
    getValue() {
        const { start, end } = this._state.snapshot();
        return { start, end };
    }

    /**
     * 設定值；不觸發 onChange。null / undefined / '' 代表清空。
     * 格式不合（非兩位數 'HH:mm'）、allowOvernight 為 false 時結束不晚於開始、或結束等於開始時，
     * console.warn 並保持原值。
     */
    setValue(value) {
        if (this._destroyed) return this;
        const parsed = this._parse(value);
        if (!parsed.ok) {
            console.warn(`[TimeRangePicker] setValue rejected: ${parsed.reason}; value unchanged.`, value);
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

    /** 目前的值是否通過驗證（必填、順序、最短／最長時間長度）。 */
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

export default TimeRangePicker;
