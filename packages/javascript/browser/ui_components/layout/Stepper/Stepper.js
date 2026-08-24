/**
 * Stepper Component
 * 步驟導覽/精靈(取代 antd Steps、react-stepzilla)
 *
 * 與 WorkflowPanel 的差異:WorkflowPanel 呈現「稽核流程歷史時間軸」(唯讀狀態),
 * Stepper 呈現「目前進行到第幾步」的輸入式導覽,可搭配分頁表單前進/後退。
 *
 * @example
 * const stepper = new Stepper({
 *     steps: [
 *         { title: '基本資料', description: '案件基本欄位' },
 *         { title: '相關人', description: '嫌疑人/共犯' },
 *         { title: '附件', description: '照片與文件' },
 *         { title: '確認送出' }
 *     ],
 *     current: 0,
 *     clickable: true,
 *     onChange: (index) => showPanel(index)
 * });
 * stepper.mount(container);
 * stepper.next();       // 下一步
 * stepper.prev();       // 上一步
 * stepper.goTo(2);      // 跳到第 3 步
 * stepper.setError(1);  // 標記第 2 步為錯誤
 */
import { escapeHtml } from '../../utils/security.js';
import { createComponentState } from '../../utils/component-state.js';

export class Stepper {
    /**
     * @param {Object} options
     * @param {Array<{title:string, description?:string}>} options.steps - 步驟定義
     * @param {number} options.current - 目前步驟索引(0-based)
     * @param {boolean} options.clickable - 是否可點擊步驟跳轉(預設 false)
     * @param {string} options.direction - 'horizontal' | 'vertical'
     * @param {string} options.size - 'small' | 'medium'
     * @param {Function} options.onChange - (index) => void 步驟變更回呼
     */
    constructor(options = {}) {
        this.options = {
            steps: [],
            current: 0,
            complete: false,
            clickable: false,
            direction: 'horizontal',
            size: 'medium',
            onChange: null,
            ...options
        };

        this._state = createComponentState({
            lifecycle: 'created',
            visibility: 'visible',
            current: this._clamp(this.options.current),
            errors: []
        }, {
            MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
            DESTROY: (state) => ({ ...state, lifecycle: 'destroyed' }),
            SHOW: (state) => ({ ...state, visibility: 'visible' }),
            HIDE: (state) => ({ ...state, visibility: 'hidden' }),
            GO_TO: (state, payload) => ({
                ...state,
                current: this._clamp(payload?.index ?? state.current)
            }),
            SET_ERROR: (state, payload) => ({
                ...state,
                errors: state.errors.includes(payload?.index)
                    ? state.errors
                    : [...state.errors, payload?.index]
            }),
            CLEAR_ERROR: (state, payload) => ({
                ...state,
                errors: payload?.index == null
                    ? []
                    : state.errors.filter((i) => i !== payload.index)
            })
        });

        this.element = this._createContainer();
        this._render();
    }

    _clamp(i) {
        const max = Math.max(0, (this.options.steps?.length || 1) - 1);
        return Math.min(Math.max(0, Number(i) || 0), max);
    }

    _createContainer() {
        const el = document.createElement('div');
        el.className = 'cl-stepper';
        return el;
    }

    _render() {
        const { steps, direction, size, clickable, complete } = this.options;
        const { current, errors, visibility } = this._state.snapshot();
        const horizontal = direction !== 'vertical';
        const circle = size === 'small' ? 24 : 32;

        this.element.style.cssText = `
            display: ${visibility === 'hidden' ? 'none' : 'flex'};
            flex-direction: ${horizontal ? 'row' : 'column'};
            align-items: ${horizontal ? 'flex-start' : 'stretch'};
            gap: 0;
            width: 100%;
            font-family: var(--cl-font-family);
        `;
        this.element.textContent = '';

        steps.forEach((step, i) => {
            const isDone = complete || i < current;
            const isActive = !complete && i === current;
            const isError = errors.includes(i);

            const color = isError ? 'var(--cl-danger)'
                : isDone ? 'var(--cl-success)'
                : isActive ? 'var(--cl-primary)'
                : 'var(--cl-text-muted)';
            const bg = isError ? 'var(--cl-danger)'
                : isDone ? 'var(--cl-success)'
                : isActive ? 'var(--cl-primary)'
                : 'var(--cl-bg-secondary)';
            const fg = (isDone || isActive || isError)
                ? 'var(--cl-text-inverse)'
                : 'var(--cl-text-muted)';

            const item = document.createElement('div');
            item.className = `cl-stepper__step${isActive ? ' is-active' : ''}${isDone ? ' is-done' : ''}${isError ? ' is-error' : ''}`;
            item.style.cssText = `
                display: flex;
                flex-direction: ${horizontal ? 'column' : 'row'};
                align-items: ${horizontal ? 'center' : 'flex-start'};
                flex: ${horizontal ? '1 1 0' : 'none'};
                position: relative;
                gap: 8px;
                padding: ${horizontal ? '0' : '0 0 24px 0'};
                cursor: ${clickable ? 'pointer' : 'default'};
                min-width: 0;
            `;

            // circle
            const dot = document.createElement('div');
            dot.style.cssText = `
                width: ${circle}px;
                height: ${circle}px;
                border-radius: var(--cl-radius-round);
                background: ${bg};
                color: ${fg};
                display: flex;
                align-items: center;
                justify-content: center;
                font-size: var(--cl-font-size-md);
                font-weight: 600;
                flex-shrink: 0;
                border: 2px solid ${color};
                transition: var(--cl-transition);
                z-index: 1;
            `;
            dot.textContent = isError ? '!' : isDone ? '✓' : String(i + 1);
            item.appendChild(dot);

            // connector line(畫在前一步與本步之間;第一步不畫)
            if (i > 0) {
                const line = document.createElement('div');
                const lineColor = i <= current ? 'var(--cl-success)' : 'var(--cl-border)';
                if (horizontal) {
                    line.style.cssText = `
                        position: absolute;
                        top: ${circle / 2}px;
                        right: calc(50% + ${circle / 2 + 4}px);
                        width: calc(100% - ${circle + 8}px);
                        height: 2px;
                        background: ${lineColor};
                    `;
                } else {
                    line.style.cssText = `
                        position: absolute;
                        left: ${circle / 2}px;
                        bottom: calc(100% - 0px);
                        transform: translateX(-1px);
                        width: 2px;
                        height: 24px;
                        background: ${lineColor};
                    `;
                }
                item.appendChild(line);
            }

            // text
            const text = document.createElement('div');
            text.style.cssText = `
                display: flex;
                flex-direction: column;
                align-items: ${horizontal ? 'center' : 'flex-start'};
                gap: 2px;
                min-width: 0;
                text-align: ${horizontal ? 'center' : 'left'};
            `;
            const title = document.createElement('div');
            title.style.cssText = `
                font-size: var(--cl-font-size-md);
                font-weight: ${isActive ? '600' : '400'};
                color: ${isActive || isDone ? 'var(--cl-text)' : 'var(--cl-text-muted)'};
            `;
            title.textContent = step.title ?? '';
            text.appendChild(title);
            if (step.description) {
                const desc = document.createElement('div');
                desc.style.cssText = `
                    font-size: var(--cl-font-size-sm);
                    color: var(--cl-text-muted);
                `;
                desc.textContent = step.description;
                text.appendChild(desc);
            }
            item.appendChild(text);

            if (clickable) {
                item.setAttribute('role', 'button');
                item.setAttribute('tabindex', '0');
                item.setAttribute('aria-label', escapeHtml(step.title ?? `step ${i + 1}`));
                item.addEventListener('click', () => this.goTo(i));
                item.addEventListener('keydown', (e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault();
                        this.goTo(i);
                    }
                });
            }
            item.setAttribute('aria-current', isActive ? 'step' : 'false');

            this.element.appendChild(item);
        });
    }

    getCurrent() {
        return this._state.snapshot().current;
    }

    goTo(index) {
        const prev = this.getCurrent();
        const next = this._state.send('GO_TO', { index }).current;
        this._render();
        if (next !== prev && typeof this.options.onChange === 'function') {
            this.options.onChange(next);
        }
        return next;
    }

    next() {
        return this.goTo(this.getCurrent() + 1);
    }

    prev() {
        return this.goTo(this.getCurrent() - 1);
    }

    setError(index) {
        this._state.send('SET_ERROR', { index });
        this._render();
    }

    clearError(index = null) {
        this._state.send('CLEAR_ERROR', { index });
        this._render();
    }

    setSteps(steps) {
        this.options.steps = Array.isArray(steps) ? steps : [];
        this._state.send('GO_TO', { index: this.getCurrent() });
        this._render();
    }

    snapshot() {
        return this._state.snapshot();
    }

    show() {
        this._state.send('SHOW');
        this._render();
    }

    hide() {
        this._state.send('HIDE');
        this._render();
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) {
            target.appendChild(this.element);
            this._state.send('MOUNT');
        }
        return this;
    }

    destroy() {
        this._state.send('DESTROY');
        if (this.element?.parentNode) this.element.remove();
    }
}

export default Stepper;
