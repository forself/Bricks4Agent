/**
 * Timeline - 垂直時間軸元件
 *
 * 按時間排列的事件列表，左側顯示時間，右側顯示內容卡片，
 * 中間用垂直連接線串聯。支援按月份分組和類型色標。
 *
 * @author MAGI System
 * @version 1.0.0
 *
 * @example
 * const timeline = new Timeline({
 *     items: [
 *         { timestamp: '2026-02-15', type: '緊急事件', color: 'var(--cl-danger)',
 *           title: '販毒案件', description: '於台北市查獲...', onClick: () => {} },
 *         { timestamp: '2026-01-20', type: '一般活動', color: 'var(--cl-primary)',
 *           title: '聚會活動', description: '成員聚會...' }
 *     ],
 *     grouped: true
 * });
 * timeline.mount('#timeline-container');
 */

import { escapeHtml } from '../../utils/security.js';
import Locale from '../../i18n/index.js';

export class Timeline {
    /** 預設類型色彩 */
    static TYPE_COLORS = {
        '緊急事件': 'var(--cl-danger)',
        '一般活動': 'var(--cl-primary)',
        '公開資訊': 'var(--cl-success)',
        '群眾事件': 'var(--cl-warning)',
        '選舉動態': 'var(--cl-purple)',
        '國際動態': 'var(--cl-info)',
        '其他': 'var(--cl-text-secondary)'
    };

    /**
     * @param {Object} options
     * @param {Object[]} options.items - 時間軸項目
     * @param {string} options.items[].timestamp - 時間（ISO 字串或可解析日期）
     * @param {string} options.items[].type - 類型標籤
     * @param {string} options.items[].icon - 圖示（emoji，可選）
     * @param {string} options.items[].color - Marker 顏色（可選，會依 type 自動選色）
     * @param {string} options.items[].title - 標題
     * @param {string} options.items[].description - 描述文字
     * @param {Function} options.items[].onClick - 點擊回調（可選）
     * @param {boolean} options.grouped - 是否按月份分組
     * @param {string} options.emptyText - 無資料文字
     */
    constructor(options = {}) {
        this.options = {
            items: [],
            grouped: true,
            emptyText: Locale.t('timeline.emptyText'),
            ...options
        };

        this.element = null;
        this._injectStyles();
    }

    _injectStyles() {
        if (document.getElementById('social-timeline-styles')) return;

        const style = document.createElement('style');
        style.id = 'social-timeline-styles';
        style.textContent = `
            .social-timeline {
                position: relative;
                padding: 0;
            }
            .social-timeline__group-label {
                font-size: var(--cl-font-size-lg);
                font-weight: 600;
                color: var(--cl-text-heading);
                padding: 12px 0 8px 0;
                border-bottom: 1px solid var(--cl-border-subtle);
                margin-bottom: 16px;
            }
            .social-timeline__list {
                position: relative;
                padding-left: 32px;
            }
            /* 垂直連接線 */
            .social-timeline__list::before {
                content: '';
                position: absolute;
                left: 11px;
                top: 0;
                bottom: 0;
                width: 2px;
                background: var(--cl-border-medium);
            }
            .social-timeline__item {
                position: relative;
                padding-bottom: 24px;
            }
            .social-timeline__item:last-child {
                padding-bottom: 0;
            }
            /* Marker 圓點 */
            .social-timeline__marker {
                position: absolute;
                left: -32px;
                top: 4px;
                width: 22px;
                height: 22px;
                border-radius: var(--cl-radius-round);
                display: flex;
                align-items: center;
                justify-content: center;
                font-size: var(--cl-font-size-xs);
                color: var(--cl-text-inverse);
                border: 3px solid var(--cl-bg);
                box-shadow: 0 0 0 2px var(--cl-border-medium);
                z-index: 1;
            }
            .social-timeline__card {
                background: var(--cl-bg);
                border-radius: var(--cl-radius-lg);
                padding: 14px 16px;
                box-shadow: var(--cl-shadow-sm);
                transition: box-shadow var(--cl-transition);
            }
            .social-timeline__card:hover {
                box-shadow: var(--cl-shadow-md);
            }
            .social-timeline__card--clickable {
                cursor: pointer;
            }
            .social-timeline__header {
                display: flex;
                align-items: center;
                gap: 8px;
                margin-bottom: 6px;
                flex-wrap: wrap;
            }
            .social-timeline__type {
                font-size: var(--cl-font-size-xs);
                padding: 2px 8px;
                border-radius: var(--cl-radius-lg);
                color: var(--cl-text-inverse);
                font-weight: 500;
            }
            .social-timeline__time {
                font-size: var(--cl-font-size-sm);
                color: var(--cl-text-dim);
            }
            .social-timeline__title {
                font-size: var(--cl-font-size-lg);
                font-weight: 600;
                color: var(--cl-text);
                margin-bottom: 4px;
            }
            .social-timeline__desc {
                font-size: var(--cl-font-size-md);
                color: var(--cl-text-secondary);
                line-height: 1.5;
                display: -webkit-box;
                -webkit-line-clamp: 2;
                -webkit-box-orient: vertical;
                overflow: hidden;
            }
            .social-timeline__empty {
                text-align: center;
                padding: 40px 20px;
                color: var(--cl-text-dim);
                font-size: var(--cl-font-size-lg);
            }
        `;
        document.head.appendChild(style);
    }

    /** 格式化日期 */
    _formatDate(dateStr) {
        try {
            const d = new Date(dateStr);
            const y = d.getFullYear();
            const m = String(d.getMonth() + 1).padStart(2, '0');
            const day = String(d.getDate()).padStart(2, '0');
            return `${y}-${m}-${day}`;
        } catch {
            return dateStr || '';
        }
    }

    /** 取得月份標籤 */
    _getMonthLabel(dateStr) {
        try {
            const d = new Date(dateStr);
            return Locale.t('timeline.monthGroup', { year: d.getFullYear(), month: d.getMonth() + 1 });
        } catch {
            return Locale.t('timeline.unknownTime');
        }
    }

    /** 取得類型顏色 */
    _getColor(item) {
        return item.color || Timeline.TYPE_COLORS[item.type] || Timeline.TYPE_COLORS['其他'];
    }

    /** 渲染單一項目 */
    _renderItem(item, index) {
        const color = this._getColor(item);
        const icon = item.icon || '';
        const safeTitle = escapeHtml(item.title || '');
        const safeDesc = escapeHtml(item.description || '');
        const safeType = escapeHtml(item.type || '');
        const timeStr = this._formatDate(item.timestamp);
        const clickClass = item.onClick ? ' social-timeline__card--clickable' : '';

        return `<div class="social-timeline__item" data-index="${index}">
            <div class="social-timeline__marker" style="background:${color};">${escapeHtml(icon)}</div>
            <div class="social-timeline__card${clickClass}">
                <div class="social-timeline__header">
                    ${safeType ? `<span class="social-timeline__type" style="background:${color};">${safeType}</span>` : ''}
                    <span class="social-timeline__time">${escapeHtml(timeStr)}</span>
                </div>
                ${safeTitle ? `<div class="social-timeline__title">${safeTitle}</div>` : ''}
                ${safeDesc ? `<div class="social-timeline__desc">${safeDesc}</div>` : ''}
            </div>
        </div>`;
    }

    /**
     * 產生 HTML 字串
     * @returns {string}
     */
    toHTML() {
        const { items, grouped, emptyText } = this.options;

        if (!items || items.length === 0) {
            return `<div class="social-timeline">
                <div class="social-timeline__empty">${escapeHtml(emptyText)}</div>
            </div>`;
        }

        // 按時間降序排列
        const sorted = [...items].sort((a, b) =>
            new Date(b.timestamp) - new Date(a.timestamp)
        );

        if (!grouped) {
            const itemsHTML = sorted.map((item, i) => this._renderItem(item, i)).join('');
            return `<div class="social-timeline">
                <div class="social-timeline__list">${itemsHTML}</div>
            </div>`;
        }

        // 按月份分組
        const groups = new Map();
        sorted.forEach((item, i) => {
            const label = this._getMonthLabel(item.timestamp);
            if (!groups.has(label)) groups.set(label, []);
            groups.get(label).push({ item, index: i });
        });

        let html = '';
        for (const [label, entries] of groups) {
            const itemsHTML = entries.map(({ item, index }) => this._renderItem(item, index)).join('');
            html += `<div class="social-timeline__group-label">${escapeHtml(label)}</div>
                     <div class="social-timeline__list">${itemsHTML}</div>`;
        }

        return `<div class="social-timeline">${html}</div>`;
    }

    /**
     * 掛載到容器
     * @param {HTMLElement|string} container
     */
    mount(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (!target) return;

        target.innerHTML = this.toHTML();
        this.element = target.querySelector('.social-timeline');

        // 綁定點擊事件（事件委派）
        if (this.element) {
            this.element.addEventListener('click', (e) => {
                const card = e.target.closest('.social-timeline__card--clickable');
                if (!card) return;
                const item = card.closest('.social-timeline__item');
                const index = parseInt(item?.dataset.index);
                const sorted = [...this.options.items].sort((a, b) =>
                    new Date(b.timestamp) - new Date(a.timestamp)
                );
                if (sorted[index]?.onClick) {
                    sorted[index].onClick(sorted[index]);
                }
            });
        }
    }

    update(options) {
        Object.assign(this.options, options);
        if (this.element && this.element.parentNode) {
            this.mount(this.element.parentNode);
        }
    }

    destroy() {
        if (this.element) {
            this.element.remove();
            this.element = null;
        }
    }
}

export default Timeline;
