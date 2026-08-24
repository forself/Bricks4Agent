/**
 * BasicButton Component
 * 一般操作按鈕元件 - 確認、取消、搜尋、重設等通用操作
 */
import Locale from '../../i18n/index.js';
import { Icon } from '../Icon/index.js';


export class BasicButton {
    static TYPES = {
        // 基本類型（無預設樣式）
        PLAIN: 'plain',       // 完全無樣式，純文字
        CUSTOM: 'custom',     // 自訂樣式，可完全控制

        // 確認/取消類
        CONFIRM: 'confirm',
        YES: 'yes',
        CANCEL: 'cancel',
        NO: 'no',
        DONE: 'done',
        CLOSE: 'close',

        // 資料操作類
        SEARCH: 'search',
        CLEAR: 'clear',
        RESET: 'reset',
        SAVE: 'save',
        APPLY: 'apply',
        COPY: 'copy',
        REFRESH: 'refresh',
        DELETE: 'delete',

        // 列表操作類
        ADD_ROW: 'addRow',
        SELECT_ALL: 'selectAll',
        DESELECT_ALL: 'deselectAll',

        // 導航類
        BACK: 'back',
        NEXT: 'next',
        PREV: 'prev',

        // 展開收合類
        EXPAND_ALL: 'expandAll',
        COLLAPSE_ALL: 'collapseAll'
    };

    static ICONS = {
        plain: {
            color: 'var(--cl-text)',
            label: '',
            icon: ''
        },
        custom: {
            color: 'var(--cl-text)',
            label: '',
            icon: ''
        },
        delete: {
            color: 'var(--cl-danger)',
            label: Locale.t('basicButton.delete'),
            icon: 'delete'
        },
        confirm: {
            color: 'var(--cl-success)',
            label: Locale.t('basicButton.confirm'),
            icon: 'check'
        },
        yes: {
            color: 'var(--cl-success)',
            label: Locale.t('basicButton.yes'),
            icon: 'check'
        },
        cancel: {
            color: 'var(--cl-grey)',
            label: Locale.t('basicButton.cancel'),
            icon: 'close'
        },
        no: {
            color: 'var(--cl-danger)',
            label: Locale.t('basicButton.no'),
            icon: 'close'
        },
        done: {
            color: 'var(--cl-primary)',
            label: Locale.t('basicButton.done'),
            icon: 'done-all'
        },
        close: {
            color: 'var(--cl-grey-dark)',
            label: Locale.t('basicButton.close'),
            icon: 'close'
        },
        search: {
            color: 'var(--cl-primary)',
            label: Locale.t('basicButton.search'),
            icon: 'search'
        },
        clear: {
            color: 'var(--cl-warning)',
            label: Locale.t('basicButton.clear'),
            icon: 'close'
        },
        reset: {
            color: 'var(--cl-purple)',
            label: Locale.t('basicButton.reset'),
            icon: 'refresh'
        },
        save: {
            color: 'var(--cl-success)',
            label: Locale.t('basicButton.save'),
            icon: 'save'
        },
        apply: {
            color: 'var(--cl-cyan)',
            label: Locale.t('basicButton.apply'),
            icon: 'check'
        },
        copy: {
            color: 'var(--cl-blue-grey)',
            label: Locale.t('basicButton.copy'),
            icon: 'content-copy'
        },
        refresh: {
            color: 'var(--cl-info)',
            label: Locale.t('basicButton.refresh'),
            icon: 'refresh'
        },
        addRow: {
            color: 'var(--cl-light-green)',
            label: Locale.t('basicButton.addRow'),
            icon: 'add-row'
        },
        selectAll: {
            color: 'var(--cl-indigo)',
            label: Locale.t('basicButton.selectAll'),
            icon: 'select-all'
        },
        deselectAll: {
            color: 'var(--cl-grey)',
            label: Locale.t('basicButton.deselectAll'),
            icon: 'deselect-all'
        },
        back: {
            color: 'var(--cl-blue-grey)',
            label: Locale.t('basicButton.back'),
            icon: 'arrow-back'
        },
        next: {
            color: 'var(--cl-primary)',
            label: Locale.t('basicButton.next'),
            icon: 'arrow-forward'
        },
        prev: {
            color: 'var(--cl-blue-grey)',
            label: Locale.t('basicButton.prev'),
            icon: 'chevron-left'
        },
        expandAll: {
            color: 'var(--cl-brown)',
            label: Locale.t('basicButton.expandAll'),
            icon: 'chevron-down'
        },
        collapseAll: {
            color: 'var(--cl-brown)',
            label: Locale.t('basicButton.collapseAll'),
            icon: 'chevron-up'
        }
    };

    /**
     * 建立一般操作按鈕
     * @param {Object} options
     * @param {string} options.type - 按鈕類型
     * @param {Function} options.onClick - 點擊回調
     * @param {string} options.size - 尺寸 (small, medium, large)
     * @param {string} options.variant - 樣式 (primary, secondary, text, icon, plain)
     * @param {boolean} options.showIcon - 是否顯示圖示
     * @param {string} options.customLabel - 自訂標籤文字
     * @param {boolean} options.disabled - 停用狀態
     * @param {boolean} options.loading - 載入狀態
     * @param {boolean} options.fullWidth - 是否全寬
     */
    constructor(options = {}) {
        // 根據類型決定預設 variant
        let defaultVariant = 'primary';
        if (options.type === 'plain' || options.type === 'custom') {
            defaultVariant = 'plain'; // 無樣式按鈕預設為 plain 變體
        }

        this.options = {
            type: 'confirm',
            onClick: null,
            size: 'medium',
            variant: options.variant || defaultVariant, // 優先使用傳入的 variant
            showIcon: true,
            customLabel: null,
            disabled: false,
            loading: false,
            fullWidth: false,
            ...options
        };

        this.element = this._createElement();
    }

    _getSizeStyles() {
        const sizes = {
            small: { padding: '6px 14px', fontSize: 'var(--cl-font-size-sm)', iconSize: '14px', gap: '4px' },
            medium: { padding: '8px 18px', fontSize: 'var(--cl-font-size-lg)', iconSize: '16px', gap: '6px' },
            large: { padding: '12px 28px', fontSize: 'var(--cl-font-size-xl)', iconSize: '20px', gap: '8px' }
        };
        return sizes[this.options.size] || sizes.medium;
    }

    _getVariantStyles(iconConfig) {
        const variants = {
            primary: {
                base: { background: iconConfig.color, color: 'var(--cl-text-inverse)', border: 'none' },
                hover: { filter: 'brightness(0.9)' }
            },
            secondary: {
                base: { background: 'var(--cl-bg)', color: iconConfig.color, border: `2px solid ${iconConfig.color}` },
                hover: { background: `${iconConfig.color}10` }
            },
            text: {
                base: { background: 'transparent', color: iconConfig.color, border: 'none' },
                hover: { background: `${iconConfig.color}15` }
            },
            icon: {
                base: { background: 'transparent', color: iconConfig.color, border: 'none', padding: '8px', borderRadius: '50%' },
                hover: { background: `${iconConfig.color}15` }
            },
            plain: {
                base: { background: 'var(--cl-bg)', color: 'var(--cl-text)', border: '1px solid var(--cl-border-light)' },
                hover: { background: 'var(--cl-bg-secondary)', color: 'var(--cl-text-dark)', border: '1px solid var(--cl-border-dark)' }
            }
        };
        return variants[this.options.variant] || variants.primary;
    }

    _createElement() {
        const { type, showIcon, customLabel, disabled, variant, fullWidth } = this.options;
        const defaultIconConfig = BasicButton.ICONS[type] || BasicButton.ICONS.confirm;
        const iconConfig = this.options.icon
            ? { ...defaultIconConfig, icon: this.options.icon }
            : defaultIconConfig;
        const sizeStyles = this._getSizeStyles();
        const variantStyles = this._getVariantStyles(iconConfig);
        const isIconOnly = variant === 'icon';
        const label = customLabel || iconConfig.label;

        const button = document.createElement('button');
        button.className = `basic-btn basic-btn--${type} basic-btn--${variant}`;
        button.setAttribute('type', 'button');
        button.setAttribute('title', label);
        button.setAttribute('aria-label', label);
        button.disabled = disabled;

        // 樣式
        const baseStyles = variantStyles.base;
        button.style.cssText = `
            display: inline-flex;
            align-items: center;
            justify-content: center;
            gap: ${sizeStyles.gap};
            padding: ${isIconOnly ? sizeStyles.iconSize : sizeStyles.padding};
            font-size: ${sizeStyles.fontSize};
            font-weight: 500;
            font-family: inherit;
            border-radius: ${isIconOnly ? '50%' : '6px'};
            cursor: pointer;
            transition: all var(--cl-transition);
            background: ${baseStyles.background};
            color: ${baseStyles.color};
            border: ${baseStyles.border || 'none'};
            ${fullWidth ? 'width: 100%;' : ''}
            ${disabled ? 'opacity: 0.5; cursor: not-allowed;' : ''}
        `;

        // 圖示
        if (showIcon || isIconOnly) {
            // 如果是 plain/custom 且沒有 icon content，則不顯示
            if (iconConfig.icon) {
                const iconWrapper = document.createElement('span');
                iconWrapper.className = 'basic-btn__icon';
                iconWrapper.style.cssText = `
                    display: inline-flex;
                    width: ${sizeStyles.iconSize};
                    height: ${sizeStyles.iconSize};
                `;
                this._icon = new Icon({ name: iconConfig.icon, size: Number.parseFloat(sizeStyles.iconSize) });
                this._icon.mount(iconWrapper);
                button.appendChild(iconWrapper);
            }
        }

        // 文字
        if (!isIconOnly) {
            const labelSpan = document.createElement('span');
            labelSpan.className = 'basic-btn__label';
            labelSpan.textContent = label;
            button.appendChild(labelSpan);
        }

        // 互動效果
        if (!disabled) {
            button.addEventListener('mouseenter', () => {
                Object.entries(variantStyles.hover).forEach(([key, value]) => {
                    button.style[key] = value;
                });
                button.style.transform = 'translateY(-1px)';
                this._icon?.redraw();
            });

            button.addEventListener('mouseleave', () => {
                Object.entries(variantStyles.base).forEach(([key, value]) => {
                    button.style[key] = value;
                });
                button.style.transform = 'translateY(0)';
                button.style.filter = 'none';
                this._icon?.redraw();
            });

            button.addEventListener('mousedown', () => {
                button.style.transform = 'translateY(0) scale(0.97)';
            });

            button.addEventListener('mouseup', () => {
                button.style.transform = 'translateY(-1px)';
            });

            button.addEventListener('click', (e) => {
                if (this.options.onClick) {
                    this.options.onClick(e, { type: this.options.type });
                }
            });
        }

        this.button = button;
        return button;
    }

    setLoading(loading) {
        this.options.loading = loading;
        this.button.disabled = loading || this.options.disabled;
        this.button.classList.toggle('basic-btn--loading', loading);
    }

    setDisabled(disabled) {
        this.options.disabled = disabled;
        this.button.disabled = disabled;
        this.button.style.opacity = disabled ? '0.5' : '1';
        this.button.style.cursor = disabled ? 'not-allowed' : 'pointer';
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        this._icon?.destroy();
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }

    /**
     * 建立按鈕群組
     */
    static createGroup(buttons, groupOptions = {}) {
        const group = document.createElement('div');
        const components = [];
        group.className = 'basic-btn-group';
        group.style.cssText = `
            display: inline-flex;
            gap: ${groupOptions.gap || '8px'};
            align-items: center;
        `;

        buttons.forEach(btnOptions => {
            const btn = new BasicButton({ ...groupOptions, ...btnOptions });
            components.push(btn);
            group.appendChild(btn.element);
        });

        Object.defineProperty(group, '_components', {
            value: components,
            enumerable: false
        });
        let destroyed = false;
        Object.defineProperty(group, 'destroy', {
            enumerable: false,
            value: () => {
                if (destroyed) return;
                destroyed = true;
                components.splice(0).forEach(component => component.destroy());
                group.remove();
            }
        });

        return group;
    }

    /**
     * 快速建立對話框按鈕組
     */
    static createDialogButtons(config = {}) {
        const {
            confirmLabel = Locale.t('basicButton.confirmLabel'),
            cancelLabel = Locale.t('basicButton.cancelLabel'),
            onConfirm = () => { },
            onCancel = () => { },
            showConfirm = true,
            showCancel = true,
            ...options
        } = config;

        const buttons = [];

        if (showCancel) {
            buttons.push({
                type: 'cancel',
                variant: 'secondary',
                customLabel: cancelLabel,
                onClick: onCancel
            });
        }

        if (showConfirm) {
            buttons.push({
                type: 'confirm',
                variant: 'primary',
                customLabel: confirmLabel,
                onClick: onConfirm
            });
        }

        return BasicButton.createGroup(buttons, options);
    }

    /**
     * 快速建立表單操作按鈕組
     */
    static createFormButtons(config = {}) {
        const {
            onSearch = () => { },
            onClear = () => { },
            onReset = () => { },
            showSearch = true,
            showClear = true,
            showReset = false,
            ...options
        } = config;

        const buttons = [];

        if (showReset) buttons.push({ type: 'reset', variant: 'text', onClick: onReset });
        if (showClear) buttons.push({ type: 'clear', variant: 'secondary', onClick: onClear });
        if (showSearch) buttons.push({ type: 'search', variant: 'primary', onClick: onSearch });

        return BasicButton.createGroup(buttons, options);
    }
}

export default BasicButton;
