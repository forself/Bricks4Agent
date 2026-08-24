/**
 * FeedCard - 動態貼文卡片元件
 *
 * Facebook 風格的動態貼文卡片，顯示作者、時間、類型、內容、
 * 圖片 Grid 和相關標籤。支援內容截斷展開和圖片預覽。
 * 樣式以元素層級 CSSOM（style.cssText / .style 指派）套用，
 * 不注入 <style>、不輸出 style="…" 屬性，相容嚴格 CSP（style-src 'self'）；
 * 僅 @media 響應式規則抽出至同目錄 FeedCard.css，以同源 <link> 載入。
 *
 * @author MAGI System
 * @version 1.0.0
 *
 * @example
 * const feed = new FeedCard({
 *     avatar: '/photos/user1.jpg',
 *     author: '張三',
 *     authorSub: '工程團隊',
 *     timestamp: '2026-02-15T14:30:00',
 *     type: '緊急事件',
 *     typeColor: 'var(--cl-danger)',
 *     title: '販毒案件',
 *     content: '於台北市中山區查獲販毒案件...',
 *     images: ['/img/evidence1.jpg', '/img/evidence2.jpg'],
 *     tags: ['販毒', '台北市'],
 *     relatedCount: 3,
 *     onClickDetail: () => navigate('/activity/123'),
 *     onClickAuthor: () => navigate('/profile/member/456')
 * });
 * feed.mount('.feed-container');
 */

import { escapeHtml } from '../../utils/security.js';
import { Avatar } from '../Avatar/index.js';
import Locale from '../../i18n/index.js';

export class FeedCard {
    /** 類型預設色彩 */
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
     * @param {string} options.avatar - 作者頭像 URL
     * @param {string} options.author - 作者名稱
     * @param {string} options.authorSub - 作者副標題（如所屬組織）
     * @param {string} options.timestamp - 時間（ISO 字串）
     * @param {string} options.type - 類型標籤
     * @param {string} options.typeColor - 類型色彩（可選，依 type 自動選）
     * @param {string} options.title - 標題
     * @param {string} options.content - 內容
     * @param {string[]} options.images - 圖片 URL 陣列
     * @param {string[]} options.tags - 標籤陣列
     * @param {number} options.relatedCount - 關聯人數
     * @param {Function|null} options.onClickDetail - 點擊查看詳情
     * @param {Function|null} options.onClickAuthor - 點擊作者名稱
     */
    constructor(options = {}) {
        this.options = {
            avatar: '',
            author: '',
            authorSub: '',
            timestamp: '',
            type: '',
            typeColor: '',
            title: '',
            content: '',
            images: [],
            tags: [],
            relatedCount: 0,
            onClickDetail: null,
            onClickAuthor: null,
            ...options
        };

        this.element = null;
        this._expanded = false;
        FeedCard._ensureStylesheet();
    }

    /**
     * 載入同目錄 FeedCard.css（僅 @media 響應式規則與純字串 API 的 wrapper 樣式）。
     * CSP 相容：同源 <link> 樣式表允許；以 id 去重，僅注入一次。
     */
    static _ensureStylesheet() {
        if (typeof document === 'undefined') return;
        if (document.getElementById('social-feed-card-css')) return;

        const link = document.createElement('link');
        link.id = 'social-feed-card-css';
        link.rel = 'stylesheet';
        link.href = new URL('./FeedCard.css', import.meta.url).href;
        document.head.appendChild(link);
    }

    /**
     * 對 scope 內（含 scope 本身）所有貼文卡片套用元素層級樣式。
     * CSP 相容：取代原本的 <style> 注入；listHTML() 產出的字串嵌入 DOM 後
     * 也可呼叫此方法完成樣式化。
     * @param {HTMLElement} scope
     */
    static applyStyles(scope) {
        if (!scope || !scope.querySelectorAll) return;
        const cards = [];
        if (scope.classList && scope.classList.contains('social-feed-card')) cards.push(scope);
        cards.push(...scope.querySelectorAll('.social-feed-card'));
        cards.forEach((card) => FeedCard._styleCard(card));

        // 內嵌的頭像一併樣式化
        Avatar.applyStyles(scope);
    }

    /** 套用單一貼文卡片的樣式與 hover 互動（以 data-fc-styled 去重） */
    static _styleCard(card) {
        if (card.dataset.fcStyled === '1') return;
        card.dataset.fcStyled = '1';

        card.style.cssText = `
            background: var(--cl-bg);
            border-radius: var(--cl-radius-xl);
            box-shadow: var(--cl-shadow-sm);
            overflow: hidden;
            transition: box-shadow var(--cl-transition);
        `;
        card.addEventListener('mouseenter', () => {
            card.style.boxShadow = 'var(--cl-shadow-md)';
        });
        card.addEventListener('mouseleave', () => {
            card.style.boxShadow = 'var(--cl-shadow-sm)';
        });

        /* 頭部：作者資訊 */
        const header = card.querySelector('.social-feed-card__header');
        if (header) {
            header.style.cssText = `
                display: flex;
                align-items: center;
                gap: 12px;
                padding: 16px 20px 12px;
            `;
        }

        const authorInfo = card.querySelector('.social-feed-card__author-info');
        if (authorInfo) {
            authorInfo.style.cssText = `
                flex: 1;
                min-width: 0;
            `;
        }

        const author = card.querySelector('.social-feed-card__author');
        if (author) {
            author.style.cssText = `
                font-size: var(--cl-font-size-lg);
                font-weight: 600;
                color: var(--cl-text);
                cursor: pointer;
            `;
            author.addEventListener('mouseenter', () => {
                author.style.color = 'var(--cl-brand-linkedin)';
                author.style.textDecoration = 'underline';
            });
            author.addEventListener('mouseleave', () => {
                author.style.color = 'var(--cl-text)';
                author.style.textDecoration = 'none';
            });
        }

        const meta = card.querySelector('.social-feed-card__meta');
        if (meta) {
            meta.style.cssText = `
                font-size: var(--cl-font-size-sm);
                color: var(--cl-text-dim);
                display: flex;
                align-items: center;
                gap: 6px;
                margin-top: 2px;
            `;
        }

        const typeBadge = card.querySelector('.social-feed-card__type-badge');
        if (typeBadge) {
            typeBadge.style.cssText = `
                font-size: var(--cl-font-size-xs);
                padding: 1px 8px;
                border-radius: var(--cl-radius-lg);
                color: var(--cl-text-inverse);
                font-weight: 500;
            `;
            // 類型色彩由 data-* 帶入，以屬性指派（值可為任意字串，不插入 cssText）
            if (typeBadge.dataset.typeColor) {
                typeBadge.style.background = typeBadge.dataset.typeColor;
            }
        }

        /* 內容區 */
        const body = card.querySelector('.social-feed-card__body');
        if (body) {
            body.style.cssText = `
                padding: 0 20px;
            `;
        }

        const title = card.querySelector('.social-feed-card__title');
        if (title) {
            title.style.cssText = `
                font-size: var(--cl-font-size-xl);
                font-weight: 600;
                color: var(--cl-text);
                margin-bottom: 6px;
            `;
        }

        const content = card.querySelector('.social-feed-card__content');
        if (content) {
            const truncated = content.classList.contains('social-feed-card__content--truncated');
            content.style.cssText = `
                font-size: var(--cl-font-size-lg);
                color: var(--cl-text);
                line-height: 1.6;
                ${truncated ? `
                display: -webkit-box;
                -webkit-line-clamp: 3;
                -webkit-box-orient: vertical;
                overflow: hidden;
                ` : ''}
            `;
        }

        const expand = card.querySelector('.social-feed-card__expand');
        if (expand) {
            expand.style.cssText = `
                font-size: var(--cl-font-size-md);
                color: var(--cl-brand-linkedin);
                cursor: pointer;
                font-weight: 500;
                margin-top: 4px;
                display: inline-block;
            `;
            expand.addEventListener('mouseenter', () => {
                expand.style.textDecoration = 'underline';
            });
            expand.addEventListener('mouseleave', () => {
                expand.style.textDecoration = 'none';
            });
        }

        /* 圖片 Grid */
        const imagesWrap = card.querySelector('.social-feed-card__images');
        if (imagesWrap) {
            const isOne = imagesWrap.classList.contains('social-feed-card__images--1');
            const isTwo = imagesWrap.classList.contains('social-feed-card__images--2');
            imagesWrap.style.cssText = `
                margin-top: 12px;
                display: grid;
                gap: 2px;
                border-radius: var(--cl-radius-lg);
                overflow: hidden;
                grid-template-columns: ${isOne ? '1fr' : '1fr 1fr'};
                ${(!isOne && !isTwo) ? 'grid-template-rows: auto auto;' : ''}
            `;

            imagesWrap.querySelectorAll('.social-feed-card__image').forEach((imgEl) => {
                imgEl.style.cssText = `
                    width: 100%;
                    height: ${isOne ? '300px' : '200px'};
                    object-fit: cover;
                    cursor: pointer;
                    transition: opacity var(--cl-transition);
                `;
                imgEl.addEventListener('mouseenter', () => {
                    imgEl.style.opacity = '0.9';
                });
                imgEl.addEventListener('mouseleave', () => {
                    imgEl.style.opacity = '1';
                });
            });

            imagesWrap.querySelectorAll('.social-feed-card__image-more').forEach((more) => {
                more.style.cssText = `
                    position: relative;
                    display: flex;
                    align-items: center;
                    justify-content: center;
                `;
            });

            imagesWrap.querySelectorAll('.social-feed-card__image-more-overlay').forEach((overlay) => {
                overlay.style.cssText = `
                    position: absolute;
                    inset: 0;
                    background: var(--cl-bg-overlay);
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    color: var(--cl-text-inverse);
                    font-size: var(--cl-font-size-3xl);
                    font-weight: 700;
                `;
            });
        }

        /* 底部 */
        const footer = card.querySelector('.social-feed-card__footer');
        if (footer) {
            footer.style.cssText = `
                display: flex;
                align-items: center;
                justify-content: space-between;
                padding: 12px 20px;
                border-top: 1px solid var(--cl-border-light);
                margin-top: 12px;
            `;
        }

        const tagsWrap = card.querySelector('.social-feed-card__tags');
        if (tagsWrap) {
            tagsWrap.style.cssText = `
                display: flex;
                flex-wrap: wrap;
                gap: 4px;
                flex: 1;
            `;
        }

        card.querySelectorAll('.social-feed-card__tag').forEach((tag) => {
            tag.style.cssText = `
                font-size: var(--cl-font-size-xs);
                padding: 2px 8px;
                border-radius: var(--cl-radius-lg);
                background: var(--cl-bg-hover);
                color: var(--cl-text-heading);
            `;
        });

        const related = card.querySelector('.social-feed-card__related');
        if (related) {
            related.style.cssText = `
                font-size: var(--cl-font-size-sm);
                color: var(--cl-text-dim);
                white-space: nowrap;
                margin-left: 12px;
            `;
        }

        const detailBtn = card.querySelector('.social-feed-card__detail-btn');
        if (detailBtn) {
            detailBtn.style.cssText = `
                font-size: var(--cl-font-size-md);
                color: var(--cl-brand-linkedin);
                cursor: pointer;
                font-weight: 500;
                white-space: nowrap;
                margin-left: 12px;
            `;
            detailBtn.addEventListener('mouseenter', () => {
                detailBtn.style.textDecoration = 'underline';
            });
            detailBtn.addEventListener('mouseleave', () => {
                detailBtn.style.textDecoration = 'none';
            });
        }
    }

    /** 格式化時間為相對時間 */
    _formatTime(timestamp) {
        if (!timestamp) return '';
        try {
            const d = new Date(timestamp);
            const now = new Date();
            const diff = now - d;
            const mins = Math.floor(diff / 60000);
            const hours = Math.floor(diff / 3600000);
            const days = Math.floor(diff / 86400000);

            if (mins < 1) return Locale.t('feedCard.justNow');
            if (mins < 60) return Locale.t('feedCard.minutesAgo', { n: mins });
            if (hours < 24) return Locale.t('feedCard.hoursAgo', { n: hours });
            if (days < 7) return Locale.t('feedCard.daysAgo', { n: days });

            const y = d.getFullYear();
            const m = String(d.getMonth() + 1).padStart(2, '0');
            const day = String(d.getDate()).padStart(2, '0');
            return `${y}-${m}-${day}`;
        } catch {
            return timestamp;
        }
    }

    /** 取得類型顏色 */
    _getTypeColor() {
        return this.options.typeColor
            || FeedCard.TYPE_COLORS[this.options.type]
            || FeedCard.TYPE_COLORS['其他'];
    }

    /** 產生圖片 Grid HTML */
    _renderImages() {
        const { images } = this.options;
        if (!images || images.length === 0) return '';

        const count = images.length;
        let gridClass = 'social-feed-card__images';

        if (count === 1) gridClass += ' social-feed-card__images--1';
        else if (count === 2) gridClass += ' social-feed-card__images--2';
        else gridClass += ' social-feed-card__images--3plus';

        const maxShow = Math.min(count, 4);
        let imgsHTML = '';

        for (let i = 0; i < maxShow; i++) {
            const safeSrc = escapeHtml(images[i]);
            if (i === 3 && count > 4) {
                // 第四張帶「+N」遮罩
                imgsHTML += `<div class="social-feed-card__image-more">
                    <img class="social-feed-card__image" src="${safeSrc}" alt="圖片${i + 1}">
                    <div class="social-feed-card__image-more-overlay">+${count - 3}</div>
                </div>`;
            } else {
                imgsHTML += `<img class="social-feed-card__image" src="${safeSrc}" alt="圖片${i + 1}" data-img-index="${i}">`;
            }
        }

        return `<div class="${gridClass}">${imgsHTML}</div>`;
    }

    /**
     * 產生 HTML 字串
     * @returns {string}
     */
    toHTML() {
        const { avatar, author, authorSub, timestamp, type, title, content,
                tags, relatedCount, onClickDetail } = this.options;

        const typeColor = this._getTypeColor();
        const avatarInstance = new Avatar({ src: avatar, alt: author, size: 'md' });
        const timeStr = this._formatTime(timestamp);
        const needTruncate = content && content.length > 150;
        const truncateClass = (needTruncate && !this._expanded)
            ? ' social-feed-card__content--truncated' : '';

        // 標籤
        const tagsHTML = (tags || []).slice(0, 5).map(tag =>
            `<span class="social-feed-card__tag">${escapeHtml(tag)}</span>`
        ).join('');

        return `<div class="social-feed-card social-animate-in">
            <div class="social-feed-card__header">
                ${avatarInstance.toHTML()}
                <div class="social-feed-card__author-info">
                    <span class="social-feed-card__author" data-feed-action="author">${escapeHtml(author)}</span>
                    <div class="social-feed-card__meta">
                        ${authorSub ? `<span>${escapeHtml(authorSub)}</span> ·` : ''}
                        <span>${escapeHtml(timeStr)}</span>
                        ${type ? ` · <span class="social-feed-card__type-badge" data-type-color="${escapeHtml(typeColor)}">${escapeHtml(type)}</span>` : ''}
                    </div>
                </div>
            </div>
            <div class="social-feed-card__body">
                ${title ? `<div class="social-feed-card__title">${escapeHtml(title)}</div>` : ''}
                ${content ? `<div class="social-feed-card__content${truncateClass}">${escapeHtml(content)}</div>` : ''}
                ${needTruncate && !this._expanded ? `<span class="social-feed-card__expand" data-feed-action="expand">...查看更多</span>` : ''}
                ${this._renderImages()}
            </div>
            <div class="social-feed-card__footer">
                <div class="social-feed-card__tags">${tagsHTML}</div>
                ${relatedCount > 0 ? `<span class="social-feed-card__related">👥 ${relatedCount} 位關聯人</span>` : ''}
                ${onClickDetail ? `<span class="social-feed-card__detail-btn" data-feed-action="detail">查看詳情 →</span>` : ''}
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
        this.element = target.querySelector('.social-feed-card');

        // CSP 相容：元素層級 CSSOM 樣式（含 hover 與內嵌頭像）
        FeedCard.applyStyles(target);

        this._bindEvents();
    }

    _bindEvents() {
        if (!this.element) return;

        this.element.addEventListener('click', (e) => {
            const action = e.target.closest('[data-feed-action]');
            if (!action) return;

            const actionType = action.dataset.feedAction;

            switch (actionType) {
                case 'author':
                    this.options.onClickAuthor?.();
                    break;
                case 'detail':
                    this.options.onClickDetail?.();
                    break;
                case 'expand':
                    this._expanded = true;
                    if (this.element.parentNode) {
                        this.mount(this.element.parentNode);
                    }
                    break;
            }
        });
    }

    /**
     * 批次產生多張貼文的 HTML
     * 嵌入 DOM 後請呼叫 FeedCard.applyStyles(container) 套樣式。
     * @param {Object[]} items - 貼文資料陣列
     * @returns {string}
     */
    static listHTML(items) {
        return items.map(item => {
            const card = new FeedCard(item);
            return `<div class="social-feed-card-wrapper">${card.toHTML()}</div>`;
        }).join('');
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

export default FeedCard;
