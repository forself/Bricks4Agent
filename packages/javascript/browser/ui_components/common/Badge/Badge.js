import { createComponentState } from '../../utils/component-state.js';

export class Badge {
    static VARIANTS = {
        DEFAULT: 'default',
        PRIMARY: 'primary',
        SUCCESS: 'success',
        WARNING: 'warning',
        DANGER: 'danger',
        INFO: 'info'
    };

    static SIZES = {
        SMALL: 'small',
        MEDIUM: 'medium',
        LARGE: 'large'
    };

    static TYPES = {
        TEXT: 'text',
        COUNT: 'count',
        DOT: 'dot'
    };

    constructor(options = {}) {
        this.options = {
            text: '',
            variant: Badge.VARIANTS.DEFAULT,
            size: Badge.SIZES.MEDIUM,
            type: Badge.TYPES.TEXT,
            maxCount: 99,
            attached: false,
            ...options
        };

        this.element = null;
        this._state = createComponentState(this._buildInitialState(), {
            MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
            SHOW: (state) => ({ ...state, visibility: 'visible' }),
            HIDE: (state) => ({ ...state, visibility: 'hidden' }),
            SET_TEXT: (state, payload) => {
                const rawText = String(payload?.text ?? '');
                const content = {
                    ...state.content,
                    rawText
                };
                return {
                    ...state,
                    content: {
                        ...content,
                        text: this._formatText(content)
                    }
                };
            },
            SET_VARIANT: (state, payload) => ({
                ...state,
                content: {
                    ...state.content,
                    variant: payload?.variant ?? state.content.variant
                }
            }),
            DESTROY: (state) => ({ ...state, lifecycle: 'destroyed' })
        });

        this._injectStyles();
        this._create();
        this._applyState();
    }

    _buildInitialState() {
        const content = {
            rawText: String(this.options.text),
            variant: this.options.variant,
            size: this.options.size,
            type: this.options.type,
            maxCount: this.options.maxCount,
            attached: !!this.options.attached
        };

        return {
            lifecycle: 'created',
            visibility: 'visible',
            content: {
                ...content,
                text: this._formatText(content)
            }
        };
    }

    _injectStyles() {
        if (document.getElementById('badge-component-styles')) return;

        const style = document.createElement('style');
        style.id = 'badge-component-styles';
        style.textContent = `
            .cl-badge {
                display: inline-flex;
                align-items: center;
                justify-content: center;
                font-family: var(--cl-font-family);
                font-weight: 600;
                white-space: nowrap;
                vertical-align: middle;
                border-radius: var(--cl-radius-pill);
                transition: background var(--cl-transition-fast),
                            color var(--cl-transition-fast),
                            transform var(--cl-transition-fast);
                box-sizing: border-box;
                line-height: 1;
            }

            .cl-badge--small {
                font-size: var(--cl-font-size-2xs);
                padding: 2px 6px;
                min-width: 16px;
                height: 16px;
            }

            .cl-badge--medium {
                font-size: var(--cl-font-size-xs);
                padding: 2px 8px;
                min-width: 20px;
                height: 20px;
            }

            .cl-badge--large {
                font-size: var(--cl-font-size-sm);
                padding: 3px 10px;
                min-width: 24px;
                height: 24px;
            }

            .cl-badge--count {
                border-radius: var(--cl-radius-pill);
                padding-left: 6px;
                padding-right: 6px;
            }

            .cl-badge--dot {
                padding: 0;
                border-radius: var(--cl-radius-round);
            }

            .cl-badge--dot.cl-badge--small {
                width: 6px;
                height: 6px;
                min-width: 6px;
            }

            .cl-badge--dot.cl-badge--medium {
                width: 8px;
                height: 8px;
                min-width: 8px;
            }

            .cl-badge--dot.cl-badge--large {
                width: 10px;
                height: 10px;
                min-width: 10px;
            }

            .cl-badge--default {
                background: var(--cl-bg-secondary);
                color: var(--cl-text-secondary);
                border: 1px solid var(--cl-border);
            }

            .cl-badge--primary {
                background: var(--cl-primary);
                color: var(--cl-text-inverse);
            }

            .cl-badge--success {
                background: var(--cl-success);
                color: var(--cl-text-inverse);
            }

            .cl-badge--warning {
                background: var(--cl-warning);
                color: var(--cl-text-inverse);
            }

            .cl-badge--danger {
                background: var(--cl-danger);
                color: var(--cl-text-inverse);
            }

            .cl-badge--info {
                background: var(--cl-info);
                color: var(--cl-text-inverse);
            }

            .cl-badge--attached {
                position: absolute;
                top: 0;
                right: 0;
                transform: translate(50%, -50%);
                z-index: 1;
            }

            .cl-badge--attached.cl-badge--dot {
                top: 2px;
                right: 2px;
                transform: translate(50%, -50%);
            }
        `;
        document.head.appendChild(style);
    }

    _create() {
        this.element = document.createElement('span');
    }

    _formatText(content = this.options) {
        const {
            rawText = this.options.text,
            text = rawText,
            type = this.options.type,
            maxCount = this.options.maxCount
        } = content;

        if (type === Badge.TYPES.COUNT) {
            const num = parseInt(rawText ?? text, 10);
            if (!isNaN(num) && num > maxCount) {
                return `${maxCount}+`;
            }
        }

        return String(rawText ?? text);
    }

    _applyState() {
        if (!this.element) return;

        const state = this.snapshot();
        const { content, visibility } = state;
        const classes = ['cl-badge', `cl-badge--${content.size}`, `cl-badge--${content.variant}`];

        if (content.type === Badge.TYPES.COUNT) {
            classes.push('cl-badge--count');
        } else if (content.type === Badge.TYPES.DOT) {
            classes.push('cl-badge--dot');
        }

        if (content.attached) {
            classes.push('cl-badge--attached');
        }

        this.element.className = classes.join(' ');
        this.element.style.display = visibility === 'hidden' ? 'none' : '';
        this.element.textContent = content.type === Badge.TYPES.DOT ? '' : content.text;
    }

    _syncOptionsFromState(state) {
        this.options.text = state.content.rawText;
        this.options.variant = state.content.variant;
        this.options.size = state.content.size;
        this.options.type = state.content.type;
        this.options.maxCount = state.content.maxCount;
        this.options.attached = state.content.attached;
    }

    snapshot() {
        return this._state.snapshot();
    }

    send(event, payload = null) {
        const nextState = this._state.send(event, payload);
        this._syncOptionsFromState(nextState);
        this._applyState();
        return nextState;
    }

    render(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;

        if (!target) {
            console.warn('[Badge] render target not found:', container);
            return this;
        }

        if (this.options.attached) {
            const pos = getComputedStyle(target).position;
            if (pos === 'static' || pos === '') {
                target.style.position = 'relative';
            }
        }

        target.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    setText(text) {
        this.send('SET_TEXT', { text });
        return this;
    }

    setVariant(variant) {
        this.send('SET_VARIANT', { variant });
        return this;
    }

    show() {
        this.send('SHOW');
        return this;
    }

    hide() {
        this.send('HIDE');
        return this;
    }

    destroy() {
        this.send('DESTROY');
        this.element?.remove();
        this.element = null;
    }
}

export default Badge;
