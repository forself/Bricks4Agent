/**
 * WorkflowPanel
 * 稽核流程時間軸元件 - 水平佈局，依容器寬自動調整每列節點數（RWD）
 */
import Locale from '../../i18n/index.js';


export class WorkflowPanel {
    // 流程階段定義
    static STAGES = {
        // 基本流程
        Create: { name: '立案', icon: '📝', color: 'var(--cl-success)' },
        Edit: { name: '編輯', icon: '✏️', color: 'var(--cl-primary)' },
        Submit: { name: '陳報', icon: '📤', color: 'var(--cl-warning)' },
        Audit: { name: '審轉', icon: '🔄', color: 'var(--cl-purple)' },
        Approved: { name: '審核', icon: '✅', color: 'var(--cl-cyan)' },
        Close: { name: '歸檔', icon: '📁', color: 'var(--cl-blue-grey)' },
        // 額外流程
        Rej: { name: '退回', icon: '↩️', color: 'var(--cl-danger)' },
        Withdraw: { name: '撤銷', icon: '⏪', color: 'var(--cl-pink)' },
        Cancel: { name: '刪除', icon: '❌', color: 'var(--cl-grey)' },
        TakeNo: { name: '取號', icon: '🔢', color: 'var(--cl-indigo)' },
        Score: { name: '核分', icon: '📊', color: 'var(--cl-brown)' },
        Modify: { name: '異動', icon: '🔧', color: 'var(--cl-deep-orange)' },
        Replenish: { name: '補件', icon: '📎', color: 'var(--cl-light-green)' }
    };

    // RWD 版面常數：節點可收縮到 72px 仍可辨識；連接線最小 24px + 左右 margin 16px
    static NODE_MIN_WIDTH = 72;
    static NODE_MAX_WIDTH = 100;
    static CONNECTOR_MIN_TOTAL = 40; // min-width 24 + margin 16
    static CONNECTOR_MAX_TOTAL = 76; // max 60 + margin 16

    /**
     * @param {Object} options
     * @param {Array} options.data - 流程歷程資料
     * @param {number} options.itemsPerRow - 每列節點數上限（預設 5，實際依容器寬自動計算）
     * @param {Object} options.nextStage - 下一階段資訊 { StageName, NextUnit }
     * @param {Function} options.onNodeClick - 節點點擊回調
     */
    constructor(options = {}) {
        this.options = {
            data: [],
            itemsPerRow: 5,
            nextStage: null,
            showDetails: true,
            onNodeClick: null,
            ...options
        };

        // itemsPerRow 限制範圍：3-7（語意為「每列上限」，實際列數依容器寬自動縮減）
        this.options.itemsPerRow = Math.max(3, Math.min(7, this.options.itemsPerRow));

        // 目前生效的每列節點數（由 ResizeObserver 調整；1 = 垂直單欄模式）
        this._effectiveItemsPerRow = this.options.itemsPerRow;
        this._resizeObserver = null;
        this._rafId = null;

        this.element = this._createElement();
        this._observeResize();
    }

    /**
     * 監看容器寬度變化，自動重算每列節點數（容器導向 RWD）
     */
    _observeResize() {
        if (typeof ResizeObserver === 'undefined') return; // 無 RO 環境維持靜態版面
        this._resizeObserver = new ResizeObserver((entries) => {
            const width = entries[0]?.contentRect?.width || 0;
            if (width <= 0) return; // display:none 等情況忽略
            const n = this._computeItemsPerRow(width);
            if (n === this._effectiveItemsPerRow) return;
            this._effectiveItemsPerRow = n;
            // 以 rAF 重排，避免 ResizeObserver 迴圈警告
            if (this._rafId) cancelAnimationFrame(this._rafId);
            this._rafId = requestAnimationFrame(() => {
                this._rafId = null;
                this._rerender();
            });
        });
        this._resizeObserver.observe(this.element);
    }

    /**
     * 依內容寬計算每列節點數：floor((寬+線min) / (節點min+線min))，上限 itemsPerRow，下限 1
     */
    _computeItemsPerRow(width) {
        const unit = WorkflowPanel.NODE_MIN_WIDTH + WorkflowPanel.CONNECTOR_MIN_TOTAL;
        const n = Math.floor((width + WorkflowPanel.CONNECTOR_MIN_TOTAL) / unit);
        return Math.max(1, Math.min(this.options.itemsPerRow, n));
    }

    /**
     * 清空並重繪面板內容（資料都在 this.options 上）
     */
    _rerender() {
        this.element.innerHTML = '';
        this._renderTimeline(this.element);
    }

    _createElement() {
        const container = document.createElement('div');
        container.className = 'workflow-panel';
        container.style.cssText = `
            font-family: var(--cl-font-family-cjk);
            padding: 20px;
            background: var(--cl-bg);
            border-radius: var(--cl-radius-lg);
            border: 1px solid var(--cl-border-light);
        `;

        this._renderTimeline(container);
        return container;
    }

    _renderTimeline(container) {
        const { data, nextStage } = this.options;
        // 實際每列數由容器寬決定（上限 = options.itemsPerRow）
        const itemsPerRow = this._effectiveItemsPerRow;

        // 依日期排序
        const sortedData = [...data].sort((a, b) =>
            new Date(a.DateTime) - new Date(b.DateTime)
        );

        // 加入下一階段提示（如果有）
        const displayData = [...sortedData];
        if (nextStage) {
            displayData.push({
                StageName: nextStage.StageName,
                DateTime: null,
                UnitName: nextStage.NextUnit || Locale.t('workflowPanel.pending'),
                UserName: '',
                isNext: true
            });
        }

        // 極窄容器（一列放不下 2 節點）：退化為垂直單欄
        if (itemsPerRow < 2) {
            this._renderVertical(container, displayData, sortedData);
            return;
        }

        // 分行
        const rows = [];
        for (let i = 0; i < displayData.length; i += itemsPerRow) {
            rows.push(displayData.slice(i, i + itemsPerRow));
        }

        // 一行寬度「上限」（用於對齊）；實際寬度隨容器以 flex 收縮
        const maxRowWidth = WorkflowPanel.NODE_MAX_WIDTH * itemsPerRow
            + WorkflowPanel.CONNECTOR_MAX_TOTAL * (itemsPerRow - 1);

        // 渲染每一行
        rows.forEach((row, rowIndex) => {
            // 奇數行（從右開始）反轉顯示以形成 S 型
            const isReversed = rowIndex % 2 === 1;

            const rowContainer = document.createElement('div');
            rowContainer.className = 'workflow-row';
            rowContainer.style.cssText = `
                display: flex;
                align-items: flex-start;
                justify-content: ${isReversed ? 'flex-end' : 'flex-start'};
                margin-bottom: ${rowIndex < rows.length - 1 ? '30px' : '0'};
                position: relative;
                width: 100%;
                max-width: ${maxRowWidth}px;
            `;

            const displayRow = isReversed ? [...row].reverse() : row;

            // 儲存所有節點，用於後續附加箭頭
            const nodes = [];

            displayRow.forEach((item, idx) => {
                const actualIndex = isReversed ? row.length - 1 - idx : idx;
                const globalIndex = rowIndex * itemsPerRow + actualIndex;
                const isLast = globalIndex === sortedData.length - 1 && !item.isNext;
                const isNextStage = item.isNext;

                // 節點
                const node = this._createNode(item, isLast, isNextStage);
                rowContainer.appendChild(node);
                nodes.push(node);

                // 連接線（非最後一個項目）
                if (idx < displayRow.length - 1) {
                    const connector = this._createConnector(isReversed);
                    rowContainer.appendChild(connector);
                }
            });

            // 行間連接線 - 附加到該行流向終點的節點正下方
            // 對於正向行（左到右），終點是最右邊 = nodes 最後一個
            // 對於反向行（右到左），終點是最左邊 = nodes 第一個（因為反轉渲染後第一個在左邊）
            if (rowIndex < rows.length - 1 && nodes.length > 0) {
                // 正向行：箭頭在右邊；反向行：箭頭在左邊
                const turningNode = isReversed ? nodes[0] : nodes.at(-1);
                const verticalConnector = this._createVerticalConnector();
                turningNode.style.position = 'relative';
                turningNode.appendChild(verticalConnector);
            }

            container.appendChild(rowContainer);
        });
    }

    /**
     * 垂直單欄模式（極窄容器）：節點直排、節點間以向下箭頭連接
     */
    _renderVertical(container, displayData, sortedData) {
        const column = document.createElement('div');
        column.className = 'workflow-column';
        column.style.cssText = `
            display: flex;
            flex-direction: column;
            align-items: center;
            width: 100%;
        `;

        displayData.forEach((item, index) => {
            const isLast = index === sortedData.length - 1 && !item.isNext;
            const node = this._createNode(item, isLast, item.isNext);
            node.style.flex = '0 0 auto'; // 直欄中取消水平用的伸縮設定
            column.appendChild(node);

            // 節點間的向下連接線
            if (index < displayData.length - 1) {
                column.appendChild(this._createDownConnector());
            }
        });

        container.appendChild(column);
    }

    /**
     * 垂直單欄用的向下連接線（一般流內元素，非絕對定位）
     */
    _createDownConnector() {
        const connector = document.createElement('div');
        connector.className = 'workflow-down-connector';
        connector.style.cssText = `
            flex: 0 0 auto;
            width: 3px;
            height: 24px;
            margin: 6px 0 14px;
            background: linear-gradient(180deg, var(--cl-success), var(--cl-primary));
            border-radius: var(--cl-radius-xs);
            position: relative;
        `;

        // 向下箭頭
        const arrow = document.createElement('div');
        arrow.style.cssText = `
            position: absolute;
            bottom: -8px;
            left: -4px;
            width: 0;
            height: 0;
            border-left: 5px solid transparent;
            border-right: 5px solid transparent;
            border-top: 8px solid var(--cl-primary);
        `;
        connector.appendChild(arrow);

        return connector;
    }

    _createNode(item, isCurrent, isNext) {
        const stage = WorkflowPanel.STAGES[item.StageName] || {
            name: item.StageName,
            icon: '📋',
            color: 'var(--cl-grey)'
        };

        const node = document.createElement('div');
        node.className = 'workflow-node';
        node.style.cssText = `
            display: flex;
            flex-direction: column;
            align-items: center;
            flex: 1 1 ${WorkflowPanel.NODE_MIN_WIDTH}px;
            min-width: ${WorkflowPanel.NODE_MIN_WIDTH}px;
            max-width: ${WorkflowPanel.NODE_MAX_WIDTH}px;
            cursor: ${this.options.onNodeClick && !isNext ? 'pointer' : 'default'};
            opacity: ${isNext ? '0.5' : '1'};
            ${isNext ? 'filter: grayscale(50%);' : ''}
        `;

        // 圖示圓圈
        const circle = document.createElement('div');
        circle.style.cssText = `
            width: 48px;
            height: 48px;
            border-radius: var(--cl-radius-round);
            background: ${isCurrent ? stage.color : (isNext ? 'var(--cl-border-light)' : 'var(--cl-bg)')};
            border: 3px solid ${stage.color};
            display: flex;
            align-items: center;
            justify-content: center;
            font-size: var(--cl-font-size-2xl);
            box-shadow: ${isCurrent ? `0 0 0 4px ${stage.color}40` : 'none'};
            transition: all var(--cl-transition-slow);
        `;
        circle.textContent = stage.icon;

        // 階段名稱
        const label = document.createElement('div');
        label.style.cssText = `
            margin-top: 8px;
            font-size: var(--cl-font-size-md);
            font-weight: ${isCurrent ? '600' : '400'};
            color: ${isCurrent ? stage.color : 'var(--cl-text)'};
        `;
        label.textContent = stage.name;

        // 日期時間
        if (item.DateTime) {
            const dateTime = document.createElement('div');
            dateTime.style.cssText = `
                margin-top: 4px;
                font-size: var(--cl-font-size-xs);
                color: var(--cl-text-placeholder);
            `;
            dateTime.textContent = this._formatDateTime(item.DateTime);
            node.appendChild(circle);
            node.appendChild(label);
            node.appendChild(dateTime);
        } else {
            // 下一階段提示
            const nextLabel = document.createElement('div');
            nextLabel.style.cssText = `
                margin-top: 4px;
                font-size: var(--cl-font-size-xs);
                color: var(--cl-text-placeholder);
                font-style: italic;
            `;
            nextLabel.textContent = isNext ? '(待處理)' : '';
            node.appendChild(circle);
            node.appendChild(label);
            node.appendChild(nextLabel);
        }

        // 單位/使用者資訊
        if (item.UnitName || item.UserName) {
            const info = document.createElement('div');
            info.style.cssText = `
                margin-top: 4px;
                font-size: var(--cl-font-size-xs);
                color: var(--cl-text-secondary);
                text-align: center;
                max-width: 100%;
                overflow: hidden;
                text-overflow: ellipsis;
                white-space: nowrap;
            `;
            const parts = [];
            if (item.UnitName) parts.push(item.UnitName);
            if (item.UserName) parts.push(item.UserName);
            info.textContent = parts.join(' / ');
            info.title = parts.join(' / ');
            node.appendChild(info);
        }

        // 點擊事件
        if (this.options.onNodeClick && !isNext) {
            node.addEventListener('click', () => {
                this.options.onNodeClick(item, stage);
            });
            node.addEventListener('mouseenter', () => {
                circle.style.transform = 'scale(1.1)';
            });
            node.addEventListener('mouseleave', () => {
                circle.style.transform = 'scale(1)';
            });
        }

        // 當前階段標示
        if (isCurrent) {
            const badge = document.createElement('div');
            badge.style.cssText = `
                position: absolute;
                top: -8px;
                background: ${stage.color};
                color: var(--cl-text-inverse);
                font-size: var(--cl-font-size-2xs);
                padding: 2px 6px;
                border-radius: var(--cl-radius-pill);
            `;
            badge.textContent = Locale.t('workflowPanel.currentBadge');
            node.style.position = 'relative';
            node.appendChild(badge);
        }

        return node;
    }

    _createConnector(isReversed) {
        const connector = document.createElement('div');
        connector.className = 'workflow-connector';
        connector.style.cssText = `
            flex: 0 1 auto;
            width: 60px;
            min-width: 24px;
            height: 3px;
            background: linear-gradient(${isReversed ? '270deg' : '90deg'}, var(--cl-primary), var(--cl-success));
            margin: 0 8px;
            border-radius: var(--cl-radius-xs);
            position: relative;
            align-self: center;
            margin-top: -50px;
        `;

        // 箭頭 - 指向流向方向
        // 正向行（左到右）：箭頭在右端，指向右
        // 反向行（右到左）：箭頭在左端，指向左
        const arrow = document.createElement('div');
        arrow.style.cssText = `
            position: absolute;
            ${isReversed ? 'left: -6px;' : 'right: -6px;'}
            top: -4px;
            width: 0;
            height: 0;
            border-top: 5px solid transparent;
            border-bottom: 5px solid transparent;
            ${isReversed ? 'border-right: 8px solid var(--cl-primary);' : 'border-left: 8px solid var(--cl-success);'}
        `;
        connector.appendChild(arrow);

        return connector;
    }

    _createVerticalConnector() {
        const connector = document.createElement('div');
        connector.className = 'workflow-vertical-connector';
        connector.style.cssText = `
            position: absolute;
            left: 50%;
            transform: translateX(-50%);
            bottom: -30px;
            width: 3px;
            height: 30px;
            background: linear-gradient(180deg, var(--cl-success), var(--cl-primary));
            border-radius: var(--cl-radius-xs);
        `;

        // 向下箭頭
        const arrow = document.createElement('div');
        arrow.style.cssText = `
            position: absolute;
            bottom: -8px;
            left: -4px;
            width: 0;
            height: 0;
            border-left: 5px solid transparent;
            border-right: 5px solid transparent;
            border-top: 8px solid var(--cl-primary);
        `;
        connector.appendChild(arrow);

        return connector;
    }

    _formatDateTime(dateTimeStr) {
        if (!dateTimeStr) return '';
        const dt = new Date(dateTimeStr);
        const y = dt.getFullYear();
        const m = String(dt.getMonth() + 1).padStart(2, '0');
        const d = String(dt.getDate()).padStart(2, '0');
        const h = String(dt.getHours()).padStart(2, '0');
        const min = String(dt.getMinutes()).padStart(2, '0');
        return `${y}/${m}/${d} ${h}:${min}`;
    }

    /**
     * 設定資料
     */
    setData(data) {
        this.options.data = data;
        this._rerender();
        return this;
    }

    /**
     * 設定下一階段
     */
    setNextStage(nextStage) {
        this.options.nextStage = nextStage;
        this._rerender();
        return this;
    }

    mount(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        // 清理 ResizeObserver 與待執行的重排
        if (this._resizeObserver) {
            this._resizeObserver.disconnect();
            this._resizeObserver = null;
        }
        if (this._rafId) {
            cancelAnimationFrame(this._rafId);
            this._rafId = null;
        }
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

// 若在模組環境
export default WorkflowPanel;
if (typeof module !== 'undefined' && module.exports) {
    module.exports = { WorkflowPanel };
}
