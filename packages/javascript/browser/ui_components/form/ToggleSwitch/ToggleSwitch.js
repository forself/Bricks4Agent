/**
 * ToggleSwitch Component
 * 開關滑桿元件 - 布林值輸入，滑桿式外觀
 * API 對齊 Checkbox，可互換使用
 */

import { setFieldError, clearFieldError, FIELD_ERROR_CONTRACT } from '../../utils/field-error.js';

export class ToggleSwitch {
    /**
     * @param {Object} options
     * @param {string} options.label - 標籤文字
     * @param {boolean} options.checked - 是否開啟
     * @param {boolean} options.disabled - 停用
     * @param {string} options.size - 尺寸 (small, medium, large)
     * @param {Function} options.onChange - (checked: boolean) => void
     */
    constructor(options = {}) {
        this.options = {
            label: '',
            checked: false,
            disabled: false,
            size: 'medium',
            onChange: null,
            ...options
        };

        this.checked = this.options.checked;
        this.element = this._createElement();
    }

    _getSizeConfig() {
        const sizes = {
            small:  { trackW: 32, trackH: 18, thumb: 14, font: '12px', gap: '6px' },
            medium: { trackW: 40, trackH: 22, thumb: 18, font: '14px', gap: '8px' },
            large:  { trackW: 48, trackH: 26, thumb: 22, font: '16px', gap: '10px' }
        };
        return sizes[this.options.size] || sizes.medium;
    }

    _createElement() {
        const { label, disabled } = this.options;
        const s = this._getSizeConfig();

        const container = document.createElement('label');
        container.className = 'toggle-switch';
        container.style.cssText = `
            display:inline-flex;align-items:center;gap:${s.gap};
            cursor:${disabled ? 'not-allowed' : 'pointer'};
            user-select:none;opacity:${disabled ? '0.5' : '1'};
        `;

        // 滑軌
        const track = document.createElement('span');
        track.className = 'toggle-switch__track';
        track.style.cssText = `
            position:relative;display:inline-block;
            width:${s.trackW}px;height:${s.trackH}px;
            border-radius:${s.trackH}px;
            background:${this.checked ? 'var(--cl-primary)' : 'var(--cl-border-dark)'};
            transition:background var(--cl-transition);flex-shrink:0;
        `;

        // 滑塊
        const thumb = document.createElement('span');
        thumb.className = 'toggle-switch__thumb';
        const offset = this.checked ? (s.trackW - s.thumb - 2) : 2;
        thumb.style.cssText = `
            position:absolute;top:${(s.trackH - s.thumb) / 2}px;left:${offset}px;
            width:${s.thumb}px;height:${s.thumb}px;
            border-radius:var(--cl-radius-round);background: var(--cl-bg);
            box-shadow:var(--cl-shadow-sm);
            transition:left var(--cl-transition);
        `;
        track.appendChild(thumb);

        container.appendChild(track);
        this._track = track;
        this._thumb = thumb;

        // 標籤
        if (label) {
            const labelEl = document.createElement('span');
            labelEl.className = 'toggle-switch__label';
            labelEl.textContent = label;
            labelEl.style.cssText = `font-size:${s.font};color:var(--cl-text);`;
            container.appendChild(labelEl);
        }

        // 點擊事件
        if (!disabled) {
            container.addEventListener('click', (e) => {
                e.preventDefault();
                this.toggle();
            });
        }

        return container;
    }

    _updateVisual() {
        const s = this._getSizeConfig();
        this._track.style.background = this.checked ? 'var(--cl-primary)' : 'var(--cl-border-dark)';
        this._thumb.style.left = `${this.checked ? (s.trackW - s.thumb - 2) : 2}px`;
    }

    // ─── 公開 API ───

    toggle() {
        this.checked = !this.checked;
        this._updateVisual();
        if (this.options.onChange) {
            this.options.onChange(this.checked);
        }
    }

    isChecked() {
        return this.checked;
    }

    setChecked(checked) {
        this.checked = !!checked;
        this._updateVisual();
    }

    getValue() {
        return this.checked;
    }

    setValue(value) {
        this.setChecked(value);
    }

    clear() {
        this.setChecked(false);
    }

    /**
     * 標示欄位錯誤；空訊息等同 clearError()。錯誤文字顯示在元件正下方（元件外），元件需已掛載。
     * display:false 只標示錯誤狀態、不顯示文字，給自行顯示錯誤文字的外層（FormField、SearchForm）使用。
     */
    setError(message, { display = true } = {}) {
        setFieldError(this, message, { target: this.element, visual: this._track, after: this.element, display });
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

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        clearFieldError(this);
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default ToggleSwitch;
