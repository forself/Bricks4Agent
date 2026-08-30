import { createComponentState } from '../../utils/component-state.js';
// 跨元件組合一律走公開入口(不帶 query 版本號),以符合 validate:ui-library 的組合面規則
import { StatCard } from '../../social/StatCard/index.js';

/**
 * StatGrid — 指標網格(複合)。grid of StatCard。確定性。
 */
export class StatGrid {
    constructor(options = {}) {
        this.options = { stats: [], columns: 4, ...options };
        this.element = null;
        this._children = [];
        this._state = createComponentState(
            { lifecycle: 'created', visibility: 'visible' },
            {
                MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
                SHOW: (s) => ({ ...s, visibility: 'visible' }),
                HIDE: (s) => ({ ...s, visibility: 'hidden' }),
                DESTROY: (s) => ({ ...s, lifecycle: 'destroyed' })
            }
        );
        this._create();
        this._applyState();
    }

    _create() {
        this.element = document.createElement('div');
        this.element.className = 'cl-statgrid';
        const cols = Math.max(1, parseInt(this.options.columns, 10) || 1);
        this.element.style.cssText = `
            display: grid;
            gap: 12px;
            grid-template-columns: repeat(${cols}, minmax(0, 1fr));
        `;
        (Array.isArray(this.options.stats) ? this.options.stats : []).forEach((stat) => {
            // 每張卡片各自掛在獨立 host:StatCard.mount() 會覆寫容器 innerHTML,
            // 共用同一個容器會讓後一張卡片洗掉前一張。
            const host = document.createElement('div');
            host.className = 'cl-statgrid__item';
            this.element.appendChild(host);
            // 展開 stat 以保留 icon/color/trend/detail 等所有欄位
            const card = new StatCard({ ...stat, label: stat.label ?? '', value: stat.value ?? 0 });
            (card.mount ? card.mount(host) : card.render?.(host));
            this._children.push(card);
        });
    }

    _applyState() { if (this.element) this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : 'grid'; }
    snapshot() { return this._state.snapshot(); }
    send(e, p = null) { const n = this._state.send(e, p); this._applyState(); return n; }

    mount(container) {
        const t = typeof container === 'string' ? document.querySelector(container) : container;
        if (!t) { console.warn('[StatGrid] mount target not found:', container); return this; }
        t.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this._children.forEach((c) => c.destroy?.()); this._children = []; this.send('DESTROY'); this.element?.remove(); this.element = null; }
}

export default StatGrid;
