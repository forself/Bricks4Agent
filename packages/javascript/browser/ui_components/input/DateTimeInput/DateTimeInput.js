import { DatePicker } from '../../form/DatePicker/index.js';
import { TimePicker } from '../../form/TimePicker/index.js';
import Locale from '../../i18n/index.js';
import { createComponentState } from '../../utils/component-state.js';

function toIsoDate(value) {
    if (!value) return '';
    if (value instanceof Date && !Number.isNaN(value.getTime())) {
        const year = value.getFullYear();
        const month = String(value.getMonth() + 1).padStart(2, '0');
        const day = String(value.getDate()).padStart(2, '0');
        return `${year}-${month}-${day}`;
    }
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return String(value);
    return toIsoDate(date);
}

export class DateTimeInput {
    constructor(options = {}) {
        this.options = {
            label: '',
            useROC: true,
            showTime: true,
            minuteStep: 15,
            dateValue: '',
            timeValue: '',
            disabled: false,
            onChange: null,
            ...options
        };

        this.dateValue = toIsoDate(this.options.dateValue);
        this.timeValue = this.options.timeValue || '';

        this.element = null;
        this.datePicker = null;
        this.timePicker = null;
        this.inputRow = null;

        this.element = this._createElement();
        this._state = createComponentState({
            lifecycle: 'created',
            visibility: 'visible',
            availability: this.options.disabled ? 'disabled' : 'enabled',
            dateValue: this.dateValue,
            timeValue: this.timeValue,
            showTime: !!this.options.showTime
        }, {
            MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
            DESTROY: (state) => ({ ...state, lifecycle: 'destroyed' }),
            SHOW: (state) => ({ ...state, visibility: 'visible' }),
            HIDE: (state) => ({ ...state, visibility: 'hidden' }),
            SET_DATE: (state, payload) => ({ ...state, dateValue: toIsoDate(payload?.value) }),
            SET_TIME: (state, payload) => ({ ...state, timeValue: String(payload?.value ?? '') }),
            SET_DISABLED: (state, payload) => ({ ...state, availability: payload?.disabled ? 'disabled' : 'enabled' }),
            CLEAR: (state) => ({ ...state, dateValue: '', timeValue: '' })
        });

        this._applyState();
    }

    _createElement() {
        const container = document.createElement('div');
        container.className = 'datetime-input';
        container.style.cssText = 'display:flex;flex-direction:column;gap:8px;';

        if (this.options.label) {
            const label = document.createElement('label');
            label.textContent = this.options.label;
            label.style.cssText = 'font-size:var(--cl-font-size-md);font-weight:500;color:var(--cl-text);';
            container.appendChild(label);
        }

        const inputRow = document.createElement('div');
        inputRow.className = 'datetime-input__row';
        inputRow.style.cssText = 'display:flex;gap:12px;align-items:flex-start;';

        const dateContainer = document.createElement('div');
        dateContainer.style.cssText = 'flex:1;min-width:160px;';
        inputRow.appendChild(dateContainer);

        this.datePicker = new DatePicker({
            label: Locale.t('dateTimeInput.dateLabel'),
            useROC: this.options.useROC,
            value: this.dateValue,
            disabled: this.options.disabled,
            onChange: (value) => {
                this.send('SET_DATE', { value });
                this._triggerChange();
            }
        });
        this.datePicker.mount(dateContainer);

        if (this.options.showTime) {
            const timeContainer = document.createElement('div');
            timeContainer.style.cssText = 'min-width:140px;';
            inputRow.appendChild(timeContainer);

            this.timePicker = new TimePicker({
                label: Locale.t('dateTimeInput.timeLabel'),
                minuteStep: this.options.minuteStep,
                value: this.timeValue,
                disabled: this.options.disabled,
                onChange: (value) => {
                    this.send('SET_TIME', { value });
                    this._triggerChange();
                }
            });
            this.timePicker.mount(timeContainer);
        }

        container.appendChild(inputRow);
        this.inputRow = inputRow;
        return container;
    }

    _syncLegacyFields(state) {
        this.dateValue = state.dateValue;
        this.timeValue = state.timeValue;
        this.options.disabled = state.availability === 'disabled';
    }

    _applyState() {
        const state = this.snapshot();
        this._syncLegacyFields(state);
        if (this.element) {
            this.element.style.display = state.visibility === 'hidden' ? 'none' : 'flex';
        }
        this.datePicker?.setDisabled?.(state.availability === 'disabled');
        this.timePicker?.setDisabled?.(state.availability === 'disabled');
    }

    _triggerChange() {
        if (this.options.onChange) {
            this.options.onChange({
                date: this.dateValue,
                time: this.timeValue,
                combined: `${this.dateValue} ${this.timeValue}`.trim()
            });
        }
    }

    snapshot() {
        return this._state.snapshot();
    }

    send(event, payload = null) {
        this._state.send(event, payload);
        this._applyState();
        return this.snapshot();
    }

    getValue() {
        return {
            date: this.dateValue,
            time: this.timeValue
        };
    }

    setValue(date, time) {
        if (date !== undefined) {
            const normalizedDate = toIsoDate(date);
            this.send('SET_DATE', { value: normalizedDate });
            this.datePicker?.setValue?.(normalizedDate);
        }
        if (time !== undefined && this.timePicker) {
            this.send('SET_TIME', { value: time });
            this.timePicker.setValue?.(time);
        }
    }

    setDisabled(disabled) {
        this.send('SET_DISABLED', { disabled });
    }

    clear() {
        this.send('CLEAR');
        this.datePicker?.clear?.();
        this.timePicker?.clear?.();
    }

    show() {
        this.send('SHOW');
    }

    hide() {
        this.send('HIDE');
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) {
            target.appendChild(this.element);
            this.send('MOUNT');
        }
        return this;
    }

    destroy() {
        this.send('DESTROY');
        this.datePicker?.destroy?.();
        this.timePicker?.destroy?.();
        if (this.element?.parentNode) this.element.remove();
    }
}

export default DateTimeInput;
