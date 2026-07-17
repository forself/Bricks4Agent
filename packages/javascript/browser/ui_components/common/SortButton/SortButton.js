/**
 * SortButton Component
 * 排序切換按鈕元件 - 用於表格欄位排序
 * 狀態：無排序 (none) → 逆序 (desc) → 正序 (asc) → 無排序 (none)
 */
import { Icon } from '../Icon/index.js';

export class SortButton {
    static STATES = {
        NONE: 'none',    // 未生效
        DESC: 'desc',    // 逆序（降冪）- 點擊第一次
        ASC: 'asc'       // 正序（升冪）- 點擊第二次
    };

    static ICONS = { none: 'sort-none', desc: 'sort-desc', asc: 'sort-asc' };

    static LABELS = { none: '未排序', desc: '降冪排序', asc: '升冪排序' };

    static COLORS = {
        none: 'var(--cl-grey)',
        desc: 'var(--cl-primary)',
        asc: 'var(--cl-success)'
    };

    /**
     * 建立排序按鈕
     * @param {Object} options
     * @param {string} options.field - 欄位名稱
     * @param {string} options.state - 初始狀態 (none, desc, asc)
     * @param {Function} options.onSort - 排序回調 (field, state) => void
     * @param {string} options.size - 尺寸 (small, medium)
     */
    constructor(options = {}) {
        this.options = {
            field: '',
            state: SortButton.STATES.NONE,
            onSort: null,
            size: 'small',
            ...options
        };

        this.state = this.options.state;
        this.element = this._createElement();
    }

    _getSizeValue() {
        return this.options.size === 'small' ? 14 : 18;
    }

    _createElement() {
        const size = this._getSizeValue();

        const button = document.createElement('button');
        button.className = `sort-btn sort-btn--${this.state}`;
        button.setAttribute('type', 'button');
        button.setAttribute('aria-label', SortButton.LABELS[this.state]);
        button.setAttribute('aria-sort', this.state === 'none' ? 'none' : this.state === 'asc' ? 'ascending' : 'descending');

        button.style.cssText = `
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: ${size + 4}px;
            height: ${size + 4}px;
            padding: 2px;
            margin-left: 4px;
            background: transparent;
            border: none;
            border-radius: var(--cl-radius-xs);
            cursor: pointer;
            transition: all var(--cl-transition-fast);
            color: ${SortButton.COLORS[this.state]};
            vertical-align: middle;
        `;

        this._icon = new Icon({ name: SortButton.ICONS[this.state], size });
        this._icon.mount(button);

        // 互動效果
        button.addEventListener('mouseenter', () => {
            button.style.background = 'rgba(0, 0, 0, 0.08)';
        });

        button.addEventListener('mouseleave', () => {
            button.style.background = 'transparent';
        });

        button.addEventListener('click', (e) => {
            e.stopPropagation();
            this._toggleState();
        });

        this.button = button;
        return button;
    }

    _toggleState() {
        // 循環切換：none → desc → asc → none
        const stateOrder = [SortButton.STATES.NONE, SortButton.STATES.DESC, SortButton.STATES.ASC];
        const currentIndex = stateOrder.indexOf(this.state);
        const nextIndex = (currentIndex + 1) % stateOrder.length;

        this.setState(stateOrder[nextIndex]);

        if (this.options.onSort) {
            this.options.onSort(this.options.field, this.state);
        }
    }

    /**
     * 設定排序狀態
     */
    setState(state) {
        this.state = state;
        this.button.className = `sort-btn sort-btn--${state}`;
        this.button.style.color = SortButton.COLORS[state];
        this._icon?.setName(SortButton.ICONS[state]);
        this.button.setAttribute('aria-label', SortButton.LABELS[state]);
        this.button.setAttribute('aria-sort', state === 'none' ? 'none' : state === 'asc' ? 'ascending' : 'descending');
    }

    /**
     * 取得目前狀態
     */
    getState() {
        return this.state;
    }

    /**
     * 重設為未排序
     */
    reset() {
        this.setState(SortButton.STATES.NONE);
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
}

export default SortButton;
