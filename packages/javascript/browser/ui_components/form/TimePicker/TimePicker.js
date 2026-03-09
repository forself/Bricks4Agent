import { escapeHtml } from '../../utils/security.js';

import Locale from '../../i18n/index.js';
/**
 * TimePicker Component
 * 時間選擇器元件
 */
export class TimePicker {
    constructor(options = {}) {
        this.options = {
            label: '',
            placeholder: Locale.t('timePicker.placeholder'),
            value: null,
            disabled: false,
            required: false,
            size: 'medium',
            minuteStep: 1, // 分鐘間隔 (1, 5, 10, 15, 30)
            className: '',
            onChange: null,
            ...options
        };

        // 兼容 step 參數
        if (options.step) {
            this.options.minuteStep = options.step;
        }

        this.hour = null;
        this.minute = null;
        this.isOpen = false;
        this.element = null;

        // 解析初始值
        if (this.options.value) {
            this._parseValue(this.options.value);
        }

        this.element = this._create();
    }

    _parseValue(value) {
        if (!value) return;
        const match = String(value).match(/^(\d{1,2}):(\d{2})$/);
        if (match) {
            this.hour = Number.parseInt(match[1], 10);
            this.minute = Number.parseInt(match[2], 10);
        }
    }

    _getSizeStyles() {
        const sizes = {
            small: { height: '32px', padding: '0 8px', fontSize: '13px' },
            medium: { height: '40px', padding: '0 12px', fontSize: '14px' },
            large: { height: '48px', padding: '0 16px', fontSize: '16px' }
        };
        return sizes[this.options.size] || sizes.medium;
    }

    _create() {
        const { label, required, disabled, placeholder, className } = this.options;
        const sizeStyles = this._getSizeStyles();

        const container = document.createElement('div');
        container.className = `timepicker ${className || ''}`;
        container.style.cssText = `position:relative;width:100%;font-family:sans-serif;`;

        // 標籤
        if (label) {
            const labelEl = document.createElement('label');
            labelEl.innerHTML = `${escapeHtml(label)}${required ? '<span style="color:var(--cl-danger);margin-left:2px;">*</span>' : ''}`;
            labelEl.style.cssText = `display:block;font-size:13px;font-weight:500;color:var(--cl-text);margin-bottom:4px;`;
            container.appendChild(labelEl);
        }

        // 輸入區域
        const inputWrapper = document.createElement('div');
        inputWrapper.className = 'timepicker__input-wrapper';
        inputWrapper.style.cssText = `
            display:flex;align-items:center;position:relative;
            height:${sizeStyles.height};padding:${sizeStyles.padding};padding-right:32px;
            background:${disabled ? 'var(--cl-bg-secondary)' : 'white'};
            border:1px solid var(--cl-border);border-radius:6px;
            cursor:${disabled ? 'not-allowed' : 'pointer'};transition:all 0.2s;
        `;

        const display = document.createElement('span');
        display.className = 'timepicker__display';
        display.textContent = this._formatTime() || placeholder;
        display.style.cssText = `flex:1;font-size:${sizeStyles.fontSize};color:${this._formatTime() ? 'var(--cl-text)' : 'var(--cl-text-placeholder)'};`;

        const icon = document.createElement('span');
        icon.innerHTML = `<svg width="16" height="16" viewBox="0 0 16 16" fill="none">
            <circle cx="8" cy="8" r="6" stroke="var(--cl-text-secondary)" stroke-width="1.5"/>
            <path d="M8 4V8L11 10" stroke="var(--cl-text-secondary)" stroke-width="1.5" stroke-linecap="round"/>
        </svg>`;
        icon.style.cssText = `position:absolute;right:10px;top:50%;transform:translateY(-50%);display:flex;`;

        inputWrapper.appendChild(display);
        inputWrapper.appendChild(icon);

        // 選擇面板
        const panel = this._createPanel();

        container.appendChild(inputWrapper);
        container.appendChild(panel);

        this.container = container;
        this.inputWrapper = inputWrapper;
        this.display = display;
        this.panel = panel;

        this._bindEvents();

        return container;
    }

    _createPanel() {
        const { minuteStep } = this.options;

        const panel = document.createElement('div');
        panel.className = 'timepicker__panel';
        panel.style.cssText = `
            position:absolute;top:100%;left:0;margin-top:4px;
            background: var(--cl-bg);border:1px solid var(--cl-border);border-radius:8px;
            box-shadow:0 4px 12px rgba(0,0,0,0.15);padding:12px;
            z-index:1000;display:none;width:200px;
        `;

        // 時間選擇區
        const selectorsWrapper = document.createElement('div');
        selectorsWrapper.style.cssText = `display:flex;gap:8px;align-items:center;`;

        // 小時選擇
        const hourColumn = this._createColumn(Locale.t('timePicker.hour'), 0, 23);
        // 分鐘選擇
        const minuteColumn = this._createColumn(Locale.t('timePicker.minute'), 0, 59, minuteStep);

        // 分隔符
        const separator = document.createElement('span');
        separator.textContent = ':';
        separator.style.cssText = `font-size:24px;font-weight:bold;color:var(--cl-text);`;

        selectorsWrapper.appendChild(hourColumn.wrapper);
        selectorsWrapper.appendChild(separator);
        selectorsWrapper.appendChild(minuteColumn.wrapper);

        // 確認按鈕
        const confirmBtn = document.createElement('button');
        confirmBtn.type = 'button';
        confirmBtn.textContent = Locale.t('timePicker.confirm');
        confirmBtn.style.cssText = `
            width:100%;margin-top:12px;padding:8px;
            background:var(--cl-primary);color:white;border:none;border-radius:6px;
            cursor:pointer;font-size:14px;transition:background 0.2s;
        `;
        confirmBtn.addEventListener('mouseenter', () => confirmBtn.style.background = 'var(--cl-primary-dark)');
        confirmBtn.addEventListener('mouseleave', () => confirmBtn.style.background = 'var(--cl-primary)');
        confirmBtn.addEventListener('click', () => this._confirm());

        panel.appendChild(selectorsWrapper);
        panel.appendChild(confirmBtn);

        this.hourColumn = hourColumn;
        this.minuteColumn = minuteColumn;

        return panel;
    }

    _createColumn(label, min, max, step = 1) {
        const wrapper = document.createElement('div');
        wrapper.style.cssText = `flex:1;text-align:center;`;

        const labelEl = document.createElement('div');
        labelEl.textContent = label;
        labelEl.style.cssText = `font-size:11px;color:var(--cl-text-muted);margin-bottom:6px;`;

        const scrollContainer = document.createElement('div');
        scrollContainer.className = 'timepicker__scroll';
        scrollContainer.style.cssText = `height:150px;overflow-y:auto;border:1px solid var(--cl-border-light);border-radius:6px;`;

        for (let i = min; i <= max; i += step) {
            const item = document.createElement('div');
            item.className = 'timepicker__item';
            item.dataset.value = i;
            item.textContent = String(i).padStart(2, '0');
            item.style.cssText = `padding:8px;cursor:pointer;transition:all 0.15s;font-size:14px;color:var(--cl-text);`;

            item.addEventListener('mouseenter', () => {
                if (!item.classList.contains('timepicker__item--selected')) {
                    item.style.background = 'var(--cl-bg-secondary)';
                }
            });
            item.addEventListener('mouseleave', () => {
                if (!item.classList.contains('timepicker__item--selected')) {
                    item.style.background = 'transparent';
                }
            });
            item.addEventListener('click', () => {
                scrollContainer.querySelectorAll('.timepicker__item').forEach(el => {
                    el.classList.remove('timepicker__item--selected');
                    el.style.background = 'transparent';
                    el.style.color = 'var(--cl-text)';
                });
                item.classList.add('timepicker__item--selected');
                item.style.background = 'var(--cl-primary)';
                item.style.color = 'var(--cl-text-inverse)';
            });

            scrollContainer.appendChild(item);
        }

        wrapper.appendChild(labelEl);
        wrapper.appendChild(scrollContainer);

        return {
            wrapper,
            scrollContainer,
            getValue: () => {
                const selected = scrollContainer.querySelector('.timepicker__item--selected');
                return selected ? Number.parseInt(selected.dataset.value, 10) : null;
            },
            setValue: (val) => {
                scrollContainer.querySelectorAll('.timepicker__item').forEach(el => {
                    if (Number.parseInt(el.dataset.value, 10) === val) {
                        el.classList.add('timepicker__item--selected');
                        el.style.background = 'var(--cl-primary)';
                        el.style.color = 'var(--cl-text-inverse)';
                        el.scrollIntoView({ block: 'center' });
                    } else {
                        el.classList.remove('timepicker__item--selected');
                        el.style.background = 'transparent';
                        el.style.color = 'var(--cl-text)';
                    }
                });
            }
        };
    }

    _bindEvents() {
        if (this.options.disabled) return;

        this.inputWrapper.addEventListener('click', () => this.toggle());

        document.addEventListener('click', (e) => {
            if (!this.container.contains(e.target)) {
                this.close();
            }
        });

        this.inputWrapper.addEventListener('mouseenter', () => {
            this.inputWrapper.style.borderColor = 'var(--cl-primary)';
        });
        this.inputWrapper.addEventListener('mouseleave', () => {
            if (!this.isOpen) this.inputWrapper.style.borderColor = 'var(--cl-border)';
        });
    }

    _confirm() {
        const hour = this.hourColumn.getValue();
        const minute = this.minuteColumn.getValue();

        if (hour !== null && minute !== null) {
            this.hour = hour;
            this.minute = minute;
            this.display.textContent = this._formatTime();
            this.display.style.color = 'var(--cl-text)';

            if (this.options.onChange) {
                this.options.onChange(this._formatTime(), { hour, minute });
            }
        }

        this.close();
    }

    _formatTime() {
        if (this.hour === null || this.minute === null) return '';
        return `${String(this.hour).padStart(2, '0')}:${String(this.minute).padStart(2, '0')}`;
    }

    open() {
        if (this.options.disabled) return;
        this.isOpen = true;
        this.panel.style.display = 'block';
        this.inputWrapper.style.borderColor = 'var(--cl-primary)';

        // 設定初始選中
        if (this.hour !== null) this.hourColumn.setValue(this.hour);
        if (this.minute !== null) this.minuteColumn.setValue(this.minute);
    }

    close() {
        this.isOpen = false;
        this.panel.style.display = 'none';
        this.inputWrapper.style.borderColor = 'var(--cl-border)';
    }

    toggle() {
        this.isOpen ? this.close() : this.open();
    }

    getValue() {
        return this._formatTime();
    }

    setValue(value) {
        this._parseValue(value);
        this.display.textContent = this._formatTime() || this.options.placeholder;
        this.display.style.color = this._formatTime() ? 'var(--cl-text)' : 'var(--cl-text-placeholder)';
    }

    clear() {
        this.hour = null;
        this.minute = null;
        this.display.textContent = this.options.placeholder;
        this.display.style.color = 'var(--cl-text-placeholder)';
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default TimePicker;
