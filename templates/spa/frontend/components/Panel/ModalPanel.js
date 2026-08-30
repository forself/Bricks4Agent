/**
 * ModalPanel
 * 彈出對話框 - 帶遮罩、居中顯示、Alert 類型
 */

import { BasePanel } from './BasePanel.js';
import { PanelManager } from './PanelManager.js';

// opt-in 品牌：JSON.parse 永遠產不出 symbol 鍵，API 回傳的純資料因此無法偽造成 raw() 標記。
// 固定用 Symbol.for 與 ui_components/utils/security.js 共用同一把鍵，兩邊產出的標記可互換。
const RAW_HTML_BRAND = Symbol.for('bricks4agent.rawHtml');

/**
 * 標記字串為「已知安全的 HTML」，作為輸出標記的明確 opt-in。
 * 與 ui_components/utils/security.js 的 raw() 同語義，兩邊產出的標記可互換。
 * @param {string} html - 已知安全的 HTML 字串
 * @returns {Readonly<{__html: string}>}
 */
export function raw(html) {
    const html_ = String(html ?? '');
    return Object.freeze({ [RAW_HTML_BRAND]: html_, __html: html_ });
}

function isRawHtml(value) {
    if (value === null || typeof value !== 'object') return false;
    // 一律查自身屬性(不用 in)：in 會走原型鏈，原型污染即可讓任意物件冒充標記
    return Object.prototype.hasOwnProperty.call(value, RAW_HTML_BRAND)
        && typeof value[RAW_HTML_BRAND] === 'string';
}

// 授權依據只看品牌欄位；__html 保留給既有讀取端，但不再決定是否進 innerHTML
function rawHtmlOf(value) {
    return value[RAW_HTML_BRAND];
}

// 文字一律走 textContent(不解析 HTML)；要輸出標記須以 raw() 明確 opt-in
function applyText(el, value) {
    if (isRawHtml(value)) {
        el.innerHTML = rawHtmlOf(value);
    } else {
        el.textContent = String(value ?? '');
    }
}

// 同一次 Esc 只讓一個 Modal 關掉：堆疊順序可能因重新 open() 而與監聽器註冊順序不一致
const handledEscapes = new WeakSet();

const MODAL_MESSAGE_STYLE = 'margin: 0 0 20px; color: #333; font-size: 14px;';
const MODAL_PROMPT_MESSAGE_STYLE = 'margin: 0 0 12px; color: #333; font-size: 14px;';
const MODAL_ROW_STYLE = 'display: flex; justify-content: flex-end; gap: 10px;';
const MODAL_ROW_STYLE_SINGLE = 'display: flex; justify-content: flex-end;';
const MODAL_BTN_SECONDARY = 'padding: 8px 16px; border: 1px solid #ddd; background: white; border-radius: 6px; cursor: pointer; font-size: 14px;';
const MODAL_BTN_PRIMARY = 'padding: 8px 16px; border: none; background: #2196F3; color: white; border-radius: 6px; cursor: pointer; font-size: 14px;';
const MODAL_INPUT_STYLE = 'width: 100%; padding: 8px 12px; border: 1px solid #ddd; border-radius: 6px; margin-bottom: 20px; font-size: 14px; box-sizing: border-box; outline: none;';

export class ModalPanel extends BasePanel {
    constructor(options = {}) {
        super({
            modal: true,
            closable: true,
            autoClose: true,
            showHeader: true,
            // 預設 false，直接 new 的呼叫端仍可 close() 後再 open() 重複使用
            destroyOnClose: false,
            visibility: BasePanel.VISIBILITY.NONE,
            ...options,
            // 標題由 BasePanel 以 textContent 呈現，raw() 在此無法解析成標記；
            // 取其字面值輸出，避免顯示成 [object Object]
            ...(isRawHtml(options.title) ? { title: rawHtmlOf(options.title) } : null)
        });

        this._modalEntered = false;
        this._wrapWithBackdrop();
    }

    _wrapWithBackdrop() {
        // 建立遮罩
        this.backdrop = document.createElement('div');
        this.backdrop.className = 'modal-backdrop';
        this.backdrop.style.cssText = `
            position: fixed;
            top: 0;
            left: 0;
            width: 100vw;
            height: 100vh;
            background: rgba(0, 0, 0, 0.5);
            display: flex;
            align-items: center;
            justify-content: center;
            z-index: ${PanelManager.calculateZIndex(this)};
            opacity: 0;
            visibility: hidden;
            transition: all 0.3s ease;
        `;

        // 調整內部元素樣式
        this.element.style.cssText += `
            position: relative;
            max-width: 90vw;
            max-height: 90vh;
            overflow: auto;
            transform: scale(0.9);
            transition: transform 0.3s ease;
        `;

        this.backdrop.appendChild(this.element);

        // 點擊遮罩關閉
        if (this.options.autoClose) {
            this.backdrop.addEventListener('click', (e) => {
                if (e.target === this.backdrop) {
                    this.close();
                }
            });
        }

        // ESC 關閉
        this._handleKeydown = (e) => {
            if (e.key !== 'Escape') return;
            if (this.options.visibility !== BasePanel.VISIBILITY.VISIBLE) return;
            // 每個實例都掛了 document 監聽，沒有這道判斷一次 Esc 會關掉所有開著的 Modal
            const stack = PanelManager.modalStack;
            if (stack[stack.length - 1] !== this.id) return;
            if (handledEscapes.has(e)) return;
            handledEscapes.add(e);
            this.close();
        };
        document.addEventListener('keydown', this._handleKeydown);
    }

    _applyVisibility() {
        const { visibility } = this.options;

        if (!this.backdrop) {
            super._applyVisibility();
            return;
        }

        switch (visibility) {
            case BasePanel.VISIBILITY.VISIBLE:
                this.backdrop.style.display = 'flex';
                this.backdrop.style.visibility = 'visible';
                this.backdrop.style.opacity = '1';
                // Critical: Override BasePanel's display:none
                this.element.style.display = '';
                this.element.style.visibility = 'visible';
                this.element.style.transform = 'scale(1)';
                document.body.style.overflow = 'hidden';
                // Recalculate z-index on open to ensure it's on top
                this.backdrop.style.zIndex = PanelManager.calculateZIndex(this);
                break;
            case BasePanel.VISIBILITY.HIDDEN:
            case BasePanel.VISIBILITY.NONE:
                // Critical: Set display:none to prevent backdrop from blocking mouse events
                this.backdrop.style.display = 'none';
                this.backdrop.style.visibility = 'hidden';
                this.backdrop.style.opacity = '0';
                this.element.style.transform = 'scale(0.9)';
                document.body.style.overflow = '';
                break;
        }
    }

    /**
     * 開啟 Modal
     */
    open() {
        // 已銷毀的實例再開只會鎖住 body 卷軸並推入永遠清不掉的 modalStack id
        if (this._destroyed) return this;

        // 先註冊進入 Modal 狀態 (這會更新 Stack，影響 calculateZIndex 結果)
        PanelManager.enterModal(this);
        this._modalEntered = true;
        this.setVisibility(BasePanel.VISIBILITY.VISIBLE);
        return this;
    }

    /**
     * 關閉 Modal
     */
    close() {
        // 先離開 Modal 狀態
        PanelManager.exitModal(this);
        this._modalEntered = false;
        super.close();

        if (this.options.destroyOnClose && !this._destroyed) {
            // 延後到 microtask，讓 `modal.close(); onConfirm();` 的同步尾段先跑完
            queueMicrotask(() => {
                // 同步尾段可能已把它重新 open()（驗證失敗重開），這時不能銷毀
                if (this._destroyed || this._modalEntered) return;
                if (this.options.visibility === BasePanel.VISIBILITY.VISIBLE) return;
                this.destroy();
            });
        }
        return this;
    }

    /**
     * 掛載（掛到 body）
     */
    mount(container = document.body) {
        if (this._destroyed) return this;

        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target) target.appendChild(this.backdrop);
        return this;
    }

    /**
     * 銷毀
     */
    destroy() {
        if (this._destroyed) return;

        // 還開著就被 destroy 時仍須結算，否則呼叫端(BasePage.confirm)的 Promise 永遠 pending
        const wasOpen = this.options.visibility === BasePanel.VISIBILITY.VISIBLE;

        document.removeEventListener('keydown', this._handleKeydown);

        // close() 已退出過就不重複退出
        if (this._modalEntered) {
            PanelManager.exitModal(this);
            this._modalEntered = false;
        }

        super.destroy();

        // 巢狀情境(confirm 的 onConfirm 再開 prompt)下，延後的銷毀不能搶走
        // 還開著的 Modal 的 body 卷軸鎖；須等 super.destroy() 清掉自己殘留的
        // stack id 之後才判斷，否則自己的 id 會讓判斷永遠不成立。
        if (PanelManager.modalStack.length === 0) {
            document.body.style.overflow = '';
        }

        // 不可將 this.backdrop 置 null:mount()/_applyVisibility() 仍會讀取
        if (this.backdrop?.parentNode) {
            this.backdrop.remove();
        }

        if (wasOpen) {
            this.options.onClose?.(this);
        }
    }

    static confirm(options = {}) {
        const {
            title = '確認',
            message = '',
            confirmText = '確認',
            cancelText = '取消',
            onConfirm = () => { },
            onCancel = () => { },
            onClose = null,
            ...rest
        } = options;

        // ESC / 遮罩 / 標題列 X 也必須結算，否則呼叫端(BasePage.confirm)的
        // Promise 永遠不會 resolve；settled 保證每條路徑只結算一次
        let settled = false;
        const finishCancel = () => {
            if (settled) return;
            settled = true;
            onCancel();
            onClose?.();
        };

        const modal = new ModalPanel({
            title,
            closable: true,
            // 確認框需等使用者明確選擇，預設不讓遮罩點擊關閉(呼叫端可傳 autoClose: true 復原)
            autoClose: false,
            destroyOnClose: true,
            onClose: finishCancel,
            ...rest
        });

        const content = document.createElement('div');

        const msgEl = document.createElement('p');
        msgEl.style.cssText = MODAL_MESSAGE_STYLE;
        applyText(msgEl, message);

        const btnRow = document.createElement('div');
        btnRow.style.cssText = MODAL_ROW_STYLE;

        const cancelBtn = document.createElement('button');
        cancelBtn.type = 'button';
        cancelBtn.className = 'modal-cancel';
        cancelBtn.style.cssText = MODAL_BTN_SECONDARY;
        applyText(cancelBtn, cancelText);

        const confirmBtn = document.createElement('button');
        confirmBtn.type = 'button';
        confirmBtn.className = 'modal-confirm';
        confirmBtn.style.cssText = MODAL_BTN_PRIMARY;
        applyText(confirmBtn, confirmText);

        btnRow.append(cancelBtn, confirmBtn);
        content.append(msgEl, btnRow);

        cancelBtn.addEventListener('click', () => {
            // close() 會觸發 onClose → finishCancel
            modal.close();
        });

        confirmBtn.addEventListener('click', () => {
            if (settled) return;
            settled = true;
            modal.close();
            onConfirm();
        });

        modal.setContent(content);
        modal.mount();
        modal.open();

        return modal;
    }

    /**
     * 快速建立提示對話框
     */
    static alert(options = {}) {
        const {
            title = '提示',
            message = '',
            confirmText = '確定',
            onConfirm = () => { },
            onClose = null,
            ...rest
        } = options;

        // ESC / 遮罩 / 標題列 X 也必須結算，否則呼叫端(BasePage.alert)的
        // Promise 永遠不會 resolve；settled 保證每條路徑只結算一次
        let settled = false;
        const finish = () => {
            if (settled) return;
            settled = true;
            onConfirm();
            onClose?.();
        };

        const modal = new ModalPanel({
            title,
            closable: true,
            destroyOnClose: true,
            onClose: finish,
            ...rest
        });

        const content = document.createElement('div');

        const msgEl = document.createElement('p');
        msgEl.style.cssText = MODAL_MESSAGE_STYLE;
        applyText(msgEl, message);

        const btnRow = document.createElement('div');
        btnRow.style.cssText = MODAL_ROW_STYLE_SINGLE;

        const confirmBtn = document.createElement('button');
        confirmBtn.type = 'button';
        confirmBtn.className = 'modal-confirm';
        confirmBtn.style.cssText = MODAL_BTN_PRIMARY;
        applyText(confirmBtn, confirmText);

        btnRow.append(confirmBtn);
        content.append(msgEl, btnRow);

        confirmBtn.addEventListener('click', () => {
            // close() 會觸發 onClose → finish
            modal.close();
        });

        modal.setContent(content);
        modal.mount();
        modal.open();

        return modal;
    }

    /**
     * 快速建立輸入對話框
     */
    static prompt(options = {}) {
        const {
            title = '輸入',
            message = '',
            placeholder = '',
            confirmText = '確認',
            cancelText = '取消',
            validate = () => true, // 驗證函式 (value) => boolean
            onConfirm = () => { },
            onCancel = () => { },
            onClose = null,
            ...rest
        } = options;

        // ESC / 遮罩 / 標題列 X 也必須結算，否則呼叫端(BasePage.prompt)的
        // Promise 永遠不會 resolve；settled 保證每條路徑只結算一次
        let settled = false;
        const finishCancel = () => {
            if (settled) return;
            settled = true;
            onCancel();
            onClose?.();
        };

        const modal = new ModalPanel({
            title,
            closable: true,
            destroyOnClose: true,
            onClose: finishCancel,
            ...rest
        });

        const content = document.createElement('div');

        const msgEl = document.createElement('p');
        msgEl.style.cssText = MODAL_PROMPT_MESSAGE_STYLE;
        applyText(msgEl, message);

        const input = document.createElement('input');
        input.type = 'text';
        input.className = 'modal-input';
        // 屬性值走 setAttribute，raw() 在屬性情境沒有意義，只取其字面值
        input.setAttribute('placeholder', isRawHtml(placeholder)
            ? rawHtmlOf(placeholder)
            : String(placeholder ?? ''));
        input.style.cssText = MODAL_INPUT_STYLE;

        const btnRow = document.createElement('div');
        btnRow.style.cssText = MODAL_ROW_STYLE;

        const cancelBtn = document.createElement('button');
        cancelBtn.type = 'button';
        cancelBtn.className = 'modal-cancel';
        cancelBtn.style.cssText = MODAL_BTN_SECONDARY;
        applyText(cancelBtn, cancelText);

        const confirmBtn = document.createElement('button');
        confirmBtn.type = 'button';
        confirmBtn.className = 'modal-confirm';
        confirmBtn.style.cssText = MODAL_BTN_PRIMARY;
        applyText(confirmBtn, confirmText);

        btnRow.append(cancelBtn, confirmBtn);
        content.append(msgEl, input, btnRow);

        // 輸入驗證樣式
        input.addEventListener('input', () => {
            const isValid = validate(input.value);
            confirmBtn.disabled = !isValid;
            confirmBtn.style.opacity = isValid ? '1' : '0.5';
            confirmBtn.style.cursor = isValid ? 'pointer' : 'not-allowed';
            input.style.borderColor = isValid ? '#ddd' : '#F44336';
        });

        // 初始驗證
        input.dispatchEvent(new Event('input'));

        const submit = () => {
            if (settled || !validate(input.value)) return;
            settled = true;
            modal.close();
            onConfirm(input.value);
            onClose?.();
        };

        cancelBtn.addEventListener('click', () => {
            // close() 會觸發 onClose → finishCancel
            modal.close();
        });

        confirmBtn.addEventListener('click', submit);

        // Enter 提交
        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') submit();
        });

        modal.setContent(content);
        modal.mount();
        modal.open();

        // 自動聚焦
        setTimeout(() => input.focus(), 100);

        return modal;
    }
}

export default ModalPanel;
