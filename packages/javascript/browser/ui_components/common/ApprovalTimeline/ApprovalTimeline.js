/**
 * ApprovalTimeline — 審核歷程（原子元件）
 *
 * 以垂直時間軸呈現一連串審核步驟（採購申請、請假單、用印申請等通用簽核流程）。
 *
 * - 語意 <ol>；狀態為 current 的步驟帶 aria-current="step"。
 * - 狀態一律有 Locale 文字標籤，圖示為文字符號（aria-hidden），顏色只是輔助。
 * - 時間預設以 Intl.DateTimeFormat(Locale 語言, { dateStyle: 'medium', timeStyle: 'short' }) 格式化，
 *   輸出 <time datetime="ISO">；無法解析的時間字串原樣顯示。
 * - 附件連結一律經 sanitizeUrl()；不安全的網址（例如 javascript:）不產生連結，只顯示檔名。
 *   openLinksInNewTab: true 時才加 target="_blank" 與 rel="noopener noreferrer"。
 * - 只有提供 onStepClick 時步驟標題才成為 <button>（原生 Enter／Space）。
 * - CSP：createElement + textContent，樣式走 CSSOM。
 *
 * @example
 * new ApprovalTimeline({
 *     steps: [
 *         { id: 1, title: '申請人送出', actor: '王小明', time: '2026-09-01T09:00:00+08:00', status: 'approved' },
 *         { id: 2, title: '部門主管審核', actor: '李主任', status: 'current' },
 *         { id: 3, title: '總務核銷', status: 'pending' }
 *     ]
 * }).mount('#history');
 */
import Locale from '../../i18n/index.js';
import { sanitizeUrl } from '../../utils/security.js';
import './locale.js';

const STATUSES = Object.freeze(['pending', 'current', 'approved', 'rejected', 'returned', 'skipped', 'cancelled']);

// 文字符號（非 SVG）；U+FE0E 要求文字呈現，才會套用 CSS 顏色
const STATUS_GLYPH = {
    pending: '○',
    current: '●',
    approved: '✓',
    rejected: '✕',
    returned: '↩︎',
    skipped: '»',
    cancelled: '⊘'
};

const STATUS_TONE = {
    pending: { color: 'var(--cl-text-muted)', soft: 'var(--cl-bg-secondary)', filled: false },
    current: { color: 'var(--cl-primary)', soft: 'var(--cl-primary-light)', filled: true },
    approved: { color: 'var(--cl-success)', soft: 'var(--cl-success-light)', filled: true },
    rejected: { color: 'var(--cl-danger)', soft: 'var(--cl-danger-light)', filled: true },
    returned: { color: 'var(--cl-warning-dark)', soft: 'var(--cl-warning-light)', filled: true },
    skipped: { color: 'var(--cl-text-muted)', soft: 'var(--cl-bg-secondary)', filled: false },
    cancelled: { color: 'var(--cl-text-muted)', soft: 'var(--cl-bg-secondary)', filled: false }
};

const SR_ONLY_CSS = 'position:absolute;width:1px;height:1px;margin:-1px;padding:0;border:0;overflow:hidden;clip:rect(0 0 0 0);clip-path:inset(50%);white-space:nowrap;';
const ROOT_CSS = 'position:relative;min-width:0;font-family:var(--cl-font-family);color:var(--cl-text);';
const LIST_CSS = 'list-style:none;margin:0;padding:0;';
const STEP_CSS = 'position:relative;display:flex;align-items:stretch;gap:12px;margin:0;padding:0;';
const RAIL_CSS = 'flex:0 0 auto;display:flex;flex-direction:column;align-items:center;';
const LINE_CSS = 'flex:1 1 auto;width:2px;min-height:8px;margin-top:4px;background:var(--cl-border);';
const BODY_CSS = 'flex:1 1 auto;min-width:0;';
const HEAD_CSS = 'display:flex;flex-wrap:wrap;align-items:center;gap:6px 8px;';
const TITLE_CSS = 'font-weight:600;color:var(--cl-text);overflow-wrap:anywhere;';
const TITLE_BUTTON_CSS = 'padding:0;margin:0;border:0;background:transparent;font:inherit;font-weight:600;color:var(--cl-primary);text-align:left;text-decoration:underline;cursor:pointer;overflow-wrap:anywhere;';
const STATUS_CSS = 'display:inline-block;padding:0 8px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-pill);font-size:var(--cl-font-size-xs);line-height:1.8;color:var(--cl-text);white-space:nowrap;';
const META_CSS = 'display:flex;flex-wrap:wrap;gap:2px 12px;margin-top:2px;font-size:var(--cl-font-size-sm);color:var(--cl-text-secondary);';
const COMMENT_CSS = 'margin:6px 0 0;border-radius:var(--cl-radius-md);background:var(--cl-bg-secondary);font-size:var(--cl-font-size-sm);color:var(--cl-text);white-space:pre-line;overflow-wrap:anywhere;';
const ATTACHMENTS_CSS = 'list-style:none;margin:6px 0 0;padding:0;display:flex;flex-wrap:wrap;gap:4px 12px;';
const LINK_CSS = 'color:var(--cl-primary);font-size:var(--cl-font-size-sm);text-decoration:underline;overflow-wrap:anywhere;';
const DISABLED_LINK_CSS = 'color:var(--cl-text-muted);font-size:var(--cl-font-size-sm);overflow-wrap:anywhere;';
const EMPTY_CSS = 'margin:0;padding:12px;text-align:center;color:var(--cl-text-muted);font-size:var(--cl-font-size-md);';

const formatterCache = new Map();

function defaultFormatter(lang) {
    if (!formatterCache.has(lang)) {
        const style = { dateStyle: 'medium', timeStyle: 'short' };
        let formatter;
        try {
            formatter = new Intl.DateTimeFormat(lang, style);
        } catch {
            // 自訂註冊的語言代碼可能不是合法 BCP 47，退回執行環境預設
            formatter = new Intl.DateTimeFormat(undefined, style);
        }
        formatterCache.set(lang, formatter);
    }
    return formatterCache.get(lang);
}

function toDate(value) {
    if (value instanceof Date) return Number.isNaN(value.getTime()) ? null : value;
    if (typeof value === 'number' || (typeof value === 'string' && value.trim() !== '')) {
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? null : date;
    }
    return null;
}

function normalizeStatus(value) {
    return STATUSES.includes(value) ? value : 'pending';
}

function textOf(value) {
    return value === null || value === undefined ? '' : String(value);
}

export class ApprovalTimeline {
    static STATUSES = STATUSES;

    constructor(options = {}) {
        this.options = {
            steps: [],                 // 步驟：[{ id, title, actor?, time?, status, comment?, attachments?: [{ name, href }] }]
            compact: false,            // 緊湊模式：較小的標記與間距
            showTimestamps: true,      // 顯示時間
            formatTime: null,          // (date: Date, step) => string；null 使用 Intl.DateTimeFormat（Locale 語言）
            reverse: false,            // true：最新的步驟在最上面
            openLinksInNewTab: false,  // 附件連結在新分頁開啟（加 target="_blank" 與 rel="noopener noreferrer"）
            emptyText: null,           // 沒有步驟時的文字；null 使用 Locale approvalTimeline.empty
            ariaLabel: null,           // 清單的無障礙名稱；null 使用 Locale approvalTimeline.label
            onStepClick: null,         // (step) => void；提供時步驟標題成為按鈕
            ...options
        };

        this.element = null;
        this._destroyed = false;
        this._localeListening = false;
        this._steps = Array.isArray(this.options.steps) ? this.options.steps.slice() : [];

        this._rootClickHandler = (event) => this._handleClick(event);
        this._onLocaleChange = () => this._render();

        this.element = document.createElement('div');
        this.element.className = 'cl-approval-timeline';
        this.element.style.cssText = ROOT_CSS;
        this.element.addEventListener('click', this._rootClickHandler);
        this._render();
    }

    // ── 渲染 ──────────────────────────────────────────────

    _render() {
        if (this._destroyed || !this.element) return;
        const focusKey = this._captureFocusKey();
        const compact = !!this.options.compact;
        this.element.dataset.compact = String(compact);

        if (this._steps.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'cl-approval-timeline__empty';
            empty.style.cssText = EMPTY_CSS;
            empty.textContent = this.options.emptyText ?? Locale.t('approvalTimeline.empty');
            this.element.replaceChildren(empty);
            return;
        }

        const list = document.createElement('ol');
        list.className = 'cl-approval-timeline__list';
        list.style.cssText = LIST_CSS;
        list.setAttribute('aria-label', this.options.ariaLabel || Locale.t('approvalTimeline.label'));
        if (this.options.reverse) list.setAttribute('reversed', '');

        const indexes = this._steps.map((_, index) => index);
        if (this.options.reverse) indexes.reverse();
        const formatter = this._resolveFormatter();
        indexes.forEach((stepIndex, position) => {
            const step = this._steps[stepIndex];
            if (!step || typeof step !== 'object') return;
            list.appendChild(this._createStep(step, stepIndex, position === indexes.length - 1, compact, formatter));
        });

        this.element.replaceChildren(list);
        this._restoreFocusKey(focusKey);
    }

    _resolveFormatter() {
        if (typeof this.options.formatTime === 'function') return this.options.formatTime;
        const formatter = defaultFormatter(Locale.getLang());
        return (date) => formatter.format(date);
    }

    _createStep(step, stepIndex, isLast, compact, formatTime) {
        const status = normalizeStatus(step.status);
        const tone = STATUS_TONE[status];

        const item = document.createElement('li');
        item.className = `cl-approval-timeline__step cl-approval-timeline__step--${status}`;
        item.dataset.status = status;
        if (step.id !== null && step.id !== undefined) item.dataset.stepId = String(step.id);
        if (status === 'current') item.setAttribute('aria-current', 'step');
        item.style.cssText = STEP_CSS;

        // 軌道：狀態符號 + 連接線（純裝飾）
        const rail = document.createElement('span');
        rail.className = 'cl-approval-timeline__rail';
        rail.setAttribute('aria-hidden', 'true');
        rail.style.cssText = RAIL_CSS;
        const marker = document.createElement('span');
        marker.className = 'cl-approval-timeline__marker';
        const size = compact ? 18 : 24;
        marker.style.cssText = 'display:flex;align-items:center;justify-content:center;box-sizing:border-box;'
            + `width:${size}px;height:${size}px;border-radius:var(--cl-radius-round);border:2px solid ${tone.color};`
            + `font-size:${compact ? 'var(--cl-font-size-2xs)' : 'var(--cl-font-size-sm)'};font-weight:700;line-height:1;`
            + (tone.filled
                ? `background:${tone.color};color:var(--cl-text-inverse);`
                : `background:var(--cl-bg);color:${tone.color};`);
        marker.textContent = STATUS_GLYPH[status];
        rail.appendChild(marker);
        if (!isLast) {
            const line = document.createElement('span');
            line.className = 'cl-approval-timeline__line';
            line.style.cssText = LINE_CSS;
            rail.appendChild(line);
        }
        item.appendChild(rail);

        const body = document.createElement('div');
        body.className = 'cl-approval-timeline__body';
        body.style.cssText = BODY_CSS;
        body.style.paddingBottom = isLast ? '0' : (compact ? '8px' : '16px');
        body.style.paddingTop = compact ? '0' : '2px';

        const head = document.createElement('div');
        head.className = 'cl-approval-timeline__head';
        head.style.cssText = HEAD_CSS;
        head.style.fontSize = compact ? 'var(--cl-font-size-md)' : 'var(--cl-font-size-lg)';
        const titleText = textOf(step.title);
        let title;
        if (typeof this.options.onStepClick === 'function') {
            title = document.createElement('button');
            title.type = 'button';
            title.className = 'cl-approval-timeline__title cl-approval-timeline__title-button';
            title.dataset.stepIndex = String(stepIndex);
            title.dataset.focusKey = `title-${stepIndex}`;
            title.style.cssText = TITLE_BUTTON_CSS;
        } else {
            title = document.createElement('span');
            title.className = 'cl-approval-timeline__title';
            title.style.cssText = TITLE_CSS;
        }
        title.textContent = titleText;
        if (status === 'cancelled') title.style.textDecoration = 'line-through';
        const statusLabel = document.createElement('span');
        statusLabel.className = 'cl-approval-timeline__status';
        statusLabel.style.cssText = STATUS_CSS;
        statusLabel.style.borderColor = tone.color;
        statusLabel.style.background = tone.soft;
        statusLabel.textContent = Locale.t(`approvalTimeline.status.${status}`);
        head.append(title, statusLabel);
        body.appendChild(head);

        const actorText = textOf(step.actor);
        const timeElement = this.options.showTimestamps ? this._createTime(step, formatTime) : null;
        if (actorText || timeElement) {
            const meta = document.createElement('div');
            meta.className = 'cl-approval-timeline__meta';
            meta.style.cssText = META_CSS;
            if (actorText) {
                const actor = document.createElement('span');
                actor.className = 'cl-approval-timeline__actor';
                actor.textContent = actorText;
                meta.appendChild(actor);
            }
            if (timeElement) meta.appendChild(timeElement);
            body.appendChild(meta);
        }

        const commentText = textOf(step.comment);
        if (commentText) {
            const comment = document.createElement('p');
            comment.className = 'cl-approval-timeline__comment';
            comment.style.cssText = COMMENT_CSS;
            comment.style.padding = compact ? '4px 8px' : '6px 10px';
            comment.textContent = commentText;
            body.appendChild(comment);
        }

        const attachments = Array.isArray(step.attachments) ? step.attachments.filter((item) => item && typeof item === 'object') : [];
        if (attachments.length > 0) body.appendChild(this._createAttachments(attachments, stepIndex));

        item.appendChild(body);
        return item;
    }

    _createTime(step, formatTime) {
        if (step.time === null || step.time === undefined || step.time === '') return null;
        const date = toDate(step.time);
        const time = document.createElement('time');
        time.className = 'cl-approval-timeline__time';
        if (!date) {
            // 無法解析：原樣顯示，不輸出 datetime
            time.textContent = String(step.time);
            return time;
        }
        time.setAttribute('datetime', date.toISOString());
        time.textContent = textOf(formatTime(date, step));
        return time;
    }

    _createAttachments(attachments, stepIndex) {
        const list = document.createElement('ul');
        list.className = 'cl-approval-timeline__attachments';
        list.setAttribute('aria-label', Locale.t('approvalTimeline.attachments'));
        list.style.cssText = ATTACHMENTS_CSS;
        const newTab = !!this.options.openLinksInNewTab;

        attachments.forEach((attachment, index) => {
            const entry = document.createElement('li');
            const name = textOf(attachment.name) || textOf(attachment.href);
            const href = sanitizeUrl(attachment.href);
            if (href) {
                const link = document.createElement('a');
                link.className = 'cl-approval-timeline__attachment';
                link.href = href;
                link.dataset.focusKey = `attachment-${stepIndex}-${index}`;
                link.style.cssText = LINK_CSS;
                link.textContent = name;
                if (newTab) {
                    link.target = '_blank';
                    link.rel = 'noopener noreferrer';
                    const hint = document.createElement('span');
                    hint.className = 'cl-approval-timeline__new-tab';
                    hint.style.cssText = SR_ONLY_CSS;
                    hint.textContent = Locale.t('approvalTimeline.newTab');
                    link.appendChild(hint);
                }
                entry.appendChild(link);
            } else {
                // 不安全或空白的網址：只顯示檔名，不產生可點的連結
                const text = document.createElement('span');
                text.className = 'cl-approval-timeline__attachment cl-approval-timeline__attachment--disabled';
                text.style.cssText = DISABLED_LINK_CSS;
                text.textContent = name;
                entry.appendChild(text);
            }
            list.appendChild(entry);
        });
        return list;
    }

    _captureFocusKey() {
        const active = this.element?.ownerDocument?.activeElement;
        if (!active || !this.element.contains(active)) return null;
        return active.dataset?.focusKey || null;
    }

    _restoreFocusKey(key) {
        if (!key) return;
        const target = [...this.element.querySelectorAll('[data-focus-key]')].find((el) => el.dataset.focusKey === key);
        target?.focus();
    }

    _handleClick(event) {
        if (typeof this.options.onStepClick !== 'function') return;
        const button = event.target && event.target.closest ? event.target.closest('.cl-approval-timeline__title-button') : null;
        if (!button || !this.element.contains(button)) return;
        const step = this._steps[Number(button.dataset.stepIndex)];
        if (step) this.options.onStepClick(step);
    }

    // ── 公開 API ─────────────────────────────────────────

    /** 以新陣列取代全部步驟並重繪 */
    setSteps(steps) {
        if (this._destroyed) return this;
        this._steps = Array.isArray(steps) ? steps.slice() : [];
        this._render();
        return this;
    }

    /** 目前的步驟（原始順序；回傳新陣列，元素為呼叫端原物件） */
    getSteps() {
        return this._steps.slice();
    }

    mount(container) {
        if (this._destroyed) return this;
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) {
            console.warn('[ApprovalTimeline] mount target not found:', container);
            return this;
        }
        target.appendChild(this.element);
        if (!this._localeListening && typeof window !== 'undefined') {
            window.addEventListener('locale-changed', this._onLocaleChange);
            this._localeListening = true;
        }
        return this;
    }

    destroy() {
        if (this._destroyed) return;
        this._destroyed = true;
        if (this._localeListening) {
            window.removeEventListener('locale-changed', this._onLocaleChange);
            this._localeListening = false;
        }
        this.element?.removeEventListener('click', this._rootClickHandler);
        this.element?.remove();
        this._steps = [];
    }
}

export default ApprovalTimeline;
