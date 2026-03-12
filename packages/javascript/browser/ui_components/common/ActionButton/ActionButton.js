/**
 * ActionButton Component
 * 流程操作按鈕元件 - 新增、刪除、編輯、詳細、送出、退回、歸檔、整合
 */
import Locale from '../../i18n/index.js';


export class ActionButton {
    static TYPES = {
        ADD: 'add',
        DELETE: 'delete',
        EDIT: 'edit',
        DETAIL: 'detail',
        SUBMIT: 'submit',
        REJECT: 'reject',
        ARCHIVE: 'archive',
        MERGE: 'merge'
    };

    static ICONS = {
        add: {
            color: 'var(--cl-success)',
            hoverColor: 'var(--cl-success)',
            label: Locale.t('actionButton.add'),
            icon: `<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                <circle cx="12" cy="12" r="10" stroke="currentColor" stroke-width="2"/>
                <path d="M12 7V17M7 12H17" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
            </svg>`
        },
        delete: {
            color: 'var(--cl-danger)',
            hoverColor: 'var(--cl-danger)',
            label: Locale.t('actionButton.delete'),
            icon: `<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M6 7V18C6 19.1 6.9 20 8 20H16C17.1 20 18 19.1 18 18V7" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
                <path d="M4 7H20" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
                <path d="M10 4H14C14.55 4 15 4.45 15 5V7H9V5C9 4.45 9.45 4 10 4Z" stroke="currentColor" stroke-width="2"/>
                <path d="M10 11V16M14 11V16" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
            </svg>`
        },
        edit: {
            color: 'var(--cl-warning)',
            hoverColor: 'var(--cl-warning)',
            label: Locale.t('actionButton.edit'),
            icon: `<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M16.5 3.5L20.5 7.5L7 21H3V17L16.5 3.5Z" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/>
                <path d="M14 6L18 10" stroke="currentColor" stroke-width="2"/>
            </svg>`
        },
        detail: {
            color: 'var(--cl-primary)',
            hoverColor: 'var(--cl-primary-dark)',
            label: Locale.t('actionButton.detail'),
            icon: `<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                <circle cx="11" cy="11" r="7" stroke="currentColor" stroke-width="2"/>
                <path d="M16 16L21 21" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
                <circle cx="11" cy="11" r="1" fill="currentColor"/>
                <path d="M11 8V11" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
            </svg>`
        },
        submit: {
            color: 'var(--cl-teal)',
            hoverColor: 'var(--cl-teal-dark)',
            label: Locale.t('actionButton.submit'),
            icon: `<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M5 12L10 17L20 7" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M20 12V18C20 19.1 19.1 20 18 20H6C4.9 20 4 19.1 4 18V6C4 4.9 4.9 4 6 4H16" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
            </svg>`
        },
        reject: {
            color: 'var(--cl-pink)',
            hoverColor: 'var(--cl-pink-dark)',
            label: Locale.t('actionButton.reject'),
            icon: `<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M9 10L4 15L9 20" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M4 15H15C17.76 15 20 12.76 20 10V4" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
            </svg>`
        },
        archive: {
            color: 'var(--cl-brown)',
            hoverColor: 'var(--cl-brown-dark)',
            label: Locale.t('actionButton.archive'),
            icon: `<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M3 6H21V9H3V6Z" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/>
                <path d="M5 9V19C5 19.55 5.45 20 6 20H18C18.55 20 19 19.55 19 19V9" stroke="currentColor" stroke-width="2"/>
                <path d="M10 13H14" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
            </svg>`
        },
        merge: {
            color: 'var(--cl-purple)',
            hoverColor: 'var(--cl-purple-dark)',
            label: Locale.t('actionButton.merge'),
            icon: `<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                <circle cx="6" cy="6" r="3" stroke="currentColor" stroke-width="2"/>
                <circle cx="18" cy="6" r="3" stroke="currentColor" stroke-width="2"/>
                <circle cx="12" cy="18" r="3" stroke="currentColor" stroke-width="2"/>
                <path d="M6 9V12C6 14 8 16 12 16M18 9V12C18 14 16 16 12 16" stroke="currentColor" stroke-width="2"/>
                <path d="M12 16V15" stroke="currentColor" stroke-width="2"/>
            </svg>`
        },
        verify: {
            color: 'var(--cl-cyan)',
            hoverColor: 'var(--cl-cyan-dark)',
            label: Locale.t('actionButton.verify'),
            icon: `<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M9 12L11 14L15 10" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                <circle cx="12" cy="12" r="9" stroke="currentColor" stroke-width="2"/>
                <path d="M12 3V5M12 19V21M3 12H5M19 12H21" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
            </svg>`
        },
        withdraw: {
            color: 'var(--cl-purple)',
            hoverColor: 'var(--cl-purple-dark)',
            label: Locale.t('actionButton.withdraw'),
            icon: `<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M19 12H5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
                <path d="M12 5L5 12L12 19" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                <circle cx="18" cy="12" r="3" stroke="currentColor" stroke-width="2"/>
            </svg>`
        },
        report: {
            color: 'var(--cl-warning)',
            hoverColor: 'var(--cl-warning)',
            label: Locale.t('actionButton.report'),
            icon: `<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M12 4L12 14" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
                <path d="M8 10L12 14L16 10" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M5 18H19" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
                <path d="M8 20H16" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
            </svg>`
        },
        transfer: {
            color: 'var(--cl-indigo)',
            hoverColor: 'var(--cl-indigo-dark)',
            label: Locale.t('actionButton.transfer'),
            icon: `<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M5 12H19" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
                <path d="M14 7L19 12L14 17" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                <circle cx="7" cy="12" r="2" stroke="currentColor" stroke-width="2"/>
            </svg>`
        },
        approve: {
            color: 'var(--cl-success)',
            hoverColor: 'var(--cl-success)',
            label: Locale.t('actionButton.approve'),
            icon: `<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M4 12L9 17L20 6" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/>
                <circle cx="17" cy="17" r="4" stroke="currentColor" stroke-width="2"/>
                <path d="M17 15V17H19" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
            </svg>`
        },
        modify: {
            color: 'var(--cl-deep-orange)',
            hoverColor: 'var(--cl-deep-orange-dark)',
            label: Locale.t('actionButton.modify'),
            icon: `<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M4 20H20" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
                <path d="M14.5 5.5L18.5 9.5L10 18H6V14L14.5 5.5Z" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/>
                <path d="M12 8L16 12" stroke="currentColor" stroke-width="2"/>
            </svg>`
        }
    };

    /**
     * 建立操作按鈕
     * @param {Object} options
     * @param {string} options.type - 按鈕類型
     * @param {Function} options.onClick - 點擊回調
     * @param {string} options.size - 尺寸 (small, medium, large)
     * @param {string} options.variant - 樣式變體 (icon, outlined, filled, text)
     * @param {boolean} options.showLabel - 是否顯示文字標籤
     * @param {string} options.tooltip - 提示文字
     * @param {boolean} options.disabled - 是否停用
     * @param {boolean} options.loading - 載入狀態
     * @param {boolean} options.confirm - 是否需要確認（適用於刪除等危險操作）
     * @param {string} options.confirmMessage - 確認訊息
     */
    constructor(options = {}) {
        this.options = {
            type: 'add',
            onClick: null,
            size: 'medium',
            variant: 'filled',
            showLabel: true,
            tooltip: '',
            disabled: false,
            loading: false,
            confirm: false,
            confirmMessage: Locale.t('actionButton.confirmMessage'),
            ...options
        };

        this.element = this._createElement();
    }

    _getSizeStyles() {
        const sizes = {
            small: {
                button: 'padding: 6px 12px; font-size: var(--cl-font-size-sm); gap: 4px;',
                icon: 'width: 14px; height: 14px;',
                iconOnly: 'width: 28px; height: 28px; padding: 6px;'
            },
            medium: {
                button: 'padding: 8px 16px; font-size: var(--cl-font-size-lg); gap: 6px;',
                icon: 'width: 18px; height: 18px;',
                iconOnly: 'width: 36px; height: 36px; padding: 8px;'
            },
            large: {
                button: 'padding: 12px 24px; font-size: var(--cl-font-size-xl); gap: 8px;',
                icon: 'width: 22px; height: 22px;',
                iconOnly: 'width: 48px; height: 48px; padding: 12px;'
            }
        };
        return sizes[this.options.size] || sizes.medium;
    }

    _getVariantStyles(iconConfig) {
        const { variant } = this.options;
        const variants = {
            filled: {
                base: `background: ${iconConfig.color}; color: var(--cl-text-inverse); border: none;`,
                hover: `background: ${iconConfig.hoverColor};`
            },
            outlined: {
                base: `background: transparent; color: ${iconConfig.color}; border: 2px solid ${iconConfig.color};`,
                hover: `background: ${iconConfig.color}10;`
            },
            text: {
                base: `background: transparent; color: ${iconConfig.color}; border: none;`,
                hover: `background: ${iconConfig.color}15;`
            },
            icon: {
                base: `background: transparent; color: ${iconConfig.color}; border: none;`,
                hover: `background: ${iconConfig.color}15;`
            }
        };
        return variants[variant] || variants.filled;
    }

    _createElement() {
        const { type, showLabel, tooltip, disabled, variant } = this.options;
        const iconConfig = ActionButton.ICONS[type] || ActionButton.ICONS.add;
        const sizeStyles = this._getSizeStyles();
        const variantStyles = this._getVariantStyles(iconConfig);
        const isIconOnly = variant === 'icon';

        // 建立按鈕
        const button = document.createElement('button');
        button.className = `action-btn action-btn--${type} action-btn--${variant}`;
        button.setAttribute('type', 'button');
        button.setAttribute('title', tooltip || iconConfig.label);
        button.setAttribute('aria-label', iconConfig.label);
        button.disabled = disabled;

        // 基本樣式
        button.style.cssText = `
            display: inline-flex;
            align-items: center;
            justify-content: center;
            border-radius: var(--cl-radius-lg);
            cursor: pointer;
            transition: all var(--cl-transition);
            font-weight: 500;
            font-family: inherit;
            ${variantStyles.base}
            ${isIconOnly ? sizeStyles.iconOnly : sizeStyles.button}
        `;

        // 圖示
        const iconWrapper = document.createElement('span');
        iconWrapper.className = 'action-btn__icon';
        iconWrapper.style.cssText = `
            display: inline-flex;
            align-items: center;
            justify-content: center;
            ${isIconOnly ? 'width: 100%; height: 100%;' : sizeStyles.icon}
        `;
        iconWrapper.innerHTML = iconConfig.icon;
        button.appendChild(iconWrapper);

        // 文字標籤
        if (showLabel && !isIconOnly) {
            const labelSpan = document.createElement('span');
            labelSpan.className = 'action-btn__label';
            labelSpan.textContent = iconConfig.label;
            button.appendChild(labelSpan);
        }

        // Hover 效果
        button.addEventListener('mouseenter', () => {
            if (!disabled) {
                Object.assign(button.style, this._parseStyles(variantStyles.hover));
                button.style.transform = 'translateY(-1px)';
                button.style.boxShadow = `0 4px 12px ${iconConfig.color}30`;
            }
        });

        button.addEventListener('mouseleave', () => {
            Object.assign(button.style, this._parseStyles(variantStyles.base));
            button.style.transform = 'translateY(0)';
            button.style.boxShadow = 'none';
        });

        // 點擊效果
        button.addEventListener('mousedown', () => {
            if (!disabled) {
                button.style.transform = 'translateY(0) scale(0.97)';
            }
        });

        button.addEventListener('mouseup', () => {
            if (!disabled) {
                button.style.transform = 'translateY(-1px)';
            }
        });

        // 點擊處理
        button.addEventListener('click', (e) => this._handleClick(e));

        // 停用樣式
        if (disabled) {
            button.style.opacity = '0.5';
            button.style.cursor = 'not-allowed';
        }

        this.button = button;
        return button;
    }

    _parseStyles(styleString) {
        const styles = {};
        styleString.split(';').forEach(rule => {
            const [prop, value] = rule.split(':').map(s => s.trim());
            if (prop && value) {
                const camelProp = prop.replaceAll(/-([a-z])/g, g => g[1].toUpperCase());
                styles[camelProp] = value;
            }
        });
        return styles;
    }

    _handleClick(e) {
        const { onClick, confirm, confirmMessage, disabled, loading } = this.options;

        if (disabled || loading) return;

        if (confirm) {
            if (!globalThis.confirm(confirmMessage)) {
                return;
            }
        }

        if (onClick) {
            onClick(e, { type: this.options.type });
        }
    }

    /**
     * 設定載入狀態
     */
    setLoading(loading) {
        this.options.loading = loading;
        if (loading) {
            this.button.classList.add('action-btn--loading');
            this.button.disabled = true;
        } else {
            this.button.classList.remove('action-btn--loading');
            this.button.disabled = this.options.disabled;
        }
    }

    /**
     * 設定停用狀態
     */
    setDisabled(disabled) {
        this.options.disabled = disabled;
        this.button.disabled = disabled;
        this.button.style.opacity = disabled ? '0.5' : '1';
        this.button.style.cursor = disabled ? 'not-allowed' : 'pointer';
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
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }

    /**
     * 建立按鈕群組
     */
    static createGroup(buttons, groupOptions = {}) {
        const group = document.createElement('div');
        group.className = 'action-btn-group';
        group.style.cssText = `
            display: inline-flex;
            gap: ${groupOptions.gap || '8px'};
            align-items: center;
            flex-wrap: wrap;
        `;

        buttons.forEach(btnOptions => {
            const btn = new ActionButton({ ...groupOptions, ...btnOptions });
            group.appendChild(btn.element);
        });

        return group;
    }

    /**
     * 建立工具列（常用操作組合）
     */
    static createToolbar(config = {}) {
        const {
            showCrud = true,      // 新增、編輯、刪除、詳細
            showWorkflow = false, // 送出、退回、歸檔、整合
            onAction = () => { },
            ...groupOptions
        } = config;

        const buttons = [];

        if (showCrud) {
            buttons.push(
                { type: 'add', onClick: (e, d) => onAction('add', d) },
                { type: 'edit', onClick: (e, d) => onAction('edit', d) },
                { type: 'delete', onClick: (e, d) => onAction('delete', d), confirm: true, confirmMessage: Locale.t('actionButton.confirmDelete') },
                { type: 'detail', onClick: (e, d) => onAction('detail', d) }
            );
        }

        if (showWorkflow) {
            buttons.push(
                { type: 'submit', onClick: (e, d) => onAction('submit', d) },
                { type: 'reject', onClick: (e, d) => onAction('reject', d), confirm: true, confirmMessage: Locale.t('actionButton.confirmReject') },
                { type: 'archive', onClick: (e, d) => onAction('archive', d) },
                { type: 'merge', onClick: (e, d) => onAction('merge', d) }
            );
        }

        return ActionButton.createGroup(buttons, groupOptions);
    }
}

export default ActionButton;
