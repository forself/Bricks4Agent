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
        this._containerEl = null;
        this._light = false;
        this._hovered = false;

        this._init();
    }

    /**
     * 初始化卡片元素
     * @private
     */
    _init() {
        this.element = document.createElement('div');
        this.element.className = 'feature-card';
        
        // 建立卡片結構（不含 style 屬性；樣式一律走 CSSOM，CSP 相容）
        this.element.innerHTML = `
            <div class="feature-card__container">
                <div class="feature-card__header">
                    <h3 class="feature-card__title">
                        ${this._escapeHtml(this.title)}
                        ${this.badge ? `<span class="feature-card__badge">${this._escapeHtml(this.badge)}</span>` : ''}
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
     * 應用卡片樣式（元素層 CSSOM，CSP 相容）
     * @private
     */
    _applyStyles() {
        // 根元素
        this.element.style.cssText = 'display: block; text-decoration: none; color: inherit; height: 100%;';

        // 容器
        const container = this.element.querySelector('.feature-card__container');
        this._containerEl = container;
        if (container) {
            container.style.cssText = 'background: var(--cl-bg-inverse-soft); border: 1px solid var(--cl-divider-inverse); border-radius: var(--cl-radius-xl); padding: 24px; transition: transform var(--cl-transition-slow), box-shadow var(--cl-transition-slow), border-color var(--cl-transition-slow), background var(--cl-transition-slow); cursor: pointer; height: 100%; display: flex; flex-direction: column;';

            // :hover 效果（CSP 相容，改用事件）
            container.addEventListener('mouseenter', () => {
                this._hovered = true;
                this._applyContainerState();
            });
            container.addEventListener('mouseleave', () => {
                this._hovered = false;
                this._applyContainerState();
            });
        }

        const header = this.element.querySelector('.feature-card__header');
        if (header) {
            header.style.cssText = 'margin-bottom: 12px;';
        }

        const title = this.element.querySelector('.feature-card__title');
        if (title) {
            title.style.cssText = 'font-size: 1.3rem; font-weight: 600; color: var(--cl-bg); margin: 0; display: flex; align-items: center; gap: 10px; flex-wrap: wrap;';
        }

        const desc = this.element.querySelector('.feature-card__description');
        if (desc) {
            desc.style.cssText = 'font-size: 0.95rem; color: var(--cl-purple-light); line-height: 1.6; margin: 0 0 16px 0; flex: 1;';
        }

        const tags = this.element.querySelector('.feature-card__tags');
        if (tags) {
            tags.style.cssText = 'display: flex; gap: 8px; flex-wrap: wrap;';
        }

        const badgeEl = this.element.querySelector('.feature-card__badge');
        if (badgeEl) {
            this._styleBadge(badgeEl);
        }

        this.element.querySelectorAll('.feature-card__tag').forEach(tag => this._styleTag(tag));

        // 應用修飾類（保留 class 供外部辨識）
        if (!this.elevated) {
            this.element.classList.add('feature-card--no-elevation');
        }

        this._applyThemeStyles();
    }

    /**
     * 徽章樣式（取代 innerHTML 內的 style 屬性）
     * @private
     */
    _styleBadge(badgeEl) {
        badgeEl.style.cssText = `font-size: 0.7rem; padding: 2px 8px; border-radius: var(--cl-radius-xl); color: var(--cl-bg); font-weight: 500; text-transform: uppercase; background: ${this.badgeColor};`;
    }

    /**
     * 標籤樣式（依 light/dark 主題）
     * @private
     */
    _styleTag(tagEl) {
        const bg = this._light ? 'var(--cl-bg-subtle)' : 'var(--cl-bg-inverse-soft-hover)';
        const color = this._light ? 'var(--cl-text-secondary)' : 'var(--cl-bg)';
        tagEl.style.cssText = `font-size: 0.75rem; padding: 4px 10px; background: ${bg}; border-radius: var(--cl-radius-lg); color: ${color};`;
    }

    /**
     * 依主題（light/dark）套用文字與標籤顏色
     * @private
     */
    _applyThemeStyles() {
        const light = this._light;

        const title = this.element.querySelector('.feature-card__title');
        if (title) {
            title.style.color = light ? 'var(--cl-text)' : 'var(--cl-bg)';
        }

        const desc = this.element.querySelector('.feature-card__description');
        if (desc) {
            desc.style.color = light ? 'var(--cl-text-secondary)' : 'var(--cl-purple-light)';
        }

        this.element.querySelectorAll('.feature-card__tag').forEach(tag => this._styleTag(tag));

        this._applyContainerState();
    }

    /**
     * 依 hover / 主題 / elevated 狀態套用容器樣式
     * （取代 .feature-card__container:hover 等 CSS 規則）
     * @private
     */
    _applyContainerState() {
        const c = this._containerEl;
        if (!c) return;

        const light = this._light;

        if (this._hovered) {
            c.style.transform = this.elevated ? 'translateY(-8px)' : 'none';
            c.style.boxShadow = 'var(--cl-shadow-lg)';
            // 原 CSS 中 light 變體以較晚宣告覆蓋 hover 的背景/邊框色
            c.style.borderColor = light ? 'var(--cl-border-light)' : 'var(--cl-gradient-start)';
            c.style.background = light ? 'var(--cl-bg)' : 'var(--cl-bg-inverse-soft-hover)';
        } else {
            c.style.transform = 'none';
            c.style.boxShadow = 'none';
            c.style.borderColor = light ? 'var(--cl-border-light)' : 'var(--cl-divider-inverse)';
            c.style.background = light ? 'var(--cl-bg)' : 'var(--cl-bg-inverse-soft)';
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
            const badgeHtml = this.badge ? `<span class="feature-card__badge">${this._escapeHtml(this.badge)}</span>` : '';
            titleEl.innerHTML = `${this._escapeHtml(title)} ${badgeHtml}`;
            // CSP:徽章樣式以 CSSOM 指派(與初始渲染同一 helper)
            const badgeEl = titleEl.querySelector('.feature-card__badge');
            if (badgeEl) this._styleBadge(badgeEl);
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
