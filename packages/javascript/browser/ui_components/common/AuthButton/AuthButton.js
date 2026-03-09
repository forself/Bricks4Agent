/**
 * AuthButton Component
 * 登入/登出按鈕元件
 */
import Locale from '../../i18n/index.js';


export class AuthButton {
    static TYPES = {
        LOGIN: 'login',
        LOGOUT: 'logout'
    };

    static ICONS = {
        login: {
            color: 'var(--cl-success)',
            hoverColor: 'var(--cl-success)',
            label: Locale.t('authButton.login'),
            icon: `<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M15 3H19C20.1 3 21 3.9 21 5V19C21 20.1 20.1 21 19 21H15" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
                <path d="M10 17L15 12L10 7" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M15 12H3" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
            </svg>`
        },
        logout: {
            color: 'var(--cl-danger)',
            hoverColor: 'var(--cl-danger)',
            label: Locale.t('authButton.logout'),
            icon: `<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M9 21H5C3.9 21 3 20.1 3 19V5C3 3.9 3.9 3 5 3H9" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
                <path d="M16 17L21 12L16 7" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M21 12H9" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
            </svg>`
        }
    };

    /**
     * 建立登入/登出按鈕
     * @param {Object} options
     * @param {string} options.type - 'login' 或 'logout'
     * @param {Function} options.onClick - 點擊回調
     * @param {string} options.size - 尺寸 (small, medium, large)
     * @param {string} options.variant - 樣式 (filled, outlined, text, icon)
     * @param {boolean} options.showLabel - 顯示文字
     * @param {string} options.customLabel - 自訂標籤
     * @param {boolean} options.confirm - 是否需確認（登出適用）
     */
    constructor(options = {}) {
        this.options = {
            type: 'login',
            onClick: null,
            size: 'medium',
            variant: 'filled',
            showLabel: true,
            customLabel: null,
            confirm: false,
            confirmMessage: Locale.t('authButton.confirmLogout'),
            ...options
        };

        this.element = this._createElement();
    }

    _getSizeStyles() {
        const sizes = {
            small: { padding: '6px 12px', fontSize: '12px', iconSize: '14px', gap: '4px', iconOnly: '28px' },
            medium: { padding: '8px 16px', fontSize: '14px', iconSize: '18px', gap: '6px', iconOnly: '36px' },
            large: { padding: '12px 24px', fontSize: '16px', iconSize: '22px', gap: '8px', iconOnly: '48px' }
        };
        return sizes[this.options.size] || sizes.medium;
    }

    _createElement() {
        const { type, showLabel, customLabel, variant, confirm, confirmMessage } = this.options;
        const iconConfig = AuthButton.ICONS[type] || AuthButton.ICONS.login;
        const sizeStyles = this._getSizeStyles();
        const isIconOnly = variant === 'icon';
        const label = customLabel || iconConfig.label;

        const button = document.createElement('button');
        button.className = `auth-btn auth-btn--${type} auth-btn--${variant}`;
        button.setAttribute('type', 'button');
        button.setAttribute('title', label);
        button.setAttribute('aria-label', label);

        // 根據變體設定樣式
        let baseBackground, baseColor, baseBorder;
        switch (variant) {
            case 'filled':
                baseBackground = iconConfig.color;
                baseColor = 'var(--cl-text-inverse)';
                baseBorder = 'none';
                break;
            case 'outlined':
                baseBackground = 'transparent';
                baseColor = iconConfig.color;
                baseBorder = `2px solid ${iconConfig.color}`;
                break;
            case 'text':
            case 'icon':
                baseBackground = 'transparent';
                baseColor = iconConfig.color;
                baseBorder = 'none';
                break;
        }

        button.style.cssText = `
            display: inline-flex;
            align-items: center;
            justify-content: center;
            gap: ${sizeStyles.gap};
            padding: ${isIconOnly ? '0' : sizeStyles.padding};
            width: ${isIconOnly ? sizeStyles.iconOnly : 'auto'};
            height: ${isIconOnly ? sizeStyles.iconOnly : 'auto'};
            font-size: ${sizeStyles.fontSize};
            font-weight: 500;
            font-family: inherit;
            background: ${baseBackground};
            color: ${baseColor};
            border: ${baseBorder};
            border-radius: ${isIconOnly ? '50%' : '6px'};
            cursor: pointer;
            transition: all 0.2s ease;
        `;

        // 圖示
        const iconWrapper = document.createElement('span');
        iconWrapper.className = 'auth-btn__icon';
        iconWrapper.style.cssText = `
            display: inline-flex;
            width: ${sizeStyles.iconSize};
            height: ${sizeStyles.iconSize};
        `;
        iconWrapper.innerHTML = iconConfig.icon;
        button.appendChild(iconWrapper);

        // 文字
        if (showLabel && !isIconOnly) {
            const labelSpan = document.createElement('span');
            labelSpan.textContent = label;
            button.appendChild(labelSpan);
        }

        // Hover 效果
        button.addEventListener('mouseenter', () => {
            button.style.transform = 'translateY(-1px)';
            if (variant === 'filled') {
                button.style.background = iconConfig.hoverColor;
            } else {
                button.style.background = `${iconConfig.color}15`;
            }
        });

        button.addEventListener('mouseleave', () => {
            button.style.transform = 'translateY(0)';
            button.style.background = baseBackground;
        });

        button.addEventListener('mousedown', () => {
            button.style.transform = 'scale(0.97)';
        });

        button.addEventListener('mouseup', () => {
            button.style.transform = 'translateY(-1px)';
        });

        // 點擊
        button.addEventListener('click', (e) => {
            if (confirm && !globalThis.confirm(confirmMessage)) {
                return;
            }
            if (this.options.onClick) {
                this.options.onClick(e, { type: this.options.type });
            }
        });

        this.button = button;
        return button;
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

    /**
     * 建立登入登出切換群組
     */
    static createAuthGroup(config = {}) {
        const {
            isLoggedIn = false,
            onLogin = () => { },
            onLogout = () => { },
            ...options
        } = config;

        if (isLoggedIn) {
            return new AuthButton({
                type: 'logout',
                onClick: onLogout,
                confirm: true,
                ...options
            }).element;
        } else {
            return new AuthButton({
                type: 'login',
                onClick: onLogin,
                ...options
            }).element;
        }
    }
}

export default AuthButton;
