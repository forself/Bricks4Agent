/**
 * FeatureCard - 功能展示卡片元件
 * 用於展示產品功能、demo 或特性的卡片
 * 
 * @version 1.0.0
 * @author dF Component Library
 */

export class FeatureCard {
    /**
     * @param {Object} options - 卡片配置
     * @param {string} options.title - 卡片標題
     * @param {string} options.description - 卡片描述
     * @param {string[]} [options.tags=[]] - 標籤陣列
     * @param {string} [options.badge=''] - 徽章文字 (如 'HOT', 'NEW', 'PRO')
     * @param {string} [options.badgeColor='var(--cl-brand-discord)'] - 徽章背景色
     * @param {string} [options.url=''] - 點擊後跳轉的 URL
     * @param {Function} [options.onClick=null] - 點擊事件回調
     * @param {boolean} [options.elevated=true] - 是否有 hover 上升效果
     * @param {Object} [options.customData={}] - 自訂資料
     */
    constructor(options = {}) {
        this.title = options.title || '未命名卡片';
        this.description = options.description || '';
        this.tags = options.tags || [];
        this.badge = options.badge || '';
        this.badgeColor = options.badgeColor || 'var(--cl-brand-discord)';
        this.url = options.url || '';
        this.onClick = options.onClick || null;
        this.elevated = options.elevated !== undefined ? options.elevated : true;
        this.customData = options.customData || {};
        
        this.element = null;
        
        this._init();
    }

    /**
     * 初始化卡片元素
     * @private
     */
    _init() {
        this.element = document.createElement('div');
        this.element.className = 'feature-card';
        
        // 建立卡片結構
        this.element.innerHTML = `
            <div class="feature-card__container">
                <div class="feature-card__header">
                    <h3 class="feature-card__title">
                        ${this._escapeHtml(this.title)}
                        ${this.badge ? `<span class="feature-card__badge" style="background: ${this.badgeColor}">${this._escapeHtml(this.badge)}</span>` : ''}
                    </h3>
                </div>
                <p class="feature-card__description">${this._escapeHtml(this.description)}</p>
                ${this.tags.length > 0 ? `
                    <div class="feature-card__tags">
                        ${this.tags.map(tag => `<span class="feature-card__tag">${this._escapeHtml(tag)}</span>`).join('')}
                    </div>
                ` : ''}
            </div>
        `;

        // 應用樣式
        this._applyStyles();

        // 綁定事件
        this._bindEvents();
    }

    /**
     * 應用卡片樣式
     * @private
     */
    _applyStyles() {
        // 確保樣式已注入
        if (!document.getElementById('feature-card-styles')) {
            const style = document.createElement('style');
            style.id = 'feature-card-styles';
            style.textContent = `
                .feature-card {
                    display: block;
                    text-decoration: none;
                    color: inherit;
                    height: 100%;
                }

                .feature-card__container {
                    background: var(--cl-bg-inverse-soft);
                    border: 1px solid var(--cl-divider-inverse);
                    border-radius: var(--cl-radius-xl);
                    padding: 24px;
                    transition: transform var(--cl-transition-slow), box-shadow var(--cl-transition-slow), border-color var(--cl-transition-slow), background var(--cl-transition-slow);
                    cursor: pointer;
                    height: 100%;
                    display: flex;
                    flex-direction: column;
                }

                .feature-card__container:hover {
                    transform: translateY(-8px);
                    border-color: var(--cl-gradient-start);
                    box-shadow: var(--cl-shadow-lg);
                    background: var(--cl-bg-inverse-soft-hover);
                }

                .feature-card__header {
                    margin-bottom: 12px;
                }

                .feature-card__title {
                    font-size: 1.3rem;
                    font-weight: 600;
                    color: var(--cl-bg);
                    margin: 0;
                    display: flex;
                    align-items: center;
                    gap: 10px;
                    flex-wrap: wrap;
                }

                .feature-card__badge {
                    font-size: 0.7rem;
                    padding: 2px 8px;
                    border-radius: var(--cl-radius-xl);
                    color: var(--cl-bg);
                    font-weight: 500;
                    text-transform: uppercase;
                }

                .feature-card__description {
                    font-size: 0.95rem;
                    color: var(--cl-purple-light);
                    line-height: 1.6;
                    margin: 0 0 16px 0;
                    flex: 1;
                }

                .feature-card__tags {
                    display: flex;
                    gap: 8px;
                    flex-wrap: wrap;
                }

                .feature-card__tag {
                    font-size: 0.75rem;
                    padding: 4px 10px;
                    background: var(--cl-bg-inverse-soft-hover);
                    border-radius: var(--cl-radius-lg);
                    color: var(--cl-bg);
                }

                /* 無上升效果的變體 */
                .feature-card--no-elevation .feature-card__container:hover {
                    transform: none;
                }

                /* 適用於淺色背景的變體 */
                .feature-card--light .feature-card__container {
                    background: var(--cl-bg);
                    border-color: var(--cl-border-light);
                }

                .feature-card--light .feature-card__title {
                    color: var(--cl-text);
                }

                .feature-card--light .feature-card__description {
                    color: var(--cl-text-secondary);
                }

                .feature-card--light .feature-card__tag {
                    background: var(--cl-bg-subtle);
                    color: var(--cl-text-secondary);
                }
            `;
            document.head.appendChild(style);
        }

        // 應用修飾類
        if (!this.elevated) {
            this.element.classList.add('feature-card--no-elevation');
        }
    }

    /**
     * 綁定事件處理
     * @private
     */
    _bindEvents() {
        this.element.addEventListener('click', (e) => {
            e.preventDefault();
            
            if (this.onClick) {
                this.onClick(this.customData);
            } else if (this.url) {
                globalThis.location.href = this.url;
            }
        });

        // 鍵盤無障礙支援
        this.element.setAttribute('role', 'button');
        this.element.setAttribute('tabindex', '0');
        this.element.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                this.element.click();
            }
        });
    }

    /**
     * HTML 轉義，防止 XSS
     * @private
     */
    _escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    /**
     * 更新卡片標題
     * @param {string} title - 新標題
     */
    setTitle(title) {
        this.title = title;
        const titleEl = this.element.querySelector('.feature-card__title');
        if (titleEl) {
            const badgeHtml = this.badge ? `<span class="feature-card__badge" style="background: ${this.badgeColor}">${this._escapeHtml(this.badge)}</span>` : '';
            titleEl.innerHTML = `${this._escapeHtml(title)} ${badgeHtml}`;
        }
    }

    /**
     * 更新卡片描述
     * @param {string} description - 新描述
     */
    setDescription(description) {
        this.description = description;
        const descEl = this.element.querySelector('.feature-card__description');
        if (descEl) {
            descEl.textContent = description;
        }
    }

    /**
     * 更新標籤
     * @param {string[]} tags - 新標籤陣列
     */
    setTags(tags) {
        this.tags = tags;
        const tagsContainer = this.element.querySelector('.feature-card__tags');
        if (tagsContainer) {
            tagsContainer.innerHTML = tags.map(tag => 
                `<span class="feature-card__tag">${this._escapeHtml(tag)}</span>`
            ).join('');
        }
    }

    /**
     * 設置徽章
     * @param {string} badge - 徽章文字
     * @param {string} [color='var(--cl-brand-discord)'] - 徽章顏色
     */
    setBadge(badge, color = 'var(--cl-brand-discord)') {
        this.badge = badge;
        this.badgeColor = color;
        this.setTitle(this.title); // 重新渲染標題
    }

    /**
     * 切換淺色模式
     * @param {boolean} light - 是否為淺色模式
     */
    setLightMode(light) {
        if (light) {
            this.element.classList.add('feature-card--light');
        } else {
            this.element.classList.remove('feature-card--light');
        }
    }

    /**
     * 掛載到指定容器
     * @param {string|HTMLElement} target - 目標容器（選擇器或元素）
     */
    mount(target) {
        const container = typeof target === 'string' ? document.querySelector(target) : target;
        if (container) {
            container.appendChild(this.element);
        } else {
            console.error('FeatureCard: 找不到目標容器');
        }
    }

    /**
     * 移除卡片
     */
    destroy() {
        this.element?.remove();
    }
}
