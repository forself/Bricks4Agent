/**
 * BasePage - 頁面基礎類別
 *
 * 所有頁面都應繼承此類別，提供：
 * - 統一的生命週期管理
 * - 響應式資料綁定
 * - 事件管理
 * - 路由參數存取
 * - XSS 防護 (escapeHtml, escapeAttr)
 *
 * @module BasePage
 * @version 1.0.0
 *
 * @example
 * class HomePage extends BasePage {
 *     async onInit() {
 *         this._data = { userName: '<script>alert("xss")</script>' };
 *     }
 *
 *     template() {
 *         // 使用 this.esc() 轉義使用者輸入，防止 XSS
 *         return `<h1>Hello, ${this.esc(this._data.userName)}</h1>`;
 *     }
 * }
 */

import { escapeHtml, escapeAttr, sanitizeInput } from './Security.js';
import { ToastPanel } from '../components/Panel/ToastPanel.js';
import { ModalPanel } from '../components/Panel/ModalPanel.js';

export class BasePage {
    /**
     * @param {Object} options - 頁面配置
     * @param {Object} options.router - 路由器
     * @param {Object} options.store - 狀態管理
     * @param {Object} options.api - API 服務
     * @param {Object} options.params - 路由參數
     * @param {Object} options.query - 查詢參數
     * @param {Object} options.meta - 路由元資訊
     */
    constructor(options = {}) {
        this.router = options.router;
        this.store = options.store;
        this.api = options.api;
        this.params = options.params || {};
        this.query = options.query || {};
        this.meta = options.meta || {};

        this.element = null;
        this._data = {};
        this._eventCleanups = [];
        this._storeCleanups = [];
        this._mounted = false;
    }

    /**
     * 響應式資料 (修改會觸發重新渲染)
     */
    get data() {
        return new Proxy(this._data, {
            set: (target, key, value) => {
                const oldValue = target[key];
                target[key] = value;

                // 只有在掛載後且值有變化時才重新渲染
                if (this._mounted && oldValue !== value) {
                    this._scheduleUpdate();
                }

                return true;
            }
        });
    }

    /**
     * 掛載頁面到容器
     * @param {HTMLElement} container - 容器元素
     */
    async mount(container) {
        const mountElement = document.createElement('div');
        try {
            // 建立頁面元素
            this.element = mountElement;
            mountElement.className = this._getPageClassName();

            // 執行初始化
            await this.onInit();

            if (this.element !== mountElement) {
                return;
            }

            // 渲染內容
            mountElement.innerHTML = this.template();

            // 加入容器
            container.appendChild(mountElement);

            // 標記已掛載
            this._mounted = true;

            // 綁定事件
            this._bindEvents();

            // 執行掛載後鉤子
            await this.onMounted();

        } catch (error) {
            console.error(`[${this.constructor.name}] 掛載失敗:`, error);
            if (this.element !== mountElement) {
                return;
            }
            this._renderError(container, error);
        }
    }

    /**
     * 取得頁面 class 名稱
     */
    _getPageClassName() {
        // 將 PascalCase 轉為 kebab-case
        const name = this.constructor.name;
        return 'page-' + name.replace(/([A-Z])/g, '-$1').toLowerCase().slice(1);
    }

    /**
     * 生命週期: 初始化 (載入資料)
     * 子類別覆寫此方法
     */
    async onInit() {
        // 子類別實作
    }

    /**
     * 生命週期: 已掛載 (可操作 DOM)
     * 子類別覆寫此方法
     */
    async onMounted() {
        // 子類別實作
    }

    /**
     * 生命週期: 銷毀前
     * 子類別覆寫此方法
     */
    async onDestroy() {
        // 子類別實作
    }

    /**
     * 頁面模板 (子類別必須實作)
     * @returns {string} HTML 字串
     */
    template() {
        return '<div>請實作 template() 方法</div>';
    }

    /**
     * 事件綁定 (子類別覆寫)
     * 返回事件配置物件
     */
    events() {
        return {};
        // 範例:
        // return {
        //     'click .btn-submit': 'onSubmit',
        //     'input .search-input': 'onSearch'
        // };
    }

    /**
     * 綁定事件
     */
    _bindEvents() {
        const eventConfig = this.events();

        Object.entries(eventConfig).forEach(([key, handler]) => {
            const [eventType, selector] = key.split(' ');
            const method = typeof handler === 'string' ? this[handler].bind(this) : handler;

            if (!method) {
                console.warn(`[${this.constructor.name}] 找不到事件處理器: ${handler}`);
                return;
            }

            // 使用事件委派
            const listener = (event) => {
                if (selector) {
                    const target = event.target.closest(selector);
                    if (target && this.element.contains(target)) {
                        method(event, target);
                    }
                } else {
                    method(event);
                }
            };

            this.element.addEventListener(eventType, listener);

            // 記錄以便清理
            this._eventCleanups.push(() => {
                this.element.removeEventListener(eventType, listener);
            });
        });
    }

    /**
     * 手動綁定事件 (帶自動清理)
     */
    on(target, eventType, handler) {
        const element = typeof target === 'string'
            ? this.element.querySelector(target)
            : target;

        if (!element) return;

        element.addEventListener(eventType, handler);
        this._eventCleanups.push(() => {
            element.removeEventListener(eventType, handler);
        });
    }

    /**
     * 訂閱 Store 狀態 (帶自動清理)
     */
    watch(key, callback) {
        if (!this.store) return;

        const unsubscribe = this.store.subscribe(key, callback);
        this._storeCleanups.push(unsubscribe);
    }

    /**
     * 排程更新 (防抖)
     */
    _scheduleUpdate() {
        if (this._updateTimer) {
            cancelAnimationFrame(this._updateTimer);
        }

        this._updateTimer = requestAnimationFrame(() => {
            this._update();
        });
    }

    /**
     * 更新頁面內容
     */
    _update() {
        if (!this._mounted || !this.element) return;

        // 保存焦點元素
        const focusedId = document.activeElement?.id;

        // 重新渲染
        this.element.innerHTML = this.template();

        // 重新綁定事件
        this._eventCleanups.forEach(cleanup => cleanup());
        this._eventCleanups = [];
        this._bindEvents();

        // 恢復焦點
        if (focusedId) {
            const element = document.getElementById(focusedId);
            element?.focus();
        }

        // 呼叫更新後鉤子
        this.onUpdated?.();
    }

    /**
     * HTML 轉義 - 防止 XSS 攻擊
     * 在 template() 中輸出使用者資料時必須使用此方法
     *
     * @param {any} value - 要轉義的值
     * @returns {string} 安全的 HTML 字串
     *
     * @example
     * template() {
     *     return `<span>${this.esc(this._data.userName)}</span>`;
     * }
     */
    esc(value) {
        return escapeHtml(value);
    }

    /**
     * HTML 屬性值轉義
     * 在 template() 中輸出屬性值時使用
     *
     * @param {any} value - 要轉義的值
     * @returns {string} 安全的屬性值字串
     *
     * @example
     * template() {
     *     return `<input value="${this.escAttr(this._data.searchQuery)}">`;
     * }
     */
    escAttr(value) {
        return escapeAttr(value);
    }

    /**
     * 清理使用者輸入
     *
     * @param {string} input - 使用者輸入
     * @param {number} maxLength - 最大長度
     * @returns {string} 清理後的字串
     */
    sanitize(input, maxLength = 1000) {
        return sanitizeInput(input, maxLength);
    }

    /**
     * 查詢元素
     * @param {string} selector - CSS 選擇器
     * @returns {HTMLElement|null}
     */
    $(selector) {
        return this.element?.querySelector(selector);
    }

    /**
     * 查詢所有元素
     * @param {string} selector - CSS 選擇器
     * @returns {NodeList}
     */
    $$(selector) {
        return this.element?.querySelectorAll(selector) || [];
    }

    /**
     * 導航到其他頁面
     */
    navigate(path, options) {
        this.router?.navigate(path, options);
    }

    /**
     * 顯示載入狀態
     */
    showLoading() {
        this.store?.set('loading', true);
    }

    /**
     * 隱藏載入狀態
     */
    hideLoading() {
        this.store?.set('loading', false);
    }

    /**
     * 顯示訊息 (使用 ToastPanel 元件)
     * @param {string} message - 訊息內容
     * @param {string} type - 訊息類型: 'info' | 'success' | 'warning' | 'error'
     */
    showMessage(message, type = 'info') {
        ToastPanel.show(message, {
            type,
            position: 'top-right',
            timeout: 3000,
            closable: true
        });
    }

    /**
     * 顯示確認對話框 (使用 ModalPanel 元件)
     * @param {Object} options - 對話框選項
     * @param {string} options.title - 標題
     * @param {string} options.message - 訊息內容
     * @param {string} options.confirmText - 確認按鈕文字
     * @param {string} options.cancelText - 取消按鈕文字
     * @returns {Promise<boolean>} 使用者選擇結果
     */
    confirm(options = {}) {
        const {
            title = '確認',
            message = '',
            confirmText = '確認',
            cancelText = '取消'
        } = options;

        return new Promise((resolve) => {
            ModalPanel.confirm({
                title,
                message,
                confirmText,
                cancelText,
                onConfirm: () => resolve(true),
                onCancel: () => resolve(false)
            });
        });
    }

    /**
     * 顯示提示對話框 (使用 ModalPanel 元件)
     * @param {Object} options - 對話框選項
     * @param {string} options.title - 標題
     * @param {string} options.message - 訊息內容
     * @param {string} options.confirmText - 確認按鈕文字
     * @returns {Promise<void>}
     */
    alert(options = {}) {
        const {
            title = '提示',
            message = '',
            confirmText = '確定'
        } = options;

        return new Promise((resolve) => {
            ModalPanel.alert({
                title,
                message,
                confirmText,
                onConfirm: () => resolve()
            });
        });
    }

    /**
     * 顯示輸入對話框 (使用 ModalPanel 元件)
     * @param {Object} options - 對話框選項
     * @param {string} options.title - 標題
     * @param {string} options.message - 訊息內容
     * @param {string} options.placeholder - 輸入框提示文字
     * @param {Function} options.validate - 驗證函式
     * @returns {Promise<string|null>} 使用者輸入值，取消時為 null
     */
    prompt(options = {}) {
        const {
            title = '輸入',
            message = '',
            placeholder = '',
            confirmText = '確認',
            cancelText = '取消',
            validate = () => true
        } = options;

        return new Promise((resolve) => {
            ModalPanel.prompt({
                title,
                message,
                placeholder,
                confirmText,
                cancelText,
                validate,
                onConfirm: (value) => resolve(value),
                onCancel: () => resolve(null)
            });
        });
    }

    /**
     * 渲染錯誤頁面
     */
    _renderError(container, error) {
        // 注意: 錯誤訊息也需要轉義以防止 XSS
        const safeMessage = escapeHtml(error.message || '未知錯誤');
        container.innerHTML = `
            <div class="page-error">
                <h2>頁面載入失敗</h2>
                <p>${safeMessage}</p>
                <button onclick="location.reload()">重新載入</button>
            </div>
        `;
    }

    /**
     * 銷毀頁面
     */
    async destroy() {
        try {
            // 執行銷毀前鉤子
            await this.onDestroy();

            // 清理事件
            this._eventCleanups.forEach(cleanup => cleanup());
            this._eventCleanups = [];

            // 清理 Store 訂閱
            this._storeCleanups.forEach(cleanup => cleanup());
            this._storeCleanups = [];

            // 移除元素
            if (this.element && this.element.parentNode) {
                this.element.parentNode.removeChild(this.element);
            }

            this._mounted = false;
            this.element = null;

        } catch (error) {
            console.error(`[${this.constructor.name}] 銷毀失敗:`, error);
        }
    }
}

export default BasePage;
