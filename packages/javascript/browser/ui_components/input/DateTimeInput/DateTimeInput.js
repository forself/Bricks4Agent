/**
 * DateTimeInput - 日期時間輸入元件
 * 整合 DatePicker 和 TimePicker 元件
 */

import { DatePicker } from '../../form/DatePicker/DatePicker.js';
import { TimePicker } from '../../form/TimePicker/TimePicker.js';

import Locale from '../../i18n/index.js';
export class DateTimeInput {
    /**
     * @param {Object} options
     * @param {string} options.label - 整體標籤
     * @param {boolean} options.useROC - 使用民國年 (預設 true)
     * @param {boolean} options.showTime - 顯示時間選擇 (預設 true)
     * @param {number} options.minuteStep - 分鐘間隔 (預設 15)
     * @param {string} options.dateValue - 預設日期 (YYYY-MM-DD 或 ROC 格式)
     * @param {string} options.timeValue - 預設時間 (HH:MM)
     * @param {Function} options.onChange - 值變更回調
     */
    constructor(options = {}) {
        this.options = {
            label: '',
            useROC: true,
            showTime: true,
            minuteStep: 15,
            dateValue: '',
            timeValue: '',
            onChange: null,
            ...options
        };

        this.dateValue = this.options.dateValue;
        this.timeValue = this.options.timeValue;

        this.element = this._createElement();
    }

    _createElement() {
        const container = document.createElement('div');
        container.className = 'datetime-input';
        container.style.cssText = `
            display: flex;
            flex-direction: column;
            gap: 8px;
        `;

        // 標籤
        if (this.options.label) {
            const label = document.createElement('label');
            label.textContent = this.options.label;
            label.style.cssText = `
                font-size: var(--cl-font-size-md);
                font-weight: 500;
                color: var(--cl-text);
            `;
            container.appendChild(label);
        }

        // 輸入區
        const inputRow = document.createElement('div');
        inputRow.style.cssText = `
            display: flex;
            gap: 12px;
            align-items: flex-start;
        `;

        // DatePicker 容器
        const dateContainer = document.createElement('div');
        dateContainer.style.cssText = 'flex: 1; min-width: 160px;';
        inputRow.appendChild(dateContainer);

        // 建立 DatePicker (使用 render 方法)
        this.datePicker = new DatePicker({
            label: Locale.t('dateTimeInput.dateLabel'),
            useROC: this.options.useROC,
            value: this.dateValue,
            onChange: (val) => {
                this.dateValue = val;
                this._triggerChange();
            }
        });
        this.datePicker.render(dateContainer);

        // TimePicker
        if (this.options.showTime) {
            const timeContainer = document.createElement('div');
            timeContainer.style.cssText = 'min-width: 140px;';
            
            this.timePicker = new TimePicker({
                label: Locale.t('dateTimeInput.timeLabel'),
                minuteStep: this.options.minuteStep,
                value: this.timeValue,
                onChange: (val) => {
                    this.timeValue = val;
                    this._triggerChange();
                }
            });
            // TimePicker 在建構時就有 element
            timeContainer.appendChild(this.timePicker.element);
            inputRow.appendChild(timeContainer);
        }

        container.appendChild(inputRow);
        return container;
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

    /**
     * 取得值
     */
    getValue() {
        return {
            date: this.dateValue,
            time: this.timeValue
        };
    }

    /**
     * 設定值
     */
    setValue(date, time) {
        if (date !== undefined) {
            this.dateValue = date;
            if (this.datePicker) {
                this.datePicker.setValue?.(date);
            }
        }
        if (time !== undefined && this.timePicker) {
            this.timeValue = time;
            this.timePicker.setValue?.(time);
        }
    }

    /**
     * 掛載
     */
    mount(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    /**
     * 移除
     */
    destroy() {
        if (this.datePicker) this.datePicker.destroy?.();
        if (this.timePicker) this.timePicker.destroy?.();
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default DateTimeInput;
