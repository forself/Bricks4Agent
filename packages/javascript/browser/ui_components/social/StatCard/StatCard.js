/**
 * StatCard - 統計數字卡片元件
 *
 * 顯示統計數據，包含圖示、數值、標籤和趨勢指示。
 * 適用於 Profile 頁面的數據摘要區塊。
 *
 * @author MAGI System
 * @version 1.0.0
 *
 * @example
 * const card = new StatCard({
 *     icon: '👥',
 *     label: '成員數',
 *     value: 42,
 *     trend: 'up',
 *     trendValue: '+5',
 *     color: 'var(--cl-primary)'
 * });
 * card.mount('#stat-container');
 */

import { escapeHtml } from '../../utils/security.js';

export class StatCard {
    /**
     * @param {Object} options
     * @param {string} options.icon - 圖示（emoji 或文字）
     * @param {string} options.label - 標籤文字
     * @param {number|string} options.value - 數值
     * @param {string|null} options.trend - 趨勢方向：'up'|'down'|null
     * @param {string} options.trendValue - 趨勢文字（如 '+5'、'-3%'）
     * @param {string} options.color - 主題色
     * @param {Function|null} options.onClick - 點擊回調
     */
    constructor(options = {}) {
        this.options = {
            icon: '',
            label: '',
            value: 0,
            trend: null,
            trendValue: '',
            color: 'var(--cl-primary)',
            onClick: null,
            ...options
        };

        this.element = null;
    }

    /**
     * CSP style-src 'self' 相容:樣式於掛載後以 CSSOM 指派(原 <style> 注入已移除,值逐字等值)。
     * :hover → mouseenter/mouseleave 事件(庫慣例)。
     */
    _applyStyles() {
        const el = this.element;
        if (!el) return;
        const clickable = !!this.options.onClick;
        el.style.cssText =
            'background: var(--cl-bg); border-radius: var(--cl-radius-xl); padding: 20px;' +
            ' display: flex; align-items: center; gap: 16px; box-shadow: var(--cl-shadow-sm);' +
            ' transition: transform var(--cl-transition), box-shadow var(--cl-transition);' +
            (clickable ? ' cursor: pointer;' : '');
        el.addEventListener('mouseenter', () => {
            el.style.transform = 'translateY(-2px)';
            el.style.boxShadow = 'var(--cl-shadow-md)';
        });
        el.addEventListener('mouseleave', () => {
            el.style.transform = '';
            el.style.boxShadow = 'var(--cl-shadow-sm)';
        });

        const icon = el.querySelector('.social-stat-card__icon');
        if (icon) {
            icon.style.cssText =
                'width: 48px; height: 48px; border-radius: var(--cl-radius-xl); display: flex;' +
                ' align-items: center; justify-content: center; font-size: 22px; flex-shrink: 0;' +
                ` background: ${this._getIconBackground(this.options.color)}; color: ${this.options.color};`;
        }
        const content = el.querySelector('.social-stat-card__content');
        if (content) content.style.cssText = 'flex: 1; min-width: 0;';
        const value = el.querySelector('.social-stat-card__value');
        if (value) value.style.cssText = 'font-size: var(--cl-font-size-3xl); font-weight: 700; color: var(--cl-text); line-height: 1.2;';
        const label = el.querySelector('.social-stat-card__label');
        if (label) label.style.cssText = 'font-size: var(--cl-font-size-md); color: var(--cl-text-secondary); margin-top: 2px;';
        const trend = el.querySelector('.social-stat-card__trend');
        if (trend) {
            const trendColor = trend.classList.contains('social-stat-card__trend--up') ? 'var(--cl-success)' : 'var(--cl-danger)';
            trend.style.cssText =
                'font-size: var(--cl-font-size-sm); font-weight: 600; display: inline-flex;' +
                ` align-items: center; gap: 2px; margin-left: 8px; color: ${trendColor};`;
        }
    }

    /**
     * 產生 HTML 字串
     * @returns {string}
     */
    toHTML() {
        const { icon, label, value, trend, trendValue, color, onClick } = this.options;
        const clickClass = onClick ? ' social-stat-card--clickable' : '';
        const safeLabel = escapeHtml(label);
        const safeValue = escapeHtml(String(value));
        const safeIcon = escapeHtml(icon);
        const safeTrendValue = escapeHtml(trendValue);
        // 圖示背景/主題色改於 _applyStyles() 以 CSSOM 指派(CSP 相容)

        let trendHTML = '';
        if (trend && trendValue) {
            const arrow = trend === 'up' ? '↑' : '↓';
            trendHTML = `<span class="social-stat-card__trend social-stat-card__trend--${trend}">${arrow} ${safeTrendValue}</span>`;
        }

        return `<div class="social-stat-card${clickClass}">
            <div class="social-stat-card__icon">
                ${safeIcon}
            </div>
            <div class="social-stat-card__content">
                <div class="social-stat-card__value">
                    ${safeValue}
                    ${trendHTML}
                </div>
                <div class="social-stat-card__label">${safeLabel}</div>
            </div>
        </div>`;
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
        this.element = target.querySelector('.social-stat-card');
        this._applyStyles();

        if (this.options.onClick && this.element) {
            this.element.addEventListener('click', this.options.onClick);
        }
    }

    update(options) {
        Object.assign(this.options, options);
        if (this.element && this.element.parentNode) {
            this.mount(this.element.parentNode);
        }
    }

    _getIconBackground(color) {
        if (!color || typeof color !== 'string') {
            return 'var(--cl-primary-soft-subtle)';
        }

        return `color-mix(in srgb, ${color} 12%, transparent)`;
    }

    destroy() {
        if (this.element) {
            this.element.remove();
            this.element = null;
        }
    }
}

export default StatCard;
