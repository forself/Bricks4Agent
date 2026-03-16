import { escapeHtml } from '../../utils/security.js';
import Locale from '../../i18n/index.js';
import { createComponentState } from '../../utils/component-state.js';

function normalizeDate(value) {
    if (!value) return null;

    const date = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(date.getTime())) return null;

    return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

function toDateValue(date) {
    if (!date) return null;

    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
}

function fromDateValue(value) {
    return normalizeDate(value);
}

export class DatePicker {
    constructor(options = {}) {
        this.options = {
            label: '',
            placeholder: Locale.t('datePicker.placeholder'),
            value: null,
            disabled: false,
            required: false,
            size: 'medium',
            useROC: false,
            format: 'western',
            min: null,
            max: null,
            className: '',
            onChange: null,
            ...options
        };

        if (this.options.format === 'taiwan') {
            this.options.useROC = true;
        } else if (this.options.format === 'western') {
            this.options.useROC = false;
        }

        this.minDate = normalizeDate(this.options.min);
        this.maxDate = normalizeDate(this.options.max);

        const selectedDate = normalizeDate(this.options.value);
        const baseDate = selectedDate || new Date();

        this.selectedDate = selectedDate;
        this.currentMonth = baseDate.getMonth();
        this.currentYear = baseDate.getFullYear();
        this.isOpen = false;

        this.element = null;
        this.inputWrapper = null;
        this.display = null;
        this.calendar = null;
        this.yearSelect = null;
        this.monthSelect = null;
        this.daysGrid = null;
        this.prevButton = null;
        this.nextButton = null;

        this.element = this._createElement();
        this._state = createComponentState(this._buildInitialState(selectedDate, baseDate), {
            MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
            DESTROY: (state) => ({ ...state, lifecycle: 'destroyed', open: false }),
            SHOW: (state) => ({ ...state, visibility: 'visible' }),
            HIDE: (state) => ({ ...state, visibility: 'hidden', open: false }),
            OPEN: (state) => {
                if (state.availability === 'disabled' || state.open) return state;
                return { ...state, open: true };
            },
            CLOSE: (state) => ({ ...state, open: false }),
            TOGGLE: (state) => {
                if (state.availability === 'disabled') return state;
                return { ...state, open: !state.open };
            },
            SET_DISABLED: (state, payload) => ({
                ...state,
                availability: payload?.disabled ? 'disabled' : 'enabled',
                open: payload?.disabled ? false : state.open
            }),
            SET_VALUE: (state, payload) => {
                if (payload?.value == null || payload?.value === '') {
                    return {
                        ...state,
                        selectedValue: null
                    };
                }

                const date = normalizeDate(payload?.value);
                if (!date || !this._isDateInRange(date)) return state;

                return {
                    ...state,
                    selectedValue: toDateValue(date),
                    currentMonth: date.getMonth(),
                    currentYear: date.getFullYear()
                };
            },
            CLEAR: (state) => ({
                ...state,
                selectedValue: null
            }),
            PREV_MONTH: (state) => {
                let month = state.currentMonth - 1;
                let year = state.currentYear;
                if (month < 0) {
                    month = 11;
                    year -= 1;
                }
                return {
                    ...state,
                    currentMonth: month,
                    currentYear: year
                };
            },
            NEXT_MONTH: (state) => {
                let month = state.currentMonth + 1;
                let year = state.currentYear;
                if (month > 11) {
                    month = 0;
                    year += 1;
                }
                return {
                    ...state,
                    currentMonth: month,
                    currentYear: year
                };
            },
            SET_YEAR: (state, payload) => {
                const year = Number.parseInt(payload?.year, 10);
                if (Number.isNaN(year)) return state;
                return {
                    ...state,
                    currentYear: year
                };
            },
            SET_MONTH: (state, payload) => {
                const month = Number.parseInt(payload?.month, 10);
                if (Number.isNaN(month) || month < 0 || month > 11) return state;
                return {
                    ...state,
                    currentMonth: month
                };
            },
            SELECT_DAY: (state, payload) => {
                const day = Number.parseInt(payload?.day, 10);
                if (Number.isNaN(day) || day < 1) return state;

                const date = new Date(state.currentYear, state.currentMonth, day);
                if (date.getMonth() !== state.currentMonth || !this._isDateInRange(date)) {
                    return state;
                }

                return {
                    ...state,
                    selectedValue: toDateValue(date),
                    open: false
                };
            }
        });

        this._bindEvents();
        this._applyState();
    }

    _buildInitialState(selectedDate, baseDate) {
        return {
            lifecycle: 'created',
            visibility: 'visible',
            availability: this.options.disabled ? 'disabled' : 'enabled',
            open: false,
            selectedValue: selectedDate ? toDateValue(selectedDate) : null,
            currentMonth: baseDate.getMonth(),
            currentYear: baseDate.getFullYear()
        };
    }

    _getSizeStyles() {
        const sizes = {
            small: { height: '32px', padding: '0 8px', fontSize: 'var(--cl-font-size-md)' },
            medium: { height: '40px', padding: '0 12px', fontSize: 'var(--cl-font-size-lg)' },
            large: { height: '48px', padding: '0 16px', fontSize: 'var(--cl-font-size-xl)' }
        };
        return sizes[this.options.size] || sizes.medium;
    }

    _createElement() {
        const { label, required, className } = this.options;
        const sizeStyles = this._getSizeStyles();

        const container = document.createElement('div');
        container.className = `datepicker ${className || ''}`.trim();
        container.style.cssText = `
            position: relative;
            display: inline-block;
            max-width: 220px;
            font-family: var(--cl-font-family);
        `;

        if (label) {
            const labelEl = document.createElement('label');
            labelEl.innerHTML = `${escapeHtml(label)}${required ? '<span style="color:var(--cl-danger);margin-left:2px;">*</span>' : ''}`;
            labelEl.style.cssText = `
                display: block;
                font-size: var(--cl-font-size-md);
                font-weight: 500;
                color: var(--cl-text);
                margin-bottom: 4px;
            `;
            container.appendChild(labelEl);
        }

        const inputWrapper = document.createElement('div');
        inputWrapper.className = 'datepicker__input-wrapper';
        inputWrapper.style.cssText = `
            display: flex;
            align-items: center;
            position: relative;
            height: ${sizeStyles.height};
            padding: ${sizeStyles.padding};
            padding-right: 32px;
            background: var(--cl-bg);
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-md);
            cursor: pointer;
            transition: all var(--cl-transition);
        `;

        const display = document.createElement('span');
        display.className = 'datepicker__display';
        display.style.cssText = `
            flex: 1;
            font-size: ${sizeStyles.fontSize};
            color: var(--cl-text-placeholder);
        `;

        const icon = document.createElement('span');
        icon.className = 'datepicker__icon';
        icon.innerHTML = `<svg width="16" height="16" viewBox="0 0 16 16" fill="none">
            <rect x="2" y="3" width="12" height="11" rx="2" stroke="var(--cl-text-secondary)" stroke-width="1.5"/>
            <path d="M2 6H14M5 1V4M11 1V4" stroke="var(--cl-text-secondary)" stroke-width="1.5" stroke-linecap="round"/>
        </svg>`;
        icon.style.cssText = `
            position: absolute;
            right: 10px;
            top: 50%;
            transform: translateY(-50%);
            display: flex;
        `;

        inputWrapper.appendChild(display);
        inputWrapper.appendChild(icon);

        const calendar = document.createElement('div');
        calendar.className = 'datepicker__calendar';
        calendar.style.cssText = `
            position: absolute;
            top: 100%;
            left: 0;
            margin-top: 4px;
            background: var(--cl-bg);
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-lg);
            box-shadow: var(--cl-shadow-md);
            padding: 6px;
            z-index: 1000;
            display: none;
            width: 220px;
        `;

        const header = document.createElement('div');
        header.className = 'datepicker__header';
        header.style.cssText = `
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 4px;
            gap: 2px;
        `;

        const prevButton = document.createElement('button');
        prevButton.type = 'button';
        prevButton.className = 'dp-prev';
        prevButton.textContent = '<';
        prevButton.style.cssText = `
            background: none;
            border: none;
            cursor: pointer;
            padding: 1px 4px;
            font-size: var(--cl-font-size-sm);
            flex-shrink: 0;
        `;

        const selectorGroup = document.createElement('div');
        selectorGroup.className = 'datepicker__selectors';
        selectorGroup.style.cssText = `
            display: flex;
            gap: 2px;
            min-width: 0;
            justify-content: center;
        `;

        const yearSelect = document.createElement('select');
        yearSelect.className = 'dp-year-select';
        yearSelect.style.cssText = `
            padding: 0 12px 0 4px;
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-sm);
            font-size: var(--cl-font-size-xs);
            cursor: pointer;
            width: 56px;
            height: 22px;
            line-height: 20px;
            background: var(--cl-bg);
        `;

        const monthSelect = document.createElement('select');
        monthSelect.className = 'dp-month-select';
        monthSelect.style.cssText = `
            padding: 0 12px 0 4px;
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-sm);
            font-size: var(--cl-font-size-xs);
            cursor: pointer;
            width: 48px;
            height: 22px;
            line-height: 20px;
            background: var(--cl-bg);
        `;

        selectorGroup.appendChild(yearSelect);
        selectorGroup.appendChild(monthSelect);

        const nextButton = document.createElement('button');
        nextButton.type = 'button';
        nextButton.className = 'dp-next';
        nextButton.textContent = '>';
        nextButton.style.cssText = prevButton.style.cssText;

        header.appendChild(prevButton);
        header.appendChild(selectorGroup);
        header.appendChild(nextButton);

        const daysGrid = document.createElement('div');
        daysGrid.className = 'datepicker__days';
        daysGrid.style.cssText = `
            display: grid;
            grid-template-columns: repeat(7, 1fr);
            gap: 0;
            text-align: center;
        `;

        calendar.appendChild(header);
        calendar.appendChild(daysGrid);

        container.appendChild(inputWrapper);
        container.appendChild(calendar);

        this.inputWrapper = inputWrapper;
        this.display = display;
        this.calendar = calendar;
        this.yearSelect = yearSelect;
        this.monthSelect = monthSelect;
        this.daysGrid = daysGrid;
        this.prevButton = prevButton;
        this.nextButton = nextButton;

        return container;
    }

    _getYearBounds() {
        const currentYear = new Date().getFullYear();
        return this.options.useROC
            ? { start: 1912, end: currentYear + 10 }
            : { start: currentYear - 100, end: currentYear + 10 };
    }

    _renderCalendar() {
        const state = this.snapshot();
        const { start, end } = this._getYearBounds();

        this.yearSelect.innerHTML = '';
        for (let year = end; year >= start; year -= 1) {
            const option = document.createElement('option');
            option.value = String(year);
            option.textContent = this.options.useROC ? String(year - 1911) : String(year);
            this.yearSelect.appendChild(option);
        }
        this.yearSelect.value = String(state.currentYear);

        this.monthSelect.innerHTML = '';
        for (let month = 0; month < 12; month += 1) {
            const option = document.createElement('option');
            option.value = String(month);
            option.textContent = String(month + 1);
            this.monthSelect.appendChild(option);
        }
        this.monthSelect.value = String(state.currentMonth);

        this.daysGrid.innerHTML = '';
        const weekdays = Object.values(Locale.t('datePicker.weekdays'));
        for (const weekday of weekdays) {
            const headerCell = document.createElement('div');
            headerCell.className = 'datepicker__weekday';
            headerCell.textContent = weekday;
            headerCell.style.cssText = `
                font-size: var(--cl-font-size-2xs);
                color: var(--cl-text-muted);
                padding: 2px 0;
            `;
            this.daysGrid.appendChild(headerCell);
        }

        const firstDay = new Date(state.currentYear, state.currentMonth, 1).getDay();
        const daysInMonth = new Date(state.currentYear, state.currentMonth + 1, 0).getDate();
        const todayValue = toDateValue(new Date());

        for (let index = 0; index < firstDay; index += 1) {
            const blank = document.createElement('div');
            blank.className = 'datepicker__blank';
            this.daysGrid.appendChild(blank);
        }

        for (let day = 1; day <= daysInMonth; day += 1) {
            const date = new Date(state.currentYear, state.currentMonth, day);
            const dateValue = toDateValue(date);
            const isSelected = state.selectedValue === dateValue;
            const isToday = todayValue === dateValue;
            const isDisabled = !this._isDateInRange(date);

            const dayEl = document.createElement('button');
            dayEl.type = 'button';
            dayEl.className = 'dp-day';
            dayEl.dataset.day = String(day);
            dayEl.dataset.disabled = isDisabled ? 'true' : 'false';
            dayEl.textContent = String(day);
            dayEl.style.cssText = `
                padding: 3px 2px;
                border: none;
                border-radius: var(--cl-radius-sm);
                cursor: ${isDisabled ? 'not-allowed' : 'pointer'};
                font-size: var(--cl-font-size-sm);
                background: ${isSelected ? 'var(--cl-primary)' : isToday ? 'var(--cl-primary-light)' : 'transparent'};
                color: ${isDisabled ? 'var(--cl-border-dark)' : isSelected ? 'var(--cl-text-inverse)' : 'var(--cl-text)'};
                ${isToday && !isSelected ? 'font-weight:600;' : ''}
                ${isDisabled ? 'opacity:0.5;' : ''}
            `;

            dayEl.addEventListener('click', (event) => {
                event.stopPropagation?.();
                if (isDisabled) return;
                this._selectDate(day);
            });

            this.daysGrid.appendChild(dayEl);
        }
    }

    _bindEvents() {
        this.inputWrapper.addEventListener('click', () => this.toggle());
        this.inputWrapper.addEventListener('mouseenter', () => {
            if (this.snapshot().availability === 'disabled') return;
            this.inputWrapper.style.borderColor = 'var(--cl-primary)';
        });
        this.inputWrapper.addEventListener('mouseleave', () => {
            if (!this.snapshot().open) {
                this.inputWrapper.style.borderColor = 'var(--cl-border)';
            }
        });

        this.prevButton.addEventListener('click', (event) => {
            event.stopPropagation?.();
            this._prevMonth();
        });

        this.nextButton.addEventListener('click', (event) => {
            event.stopPropagation?.();
            this._nextMonth();
        });

        this.yearSelect.addEventListener('change', (event) => {
            event.stopPropagation?.();
            this.send('SET_YEAR', { year: event.target.value });
        });

        this.monthSelect.addEventListener('change', (event) => {
            event.stopPropagation?.();
            this.send('SET_MONTH', { month: event.target.value });
        });

        this._onDocumentClick = (event) => {
            if (!this.element.contains(event.target)) {
                this.close();
            }
        };
        document.addEventListener('click', this._onDocumentClick);
    }

    _syncLegacyFields(state) {
        this.selectedDate = fromDateValue(state.selectedValue);
        this.currentMonth = state.currentMonth;
        this.currentYear = state.currentYear;
        this.isOpen = state.open;
        this.options.disabled = state.availability === 'disabled';
    }

    _applyState() {
        const state = this.snapshot();
        this._syncLegacyFields(state);

        if (this.element) {
            this.element.style.display = state.visibility === 'hidden' ? 'none' : 'inline-block';
        }

        if (this.inputWrapper) {
            this.inputWrapper.style.background = state.availability === 'disabled' ? 'var(--cl-bg-secondary)' : 'var(--cl-bg)';
            this.inputWrapper.style.cursor = state.availability === 'disabled' ? 'not-allowed' : 'pointer';
            this.inputWrapper.style.opacity = state.availability === 'disabled' ? '0.6' : '1';
            this.inputWrapper.style.borderColor = state.open ? 'var(--cl-primary)' : 'var(--cl-border)';
        }

        if (this.display) {
            const selectedDate = fromDateValue(state.selectedValue);
            this.display.textContent = selectedDate ? this._formatDate(selectedDate) : this.options.placeholder;
            this.display.style.color = selectedDate ? 'var(--cl-text)' : 'var(--cl-text-placeholder)';
        }

        if (this.calendar) {
            this.calendar.style.display = state.open ? 'block' : 'none';
        }

        this._renderCalendar();
    }

    _isDateInRange(date) {
        const checkDate = normalizeDate(date);
        if (!checkDate) return false;

        if (this.minDate && checkDate < this.minDate) {
            return false;
        }

        if (this.maxDate && checkDate > this.maxDate) {
            return false;
        }

        return true;
    }

    _formatDate(date) {
        if (!date) return '';

        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');

        if (this.options.useROC) {
            return `${year - 1911}/${month}/${day}`;
        }

        return `${year}/${month}/${day}`;
    }

    _getYearDisplay(year) {
        if (this.options.useROC) {
            return Locale.t('datePicker.rocYear', { year: year - 1911 });
        }
        return String(year);
    }

    snapshot() {
        return this._state.snapshot();
    }

    send(event, payload = null) {
        this._state.send(event, payload);
        this._applyState();
        return this.snapshot();
    }

    render(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) {
            target.appendChild(this.element);
            this.send('MOUNT');
        }
        return this.element;
    }

    _selectDate(day) {
        const previousValue = this.snapshot().selectedValue;
        const nextState = this.send('SELECT_DAY', { day });

        if (nextState.selectedValue !== previousValue && nextState.selectedValue && this.options.onChange) {
            const selectedDate = fromDateValue(nextState.selectedValue);
            this.options.onChange(selectedDate, this._formatDate(selectedDate));
        }
    }

    _prevMonth() {
        this.send('PREV_MONTH');
    }

    _nextMonth() {
        this.send('NEXT_MONTH');
    }

    open() {
        if (this.options.disabled || this.isOpen) return;
        this.send('OPEN');
    }

    close() {
        if (!this.isOpen) return;
        this.send('CLOSE');
    }

    toggle() {
        this.send('TOGGLE');
    }

    getValue() {
        return this.selectedDate;
    }

    getFormattedValue() {
        return this._formatDate(this.selectedDate);
    }

    setValue(value) {
        this.send('SET_VALUE', { value });
    }

    clear() {
        this.send('CLEAR');
    }

    setDisabled(disabled) {
        this.send('SET_DISABLED', { disabled });
    }

    show() {
        this.send('SHOW');
    }

    hide() {
        this.send('HIDE');
    }

    mount(container) {
        return this.render(container);
    }

    destroy() {
        this.send('DESTROY');
        if (this._onDocumentClick) {
            document.removeEventListener('click', this._onDocumentClick);
        }
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default DatePicker;
