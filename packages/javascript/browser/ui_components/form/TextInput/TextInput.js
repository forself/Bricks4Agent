import { escapeHtml, hasSqlInjectionRisk, hasPathTraversalRisk } from '../../utils/security.js';

export class TextInput {
    /**
     * @param {Object} options
     * @param {string} options.type - 輸入類型 'text', 'password', 'email', 'tel'
     * @param {string} options.placeholder - 預設提示
     * @param {string} options.value - 初始值
     * @param {string} options.label - 標籤文字
     * @param {string} options.size - 尺寸 'small', 'medium', 'large'
     * @param {boolean} options.disabled - 停用
     * @param {boolean} options.readonly - 唯讀
     * @param {boolean} options.required - 必填
     * @param {string} options.error - 錯誤訊息
     * @param {string} options.hint - 提示訊息
     * @param {number} options.maxLength - 最大長度
     * @param {string} options.width - 寬度
     * @param {boolean} options.enableSecurity - 啟用資安檢查 (SQL/Path)
     * @param {Function} options.onChange - 變更回調
     * @param {Function} options.onBlur - 失焦回調
     */
    constructor(options = {}) {
        this.options = {
            type: 'text',
            placeholder: '',
            value: '',
            label: '',
            size: 'medium',
            disabled: false,
            readonly: false,
            required: false,
            error: '',
            hint: '',
            maxLength: null,
            width: '100%',
            enableSecurity: true,
            onChange: null,
            onBlur: null,
            ...options
        };

        this.element = this._create();
        this.input = null;
        this.message = null;
    }

    _create() {
        const { label, type, placeholder, value, size, disabled, readonly, required, error, hint, maxLength, width } = this.options;

        const sizeStyles = {
            small: { height: '32px', padding: '0 8px', fontSize: 'var(--cl-font-size-md)' },
            medium: { height: '40px', padding: '0 12px', fontSize: 'var(--cl-font-size-lg)' },
            large: { height: '48px', padding: '0 16px', fontSize: 'var(--cl-font-size-xl)' }
        }[size] || { height: '40px', padding: '0 12px', fontSize: 'var(--cl-font-size-lg)' };

        const container = document.createElement('div');
        container.className = 'text-input-container';
        container.style.cssText = `
            display: flex;
            flex-direction: column;
            gap: 4px;
            width: ${width};
        `;

        // 標籤
        if (label) {
            const labelEl = document.createElement('label');
            labelEl.className = 'text-input__label';
            labelEl.innerHTML = `${escapeHtml(label)}${required ? '<span style="color: var(--cl-danger); margin-left: 2px;">*</span>' : ''}`;
            labelEl.style.cssText = `font-size: var(--cl-font-size-md); font-weight: 500; color: var(--cl-text);`;
            container.appendChild(labelEl);
        }

        // 輸入框
        const input = document.createElement('input');
        input.className = 'text-input';
        input.type = type;
        input.placeholder = placeholder;
        input.value = value;
        input.disabled = disabled;
        input.readOnly = readonly;
        if (maxLength) input.maxLength = maxLength;

        const borderColor = error ? 'var(--cl-danger)' : 'var(--cl-border)';
        input.style.cssText = `
            width: 100%;
            height: ${sizeStyles.height};
            padding: ${sizeStyles.padding};
            font-size: ${sizeStyles.fontSize};
            font-family: inherit;
            border: 1px solid ${borderColor};
            border-radius: var(--cl-radius-md);
            outline: none;
            transition: all var(--cl-transition);
            background: ${disabled ? 'var(--cl-bg-secondary)' : 'var(--cl-bg)'};
            color: ${disabled ? 'var(--cl-text-placeholder)' : 'var(--cl-text)'};
        `;

        // Focus 效果
        input.addEventListener('focus', () => {
            if (this.options.error) return; // Error state overrides focus color
            input.style.borderColor = 'var(--cl-primary)';
            input.style.boxShadow = `0 0 0 3px rgba(var(--cl-primary-rgb), 0.1)`;
        });

        input.addEventListener('blur', () => {
            if (this.options.error) return;
            input.style.borderColor = 'var(--cl-border)';
            input.style.boxShadow = 'none';

            // Security Check on Blur
            if (this.options.enableSecurity) {
                this._validateSecurity(input.value);
            }

            if (this.options.onBlur) {
                this.options.onBlur(input.value);
            }
        });

        input.addEventListener('input', () => {
            this.options.error = ''; // Clear error on input
            input.style.borderColor = 'var(--cl-primary)'; // Restore focus color

            // Optional: aggressive security check on input
            // if (this.options.enableSecurity) {
            //     this._validateSecurity(input.value);
            // }

            if (this.options.onChange) {
                this.options.onChange(input.value);
            }
        });

        container.appendChild(input);
        this.input = input;

        // 提示/錯誤訊息
        if (error || hint) {
            const message = document.createElement('span');
            message.className = error ? 'text-input__error' : 'text-input__hint';
            message.textContent = error || hint;
            message.style.cssText = `
                font-size: var(--cl-font-size-sm);
                color: ${error ? 'var(--cl-danger)' : 'var(--cl-text-muted)'};
            `;
            container.appendChild(message);
            this.message = message;
        }

        return container;
    }

    _validateSecurity(value) {
        if (!value) return true;

        const sqlRisk = hasSqlInjectionRisk(value);
        const pathRisk = hasPathTraversalRisk(value);

        if (sqlRisk) {
            this.setError('Security Alert: SQL Injection risk detected.');
            console.warn('[Security] SQL Injection pattern found:', value);
            return false;
        }

        if (pathRisk) {
            this.setError('Security Alert: Path Traversal risk detected.');
            console.warn('[Security] Path Traversal pattern found:', value);
            return false;
        }

        this.clearError();
        return true;
    }

    getValue() {
        return this.input.value;
    }

    setValue(value) {
        this.input.value = value;
    }

    clear() {
        this.setValue('');
        this.clearError();
    }

    setError(error) {
        this.options.error = error;
        this.input.style.borderColor = error ? 'var(--cl-danger)' : 'var(--cl-border)';

        if (!this.message) {
            const message = document.createElement('span');
            message.className = 'text-input__error';
            message.style.cssText = `font-size: var(--cl-font-size-sm); color: var(--cl-danger);`;
            this.element.appendChild(message);
            this.message = message;
        }

        this.message.textContent = error;
        this.message.style.color = 'var(--cl-danger)';
        this.input.style.boxShadow = `0 0 0 3px rgba(var(--cl-danger-rgb), 0.1)`;
    }

    clearError() {
        this.options.error = '';
        if (this.input) {
            this.input.style.borderColor = 'var(--cl-border)';
            this.input.style.boxShadow = 'none';
        }
        if (this.message) {
            this.message.textContent = this.options.hint || '';
            this.message.style.color = 'var(--cl-text-muted)';
            if (!this.options.hint) {
                this.message.textContent = '';
            }
        }
    }

    focus() {
        this.input.focus();
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

export default TextInput;
