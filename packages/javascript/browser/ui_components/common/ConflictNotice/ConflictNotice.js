/**
 * ConflictNotice — 樂觀並行控制衝突提示。
 *
 * 儲存時伺服器回報「資料已被其他人更新」，用它告訴使用者差異，並讓使用者選擇：
 *   'reload'    載入伺服器上的最新資料
 *   'overwrite' 以自己的版本覆寫（預設需要再確認一次）
 *   'cancel'    先不處理
 *
 *   // 對話框（ModalPanel，destroyOnClose）
 *   const action = await ConflictNotice.show({ diffs, serverUpdatedBy, serverUpdatedAt });
 *
 *   // 行內（Alert 風格區塊）
 *   new ConflictNotice({ diffs, onResolve: (action) => { ... } }).mount('#notice-host');
 *
 * 所有顯示內容（欄位名稱、值、修改者）一律以 textContent 寫入，資料中的 HTML 不會被解析。
 */
import Locale from '../../i18n/index.js';
import { ModalPanel } from '../../layout/Panel/index.js';
import { createComponentState } from '../../utils/component-state.js';
import { nextUid } from '../../utils/uid.js';
import './locale.js';

const ACTION_KEYS = Object.freeze(['reload', 'overwrite', 'cancel']);
/** 只供 ConflictNotice.show() 使用：以對話框內容的方式渲染（不含標題、不設 role="alert"）。 */
const EMBEDDED = Symbol('conflictNotice.embedded');
const FOCUSABLE_SELECTOR = 'button, [href], input, select, textarea, [tabindex]';

const ROOT_INLINE_STYLE = [
    'display: flex',
    'flex-direction: column',
    'gap: 10px',
    'box-sizing: border-box',
    'padding: 12px 16px',
    'border: 1px solid var(--cl-warning)',
    'border-left-width: 4px',
    'border-radius: var(--cl-radius-lg)',
    'background: var(--cl-warning-light)',
    'color: var(--cl-text)',
    'font-family: var(--cl-font-family)',
    'font-size: var(--cl-font-size-lg)'
].join('; ') + ';';
const ROOT_EMBEDDED_STYLE = 'display: flex; flex-direction: column; gap: 12px; max-width: 640px; color: var(--cl-text); font-family: var(--cl-font-family); font-size: var(--cl-font-size-lg);';
const TITLE_STYLE = 'margin: 0; font-weight: 600; font-size: var(--cl-font-size-xl); color: var(--cl-text);';
const MESSAGE_STYLE = 'margin: 0; line-height: 1.5; color: var(--cl-text);';
const META_STYLE = 'margin: 0; font-size: var(--cl-font-size-md); color: var(--cl-text-secondary);';
const TABLE_STYLE = 'width: 100%; border-collapse: collapse; font-size: var(--cl-font-size-md); color: var(--cl-text); background: var(--cl-bg);';
const CAPTION_STYLE = 'caption-side: top; text-align: left; padding: 0 0 4px; font-weight: 600; font-size: var(--cl-font-size-md); color: var(--cl-text-secondary);';
const CELL_STYLE = 'border: 1px solid var(--cl-border-light); padding: 6px 8px; text-align: left; vertical-align: top; overflow-wrap: anywhere;';
const HEAD_CELL_STYLE = `${CELL_STYLE} background: var(--cl-bg-tertiary); color: var(--cl-text-secondary); font-weight: 600;`;
const ROW_HEAD_STYLE = `${CELL_STYLE} font-weight: 600;`;
const ACTIONS_STYLE = 'flex-wrap: wrap; justify-content: flex-end; gap: 8px;';
const CONFIRM_STYLE = 'flex-direction: column; gap: 8px; padding: 10px 12px; border: 1px solid var(--cl-danger); border-radius: var(--cl-radius-md); background: var(--cl-bg-danger-light);';
const CONFIRM_MESSAGE_STYLE = 'margin: 0; font-weight: 600; color: var(--cl-text);';
const CONFIRM_BUTTONS_STYLE = 'display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 8px;';

const BUTTON_BASE_STYLE = [
    'display: inline-flex',
    'align-items: center',
    'justify-content: center',
    'padding: 8px 16px',
    'border: 1px solid transparent',
    'border-radius: var(--cl-radius-md)',
    'font-size: var(--cl-font-size-lg)',
    'font-family: var(--cl-font-family)',
    'line-height: 1.4',
    'cursor: pointer',
    'transition: opacity var(--cl-transition-fast), background var(--cl-transition-fast)'
].join('; ') + ';';
const BUTTON_VARIANT_STYLE = {
    primary: 'background: var(--cl-primary); border-color: var(--cl-primary); color: var(--cl-text-inverse);',
    danger: 'background: var(--cl-danger); border-color: var(--cl-danger); color: var(--cl-text-inverse);',
    secondary: 'background: var(--cl-bg); border-color: var(--cl-border); color: var(--cl-text);'
};

function interpolate(template, params) {
    return String(template).replace(/\{(\w+)\}/g, (match, key) =>
        (params[key] !== undefined ? String(params[key]) : match));
}

function formatDate(date, dateTimeFormat) {
    const format = dateTimeFormat && typeof dateTimeFormat === 'object' ? dateTimeFormat : undefined;
    try {
        return new Intl.DateTimeFormat(Locale.getLang(), format).format(date);
    } catch {
        try {
            return new Intl.DateTimeFormat(undefined, format).format(date);
        } catch {
            return date.toISOString();
        }
    }
}

function formatObject(value, strings) {
    try {
        const json = JSON.stringify(value);
        return json === undefined ? strings.empty : json;
    } catch {
        return String(value);
    }
}

// 注意：模組層級函式內不要寫四格縮排的 `if (...) {`，metadata 擷取器會把它誤認成公開方法。
function formatDefault(value, strings, dateTimeFormat) {
    if (value === null || value === undefined || value === '') return strings.empty;
    if (typeof value === 'boolean') return value ? strings.yes : strings.no;
    if (value instanceof Date) return Number.isNaN(value.getTime()) ? strings.empty : formatDate(value, dateTimeFormat);
    if (Array.isArray(value)) {
        return value.length
            ? value.map((item) => formatDefault(item, strings, dateTimeFormat)).join(', ')
            : strings.empty;
    }
    if (typeof value === 'object') return formatObject(value, strings);
    return String(value);
}

function setShown(element, shown, display = 'flex') {
    element.hidden = !shown;
    element.style.display = shown ? display : 'none';
}

function restoreFocus(element) {
    if (!element || element === document.body || !element.isConnected || typeof element.focus !== 'function') return;
    try {
        element.focus();
    } catch {
        // 無法聚焦時忽略
    }
}

function trapTab(event, container) {
    if (event.key !== 'Tab') return;
    const items = [...container.querySelectorAll(FOCUSABLE_SELECTOR)].filter((item) =>
        !item.disabled && item.getAttribute('tabindex') !== '-1' && !item.closest('[hidden]'));
    if (items.length === 0) return;
    const first = items[0];
    const last = items[items.length - 1];
    const active = container.ownerDocument.activeElement;
    if (event.shiftKey && (active === first || !container.contains(active))) {
        event.preventDefault();
        last.focus();
    } else if (!event.shiftKey && (active === last || !container.contains(active))) {
        event.preventDefault();
        first.focus();
    }
}

export class ConflictNotice {
    static ACTIONS = Object.freeze({ RELOAD: 'reload', OVERWRITE: 'overwrite', CANCEL: 'cancel' });

    /**
     * 以對話框顯示衝突提示。Esc、右上角關閉鈕都視為 'cancel'（即使 actions 不含 'cancel'）。
     * 選擇 'overwrite' 且 confirmOverwrite 為 true 時，會先在對話框內要求再確認一次。
     * @param {object} options - 同 constructor
     * @returns {Promise<'reload'|'overwrite'|'cancel'>}
     */
    static show(options = {}) {
        const opts = options && typeof options === 'object' ? options : {};
        return new Promise((resolve) => {
            const previousFocus = document.activeElement;
            const userOnResolve = opts.onResolve;
            let settled = false;
            let modal = null;
            let notice = null;

            const finish = (action) => {
                if (settled) return;
                settled = true;
                if (modal) modal.close();
                const finished = notice;
                queueMicrotask(() => finished?.destroy());
                restoreFocus(previousFocus);
                resolve(action);
                if (typeof userOnResolve === 'function') {
                    try {
                        userOnResolve(action);
                    } catch (error) {
                        console.error('[ConflictNotice] onResolve failed', error);
                    }
                }
            };

            notice = new ConflictNotice({ ...opts, onResolve: finish, [EMBEDDED]: true });
            const title = notice._titleText();
            modal = new ModalPanel({
                title,
                closable: true,
                // 需要明確選擇；點遮罩不關閉
                autoClose: false,
                destroyOnClose: true,
                onClose: () => finish('cancel')
            });
            notice.mount(modal.content);

            const dialog = modal.element;
            dialog.setAttribute('role', 'alertdialog');
            dialog.setAttribute('aria-modal', 'true');
            const titleElement = dialog.querySelector('.panel__title');
            if (titleElement) {
                if (!titleElement.id) titleElement.id = notice._idFor('dialog-title');
                dialog.setAttribute('aria-labelledby', titleElement.id);
            } else {
                dialog.setAttribute('aria-label', notice._strings().title);
            }
            if (notice._messageElement) dialog.setAttribute('aria-describedby', notice._messageElement.id);
            dialog.addEventListener('keydown', (event) => trapTab(event, dialog));

            modal.mount();
            modal.open();
            notice.focus();
        });
    }

    constructor(options = {}) {
        this.options = {
            title: null,               // 標題；null 時用 Locale（conflictNotice.title）
            message: null,             // 說明文字；null 時用 Locale（conflictNotice.message）
            diffs: [],                 // 差異列：[{ field, label, local, server }]，以純文字顯示
            serverUpdatedBy: null,     // 伺服器端最後修改者（純文字）
            serverUpdatedAt: null,     // 伺服器端最後修改時間：Date、ISO 字串或毫秒數
            dateTimeFormat: { dateStyle: 'medium', timeStyle: 'short' }, // Intl.DateTimeFormat 選項
            actions: ['reload', 'overwrite', 'cancel'], // 顯示哪些選項與順序
            confirmOverwrite: true,    // 選擇覆寫時再確認一次
            danger: true,              // 覆寫按鈕使用 danger 樣式
            labels: {},                // 覆寫 conflictNotice 命名空間的任一字串（按鈕、欄位標題…）
            formatValue: null,         // (value, { diff, side }) => string；回傳 undefined 改用預設格式
            onResolve: null,           // (action) => void：使用者做出選擇時呼叫
            ...options
        };

        this._embedded = this.options[EMBEDDED] === true;
        this._uid = nextUid('cl-conflict-notice');
        this._destroyed = false;
        this._actionList = this._normalizeActions(this.options.actions);
        this._buttons = {};
        this._confirmButtons = {};
        this._actionsElement = null;
        this._confirmElement = null;
        this._messageElement = null;
        this.element = null;

        this.result = new Promise((resolve) => { this._settleResult = resolve; });
        this._state = createComponentState(
            { lifecycle: 'created', step: 'choose', resolved: null },
            {
                MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
                CONFIRM: (state) => ({ ...state, step: 'confirm' }),
                BACK: (state) => ({ ...state, step: 'choose' }),
                RESOLVE: (state, payload) => ({ ...state, step: 'done', resolved: payload.action }),
                DESTROY: (state) => ({ ...state, lifecycle: 'destroyed' })
            }
        );

        this._rootClickHandler = (event) => this._handleClick(event);
        this._onKeydown = (event) => this._handleKeydown(event);
        this._create();
    }

    /** 目前狀態：{ lifecycle, step: 'choose'|'confirm'|'done', resolved } */
    snapshot() {
        return this._state.snapshot();
    }

    mount(container) {
        if (this._destroyed || !this.element) return this;
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) {
            console.warn('[ConflictNotice] mount target not found:', container);
            return this;
        }
        target.appendChild(this.element);
        if (this.snapshot().lifecycle === 'created') this._state.send('MOUNT');
        return this;
    }

    /** 聚焦最安全的選項（第一個非覆寫的按鈕）；確認步驟中聚焦「返回」。 */
    focus() {
        if (this._destroyed || !this.element) return this;
        const step = this.snapshot().step;
        if (step === 'confirm') {
            this._confirmButtons.back?.focus();
        } else if (step === 'choose') {
            const safe = this._actionList.find((action) => action !== 'overwrite') || this._actionList[0];
            this._buttons[safe]?.focus();
        }
        return this;
    }

    destroy() {
        if (this._destroyed) return;
        this._destroyed = true;
        if (this.element) {
            this.element.removeEventListener('click', this._rootClickHandler);
            this.element.removeEventListener('keydown', this._onKeydown);
            this.element.remove();
        }
        this.element = null;
        this._buttons = {};
        this._confirmButtons = {};
        this._actionsElement = null;
        this._confirmElement = null;
        this._messageElement = null;
        this._state.send('DESTROY');
        // 未做選擇就被銷毀：讓等待 result 的程式不會永遠卡住（不呼叫 onResolve）
        this._settleResult('cancel');
    }

    _idFor(part) {
        return `${this._uid}-${part}`;
    }

    _strings() {
        const labels = this.options.labels && typeof this.options.labels === 'object' ? this.options.labels : undefined;
        return Locale.getComponentStrings('conflictNotice', labels);
    }

    _titleText(strings = this._strings()) {
        const { title } = this.options;
        return title === null || title === undefined ? strings.title : String(title);
    }

    _normalizeActions(actions) {
        const source = Array.isArray(actions) ? actions : ACTION_KEYS;
        const result = [];
        for (const action of source) {
            if (!ACTION_KEYS.includes(action)) {
                console.warn(`[ConflictNotice] unknown action "${String(action)}" ignored`);
                continue;
            }
            if (!result.includes(action)) result.push(action);
        }
        return result.length ? result : [...ACTION_KEYS];
    }

    _create() {
        const strings = this._strings();
        const root = document.createElement('div');
        root.className = 'cl-conflict-notice';
        root.dataset.variant = this._embedded ? 'modal' : 'inline';
        root.style.cssText = this._embedded ? ROOT_EMBEDDED_STYLE : ROOT_INLINE_STYLE;

        if (!this._embedded) {
            root.setAttribute('role', 'alert');
            const titleText = this._titleText(strings);
            if (titleText) {
                const title = document.createElement('div');
                title.className = 'cl-conflict-notice__title';
                title.id = this._idFor('title');
                title.style.cssText = TITLE_STYLE;
                title.textContent = titleText;
                root.setAttribute('aria-labelledby', title.id);
                root.appendChild(title);
            }
        }

        const messageText = this.options.message === null || this.options.message === undefined
            ? strings.message
            : String(this.options.message);
        if (messageText) {
            const message = document.createElement('p');
            message.className = 'cl-conflict-notice__message';
            message.id = this._idFor('message');
            message.style.cssText = MESSAGE_STYLE;
            message.textContent = messageText;
            if (!this._embedded) root.setAttribute('aria-describedby', message.id);
            root.appendChild(message);
            this._messageElement = message;
        }

        const meta = this._buildMeta(strings);
        if (meta) root.appendChild(meta);

        const table = this._buildDiffTable(strings);
        if (table) root.appendChild(table);

        const actions = document.createElement('div');
        actions.className = 'cl-conflict-notice__actions';
        actions.setAttribute('role', 'group');
        actions.setAttribute('aria-label', strings.actionsLabel);
        actions.style.cssText = ACTIONS_STYLE;
        setShown(actions, true);
        for (const action of this._actionList) {
            const button = this._createButton(action, strings[action], this._variantFor(action));
            this._buttons[action] = button;
            actions.appendChild(button);
        }
        root.appendChild(actions);
        this._actionsElement = actions;

        if (this._actionList.includes('overwrite') && this.options.confirmOverwrite) {
            root.appendChild(this._buildConfirm(strings));
        }

        root.addEventListener('click', this._rootClickHandler);
        root.addEventListener('keydown', this._onKeydown);
        this.element = root;
    }

    _variantFor(action) {
        if (action === 'reload') return 'primary';
        if (action === 'overwrite') return this.options.danger ? 'danger' : 'secondary';
        return 'secondary';
    }

    _createButton(action, label, variant) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = `cl-conflict-notice__button cl-conflict-notice__button--${action}`;
        button.dataset.action = action;
        button.style.cssText = `${BUTTON_BASE_STYLE} ${BUTTON_VARIANT_STYLE[variant] || BUTTON_VARIANT_STYLE.secondary}`;
        button.textContent = label;
        return button;
    }

    _buildConfirm(strings) {
        const box = document.createElement('div');
        box.className = 'cl-conflict-notice__confirm';
        box.setAttribute('role', 'group');
        box.style.cssText = CONFIRM_STYLE;
        setShown(box, false);

        const text = document.createElement('p');
        text.id = this._idFor('confirm');
        text.style.cssText = CONFIRM_MESSAGE_STYLE;
        text.textContent = strings.confirmOverwriteMessage;
        box.setAttribute('aria-labelledby', text.id);

        const row = document.createElement('div');
        row.style.cssText = CONFIRM_BUTTONS_STYLE;
        const back = this._createButton('back', strings.back, 'secondary');
        const confirm = this._createButton('confirm-overwrite', strings.confirmOverwrite,
            this.options.danger ? 'danger' : 'primary');
        row.append(back, confirm);
        box.append(text, row);

        this._confirmButtons = { back, confirm };
        this._confirmElement = box;
        return box;
    }

    _buildMeta(strings) {
        const { serverUpdatedBy } = this.options;
        const user = serverUpdatedBy === null || serverUpdatedBy === undefined ? '' : String(serverUpdatedBy);
        const time = this._formatTimestamp(this.options.serverUpdatedAt);
        if (!user && !time) return null;
        let text;
        if (user && time) text = interpolate(strings.updatedByAt, { user, time });
        else if (user) text = interpolate(strings.updatedBy, { user });
        else text = interpolate(strings.updatedAt, { time });

        const meta = document.createElement('p');
        meta.className = 'cl-conflict-notice__meta';
        meta.style.cssText = META_STYLE;
        meta.textContent = text;
        return meta;
    }

    _formatTimestamp(value) {
        if (value === null || value === undefined || value === '') return '';
        const date = value instanceof Date ? value : new Date(value);
        if (Number.isNaN(date.getTime())) return String(value);
        return formatDate(date, this.options.dateTimeFormat);
    }

    _buildDiffTable(strings) {
        const diffs = Array.isArray(this.options.diffs)
            ? this.options.diffs.filter((diff) => diff && typeof diff === 'object')
            : [];
        if (diffs.length === 0) return null;

        const table = document.createElement('table');
        table.className = 'cl-conflict-notice__diffs';
        table.style.cssText = TABLE_STYLE;

        const caption = document.createElement('caption');
        caption.style.cssText = CAPTION_STYLE;
        caption.textContent = strings.diffCaption;

        const head = document.createElement('thead');
        const headRow = document.createElement('tr');
        for (const text of [strings.columnField, strings.columnLocal, strings.columnServer]) {
            const cell = document.createElement('th');
            cell.scope = 'col';
            cell.style.cssText = HEAD_CELL_STYLE;
            cell.textContent = text;
            headRow.appendChild(cell);
        }
        head.appendChild(headRow);

        const body = document.createElement('tbody');
        const rows = document.createDocumentFragment();
        for (const diff of diffs) {
            const row = document.createElement('tr');
            row.className = 'cl-conflict-notice__row';
            if (diff.field !== null && diff.field !== undefined) row.dataset.field = String(diff.field);

            const label = document.createElement('th');
            label.scope = 'row';
            label.style.cssText = ROW_HEAD_STYLE;
            const labelValue = diff.label ?? diff.field;
            label.textContent = labelValue === null || labelValue === undefined ? '' : String(labelValue);

            const local = document.createElement('td');
            local.className = 'cl-conflict-notice__local';
            local.style.cssText = CELL_STYLE;
            local.textContent = this._formatValue(diff.local, diff, 'local', strings);

            const server = document.createElement('td');
            server.className = 'cl-conflict-notice__server';
            server.style.cssText = CELL_STYLE;
            server.textContent = this._formatValue(diff.server, diff, 'server', strings);

            row.append(label, local, server);
            rows.appendChild(row);
        }
        body.appendChild(rows);
        table.append(caption, head, body);
        return table;
    }

    _formatValue(value, diff, side, strings) {
        const custom = this.options.formatValue;
        if (typeof custom === 'function') {
            let out;
            try {
                out = custom(value, { diff, side });
            } catch (error) {
                console.error('[ConflictNotice] formatValue failed; using the default format', error);
                out = undefined;
            }
            if (out !== undefined) return out === null || out === '' ? strings.empty : String(out);
        }
        return formatDefault(value, strings, this.options.dateTimeFormat);
    }

    _handleClick(event) {
        const button = event.target && typeof event.target.closest === 'function'
            ? event.target.closest('button[data-action]')
            : null;
        if (!button || !this.element || !this.element.contains(button) || button.disabled) return;
        const action = button.dataset.action;
        if (action === 'overwrite') {
            if (this._confirmElement) this._enterConfirm();
            else this._resolve('overwrite');
        } else if (action === 'confirm-overwrite') {
            this._resolve('overwrite');
        } else if (action === 'back') {
            this._leaveConfirm();
        } else if (ACTION_KEYS.includes(action)) {
            this._resolve(action);
        }
    }

    _handleKeydown(event) {
        if (event.key !== 'Escape' || this.snapshot().step !== 'confirm') return;
        // 確認步驟中 Esc 只回到上一步；不讓外層（例如 ModalPanel）把整個對話框關掉
        event.preventDefault();
        event.stopPropagation();
        this._leaveConfirm();
    }

    _enterConfirm() {
        if (this._destroyed || this.snapshot().step !== 'choose') return;
        this._state.send('CONFIRM');
        setShown(this._actionsElement, false);
        setShown(this._confirmElement, true);
        this._confirmButtons.back?.focus();
    }

    _leaveConfirm() {
        if (this._destroyed || this.snapshot().step !== 'confirm') return;
        this._state.send('BACK');
        setShown(this._confirmElement, false);
        setShown(this._actionsElement, true);
        this._buttons.overwrite?.focus();
    }

    _resolve(action) {
        if (this._destroyed || this.snapshot().resolved) return;
        this._state.send('RESOLVE', { action });
        // 選擇後停用所有按鈕，避免重複送出（例如連點兩次覆寫）
        this.element.querySelectorAll('button').forEach((button) => {
            button.disabled = true;
            button.style.opacity = '0.6';
            button.style.cursor = 'not-allowed';
        });
        this._settleResult(action);
        if (typeof this.options.onResolve === 'function') {
            try {
                this.options.onResolve(action);
            } catch (error) {
                console.error('[ConflictNotice] onResolve failed', error);
            }
        }
    }
}

export default ConflictNotice;
