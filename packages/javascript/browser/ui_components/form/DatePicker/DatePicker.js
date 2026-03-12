import { escapeHtml } from '../../utils/security.js';

import Locale from '../../i18n/index.js';
/**
 * DatePicker Component
 * 日期選擇器元件 - 支援民國年 (ROC) 格式
 */
export class DatePicker {
    constructor(options = {}) {
        this.options = {
            label: '',
            placeholder: Locale.t('datePicker.placeholder'),
            value: null,
            disabled: false,
            required: false,
            size: 'medium',
            useROC: false, // 是否使用民國年
            format: 'western', // 日期格式: 'western' (西元) 或 'taiwan' (民國)
            min: null, // 最小日期
            max: null, // 最大日期
            className: '',
            onChange: null,
            ...options
        };

        // 支援 format 參數，轉換為 useROC
        if (this.options.format === 'taiwan') {
            this.options.useROC = true;
        } else if (this.options.format === 'western') {
            this.options.useROC = false;
        }

        // 標準化 min 和 max 日期（移除時間部分）
        if (this.options.min) {
            const minDate = new Date(this.options.min);
            this.minDate = new Date(minDate.getFullYear(), minDate.getMonth(), minDate.getDate());
        } else {
            this.minDate = null;
        }

        if (this.options.max) {
            const maxDate = new Date(this.options.max);
            this.maxDate = new Date(maxDate.getFullYear(), maxDate.getMonth(), maxDate.getDate());
        } else {
            this.maxDate = null;
        }

        this.selectedDate = null;
        this.currentMonth = new Date().getMonth();
        this.currentYear = new Date().getFullYear();
        this.isOpen = false;
        this.element = null;

        // 解析初始值
        if (this.options.value) {
            this.selectedDate = new Date(this.options.value);
            this.currentMonth = this.selectedDate.getMonth();
            this.currentYear = this.selectedDate.getFullYear();
        }
    }

    _getSizeStyles() {
        const sizes = {
            small: { height: '32px', padding: '0 8px', fontSize: 'var(--cl-font-size-md)' },
            medium: { height: '40px', padding: '0 12px', fontSize: 'var(--cl-font-size-lg)' },
            large: { height: '48px', padding: '0 16px', fontSize: 'var(--cl-font-size-xl)' }
        };
        return sizes[this.options.size] || sizes.medium;
    }

    render(container) {
        const { label, required, disabled, placeholder, className } = this.options;
        const sizeStyles = this._getSizeStyles();

        this.element = document.createElement('div');
        this.element.className = `datepicker ${className || ''}`;
        this.element.style.cssText = `position:relative;display:inline-block;max-width:200px;font-family:var(--cl-font-family);`;

        // 標籤
        if (label) {
            const labelEl = document.createElement('label');
            labelEl.innerHTML = `${escapeHtml(label)}${required ? '<span style="color:var(--cl-danger);margin-left:2px;">*</span>' : ''}`;
            labelEl.style.cssText = `display:block;font-size:var(--cl-font-size-md);font-weight:500;color:var(--cl-text);margin-bottom:4px;`;
            this.element.appendChild(labelEl);
        }

        // 輸入區域
        const inputWrapper = document.createElement('div');
        inputWrapper.className = 'datepicker__input-wrapper';
        inputWrapper.style.cssText = `
            display:flex;align-items:center;position:relative;
            height:${sizeStyles.height};padding:${sizeStyles.padding};padding-right:32px;
            background:${disabled ? 'var(--cl-bg-secondary)' : 'var(--cl-bg)'};
            border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);
            cursor:${disabled ? 'not-allowed' : 'pointer'};transition:all var(--cl-transition);
        `;

        const display = document.createElement('span');
        display.className = 'datepicker__display';
        display.textContent = this.selectedDate ? this._formatDate(this.selectedDate) : placeholder;
        display.style.cssText = `flex:1;font-size:${sizeStyles.fontSize};color:${this.selectedDate ? 'var(--cl-text)' : 'var(--cl-text-placeholder)'};`;

        const icon = document.createElement('span');
        icon.innerHTML = `<svg width="16" height="16" viewBox="0 0 16 16" fill="none">
            <rect x="2" y="3" width="12" height="11" rx="2" stroke="var(--cl-text-secondary)" stroke-width="1.5"/>
            <path d="M2 6H14M5 1V4M11 1V4" stroke="var(--cl-text-secondary)" stroke-width="1.5" stroke-linecap="round"/>
        </svg>`;
        icon.style.cssText = `position:absolute;right:10px;top:50%;transform:translateY(-50%);display:flex;`;

        inputWrapper.appendChild(display);
        inputWrapper.appendChild(icon);

        // 日曆面板
        const calendar = document.createElement('div');
        calendar.className = 'datepicker__calendar';
        calendar.style.cssText = `
            position:absolute;top:100%;left:0;margin-top:4px;
            background: var(--cl-bg);border:1px solid var(--cl-border);border-radius:var(--cl-radius-lg);
            box-shadow:var(--cl-shadow-md);padding:6px;
            z-index:1000;display:none;width:220px;
        `;

        this.element.appendChild(inputWrapper);
        this.element.appendChild(calendar);

        this.inputWrapper = inputWrapper;
        this.display = display;
        this.calendar = calendar;

        this._renderCalendar();
        this._bindEvents();

        // 掛載到容器
        if (container) {
            const target = typeof container === 'string' ? document.querySelector(container) : container;
            if (target) target.appendChild(this.element);
        }

        return this.element;
    }

    _renderCalendar() {
        const months = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '10', '11', '12'];
        const currentYear = new Date().getFullYear();
        const yearStart = this.options.useROC ? 1 : currentYear - 100; // 民國1年 或 西元前100年
        const yearEnd = this.options.useROC ? currentYear - 1911 + 10 : currentYear + 10;

        // 生成年份選項
        let yearOptions = '';
        for (let y = yearEnd; y >= yearStart; y--) {
            const actualYear = this.options.useROC ? y + 1911 : y;
            const displayYear = this.options.useROC ? `${y}` : `${y}`;
            const selected = actualYear === this.currentYear ? 'selected' : '';
            yearOptions += `<option value="${actualYear}" ${selected}>${displayYear}</option>`;
        }

        // 生成月份選項
        let monthOptions = '';
        months.forEach((m, i) => {
            const selected = i === this.currentMonth ? 'selected' : '';
            monthOptions += `<option value="${i}" ${selected}>${m}</option>`;
        });

        this.calendar.innerHTML = `
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:4px;gap:2px;">
                <button type="button" class="dp-prev" style="background:none;border:none;cursor:pointer;padding:1px 4px;font-size:var(--cl-font-size-sm);flex-shrink:0;">◀</button>
                <div style="display:flex;gap:2px;min-width:0;justify-content:center;">
                    <select class="dp-year-select" style="padding:0 12px 0 4px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-sm);font-size:var(--cl-font-size-xs);cursor:pointer;width:52px;height:22px;line-height:20px;appearance:none;-webkit-appearance:none;background:url('data:image/svg+xml,<svg xmlns=%22http://www.w3.org/2000/svg%22 width=%228%22 height=%225%22><path d=%22M0 0l4 5 4-5z%22 fill=%22%23666%22/></svg>') no-repeat right 2px center/8px 5px var(--cl-bg);">
                        ${yearOptions}
                    </select>
                    <select class="dp-month-select" style="padding:0 12px 0 4px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-sm);font-size:var(--cl-font-size-xs);cursor:pointer;width:44px;height:22px;line-height:20px;appearance:none;-webkit-appearance:none;background:url('data:image/svg+xml,<svg xmlns=%22http://www.w3.org/2000/svg%22 width=%228%22 height=%225%22><path d=%22M0 0l4 5 4-5z%22 fill=%22%23666%22/></svg>') no-repeat right 2px center/8px 5px var(--cl-bg);">
                        ${monthOptions}
                    </select>
                </div>
                <button type="button" class="dp-next" style="background:none;border:none;cursor:pointer;padding:1px 4px;font-size:var(--cl-font-size-sm);flex-shrink:0;">▶</button>
            </div>
            <div style="display:grid;grid-template-columns:repeat(7,1fr);gap:0;text-align:center;">
                ${Object.values(Locale.t('datePicker.weekdays')).map(d => `<div style="font-size:var(--cl-font-size-2xs);color:var(--cl-text-muted);padding:2px 0;">${d}</div>`).join('')}
                ${this._renderDays()}
            </div>
        `;

        // 綁定年份選擇
        this.calendar.querySelector('.dp-year-select').onchange = (e) => {
            e.stopPropagation();
            this.currentYear = Number.parseInt(e.target.value, 10);
            this._renderCalendar();
        };

        // 綁定月份選擇
        this.calendar.querySelector('.dp-month-select').onchange = (e) => {
            e.stopPropagation();
            this.currentMonth = Number.parseInt(e.target.value, 10);
            this._renderCalendar();
        };

        // 綁定月份切換箭頭
        this.calendar.querySelector('.dp-prev').onclick = (e) => { e.stopPropagation(); this._prevMonth(); };
        this.calendar.querySelector('.dp-next').onclick = (e) => { e.stopPropagation(); this._nextMonth(); };

        // 綁定日期點擊
        this.calendar.querySelectorAll('.dp-day').forEach(el => {
            el.onclick = (e) => {
                e.stopPropagation();
                // 檢查是否為禁用日期
                if (el.dataset.disabled === 'true') {
                    return;
                }
                const day = Number.parseInt(el.dataset.day, 10);
                this._selectDate(day);
            };
        });
    }

    _renderDays() {
        const firstDay = new Date(this.currentYear, this.currentMonth, 1).getDay();
        const daysInMonth = new Date(this.currentYear, this.currentMonth + 1, 0).getDate();
        const today = new Date();

        let html = '';

        // 空白填充
        for (let i = 0; i < firstDay; i++) {
            html += '<div></div>';
        }

        // 日期
        for (let d = 1; d <= daysInMonth; d++) {
            const currentDate = new Date(this.currentYear, this.currentMonth, d);
            const isToday = today.getDate() === d && today.getMonth() === this.currentMonth && today.getFullYear() === this.currentYear;
            const isSelected = this.selectedDate && this.selectedDate.getDate() === d && this.selectedDate.getMonth() === this.currentMonth && this.selectedDate.getFullYear() === this.currentYear;
            const isDisabled = !this._isDateInRange(currentDate);

            const style = `
                padding:3px 2px;border-radius:var(--cl-radius-sm);cursor:${isDisabled ? 'not-allowed' : 'pointer'};font-size:var(--cl-font-size-sm);
                background:${isSelected ? 'var(--cl-primary)' : isToday ? 'var(--cl-primary-light)' : 'transparent'};
                color:${isDisabled ? 'var(--cl-border-dark)' : isSelected ? 'var(--cl-text-inverse)' : 'var(--cl-text)'};
                ${isToday && !isSelected ? 'font-weight:600;' : ''}
                ${isDisabled ? 'opacity:0.5;' : ''}
            `;
            const disabledAttr = isDisabled ? 'data-disabled="true"' : '';
            html += `<div class="dp-day" data-day="${d}" ${disabledAttr} style="${style}">${d}</div>`;
        }

        return html;
    }

    _isDateInRange(date) {
        // 標準化日期（移除時間部分）
        const checkDate = new Date(date.getFullYear(), date.getMonth(), date.getDate());

        if (this.minDate && checkDate < this.minDate) {
            return false;
        }

        if (this.maxDate && checkDate > this.maxDate) {
            return false;
        }

        return true;
    }

    _bindEvents() {
        if (this.options.disabled) return;

        this.inputWrapper.addEventListener('click', () => this.toggle());

        // 注意：_renderCalendar() 會替換 innerHTML，導致舊 select 元素脫離 DOM
        // 此時 e.target 已不在文件中，不應觸發關閉
        document.addEventListener('click', (e) => {
            if (!document.contains(e.target)) return;
            if (!this.element.contains(e.target)) this.close();
        });

        this.inputWrapper.addEventListener('mouseenter', () => {
            this.inputWrapper.style.borderColor = 'var(--cl-primary)';
        });
        this.inputWrapper.addEventListener('mouseleave', () => {
            if (!this.isOpen) this.inputWrapper.style.borderColor = 'var(--cl-border)';
        });
    }

    _selectDate(day) {
        const date = new Date(this.currentYear, this.currentMonth, day);

        // 檢查日期是否在範圍內
        if (!this._isDateInRange(date)) {
            return;
        }

        this.selectedDate = date;
        this.display.textContent = this._formatDate(this.selectedDate);
        this.display.style.color = 'var(--cl-text)';
        this._renderCalendar();
        this.close();

        if (this.options.onChange) {
            this.options.onChange(this.selectedDate, this._formatDate(this.selectedDate));
        }
    }

    _prevMonth() {
        this.currentMonth--;
        if (this.currentMonth < 0) {
            this.currentMonth = 11;
            this.currentYear--;
        }
        this._renderCalendar();
    }

    _nextMonth() {
        this.currentMonth++;
        if (this.currentMonth > 11) {
            this.currentMonth = 0;
            this.currentYear++;
        }
        this._renderCalendar();
    }

    _formatDate(date) {
        if (!date) return '';
        const y = date.getFullYear();
        const m = String(date.getMonth() + 1).padStart(2, '0');
        const d = String(date.getDate()).padStart(2, '0');

        if (this.options.useROC) {
            return `${y - 1911}/${m}/${d}`;
        }
        return `${y}/${m}/${d}`;
    }

    _getYearDisplay(year) {
        if (this.options.useROC) {
            return Locale.t('datePicker.rocYear', { year: year - 1911 });
        }
        return `${year} 年`;
    }

    open() {
        if (this.options.disabled) return;
        this.isOpen = true;
        this.calendar.style.display = 'block';
        this._renderCalendar(); // 在可見狀態下重新渲染，修正 appearance:none 初始佈局問題
        this.inputWrapper.style.borderColor = 'var(--cl-primary)';
    }

    close() {
        this.isOpen = false;
        this.calendar.style.display = 'none';
        this.inputWrapper.style.borderColor = 'var(--cl-border)';
    }

    toggle() {
        this.isOpen ? this.close() : this.open();
    }

    getValue() {
        return this.selectedDate;
    }

    getFormattedValue() {
        return this._formatDate(this.selectedDate);
    }

    setValue(value) {
        this.selectedDate = new Date(value);
        this.currentMonth = this.selectedDate.getMonth();
        this.currentYear = this.selectedDate.getFullYear();
        this.display.textContent = this._formatDate(this.selectedDate);
        this.display.style.color = 'var(--cl-text)';
        this._renderCalendar();
    }

    clear() {
        this.selectedDate = null;
        this.display.textContent = this.options.placeholder;
        this.display.style.color = 'var(--cl-text-placeholder)';
        this._renderCalendar();
    }

    mount(container) {
        return this.render(container);
    }

    destroy() {
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default DatePicker;
