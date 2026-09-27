/**
 * DirtyGuard — 追蹤尚未儲存的變更，離開前請使用者確認。
 *
 *   const guard = createDirtyGuard();
 *   guard.track(formElement, { key: 'profile' });   // 表單／容器元素
 *   guard.track(notesEditor, { key: 'notes' });     // 具 getValue() 的元件
 *   guard.onChange((dirty) => saveButton.setDisabled(!dirty));
 *
 *   // 儲存成功後重新設定基準
 *   guard.markClean();
 *
 *   // 路由守衛或關閉按鈕
 *   router.beforeLeave(() => guard.confirmLeave());
 *   closeButton.addEventListener('click', guard.wrap(() => drawer.close()));
 *
 * 來源種類：
 * - Element：監聽其內部 input / change 事件，比對表單控制項值的快照。
 * - 具 isDirty() 的物件：狀態由物件自己決定。
 * - 具 getValue() 的元件：track 當下記錄基準值；以穩定鍵序的深度比較判斷是否變更。
 *
 * B4A 元件的值改變時多半只呼叫 options.onChange，不發 DOM 事件（下拉選單、日曆還可能
 * 渲染在元件外）。因此有元件或 isDirty 物件被追蹤時，會在 document 以捕獲階段監聽使用者
 * 互動（input / change / click / keyup / pointerup），於事件處理結束後重新比對；
 * 沒有這類來源時不掛這些監聽。程式化改值請呼叫 guard.check()。
 */
import Locale from '../i18n/index.js';
import { ModalPanel } from '../layout/Panel/ModalPanel.js';
import { nextUid } from './uid.js';
import './dirty-guard.locale.js';

const DOCUMENT_EVENTS = ['input', 'change', 'click', 'keyup', 'pointerup'];
const SKIPPED_INPUT_TYPES = new Set(['button', 'submit', 'reset', 'image']);
const CONTROL_SELECTOR = 'input, select, textarea';
const IGNORE_SELECTOR = '[data-dirty-ignore]';
const FOCUSABLE_SELECTOR = 'button, [href], input, select, textarea, [tabindex]';
const DIRTY = '\u0000dirty';
const CLEAN = '\u0000clean';

/**
 * 穩定序列化：物件鍵排序（鍵序不同視為相同）、陣列保留順序、
 * 值為 undefined 的鍵忽略、Date 以 ISO 表示、Map/Set 排序後比較、循環參照不會無限遞迴。
 */
function stableSerialize(value) {
    const ancestors = new Set();
    const walk = (current) => {
        if (current === null) return 'null';
        switch (typeof current) {
            case 'undefined': return 'undefined';
            case 'string': return JSON.stringify(current);
            case 'number':
                if (Number.isNaN(current)) return 'NaN';
                return current === 0 ? '0' : String(current);
            case 'boolean': return current ? 'true' : 'false';
            case 'bigint': return `${current}n`;
            case 'function':
            case 'symbol':
                return typeof current;
            default:
                break;
        }
        if (ancestors.has(current)) return '"[Circular]"';
        ancestors.add(current);
        try {
            if (current instanceof Date) {
                return `Date(${Number.isNaN(current.getTime()) ? 'Invalid' : current.toISOString()})`;
            }
            if (Array.isArray(current)) return `[${current.map(walk).join(',')}]`;
            if (current instanceof Map) {
                return `Map{${[...current].map(([key, item]) => `${walk(key)}=>${walk(item)}`).sort().join(',')}}`;
            }
            if (current instanceof Set) return `Set[${[...current].map(walk).sort().join(',')}]`;
            if (typeof ArrayBuffer !== 'undefined' && ArrayBuffer.isView(current)) {
                const bytes = new Uint8Array(current.buffer, current.byteOffset, current.byteLength);
                return `${current.constructor.name}(${bytes.join(',')})`;
            }
            if (typeof File !== 'undefined' && current instanceof File) {
                return `File(${JSON.stringify([current.name, current.size, current.type, current.lastModified])})`;
            }
            if (typeof Blob !== 'undefined' && current instanceof Blob) {
                return `Blob(${JSON.stringify([current.size, current.type])})`;
            }
            if (typeof current.toJSON === 'function') return walk(current.toJSON());
            const keys = Object.keys(current).filter((key) => current[key] !== undefined).sort();
            return `{${keys.map((key) => `${JSON.stringify(key)}:${walk(current[key])}`).join(',')}}`;
        } finally {
            ancestors.delete(current);
        }
    };
    return walk(value);
}

/** 表單控制項值的快照；button/submit/reset/image 與 [data-dirty-ignore] 內的控制項不計入。 */
function snapshotControls(root) {
    const controls = [];
    if (typeof root.matches === 'function' && root.matches(CONTROL_SELECTOR)) controls.push(root);
    controls.push(...root.querySelectorAll(CONTROL_SELECTOR));

    const values = [];
    for (const control of controls) {
        const marker = control.closest(IGNORE_SELECTOR);
        if (marker && (marker === root || root.contains(marker))) continue;
        const name = control.name || control.id || '';
        const tag = control.tagName;
        if (tag === 'SELECT') {
            values.push([name, Array.from(control.options, (option) => (option.selected ? option.value : null))
                .filter((item) => item !== null)]);
        } else if (tag === 'TEXTAREA') {
            values.push([name, control.value]);
        } else {
            const type = String(control.type || 'text').toLowerCase();
            if (SKIPPED_INPUT_TYPES.has(type)) continue;
            if (type === 'checkbox' || type === 'radio') {
                values.push([name, control.checked]);
            } else if (type === 'file') {
                values.push([name, Array.from(control.files || [], (file) => `${file.name}:${file.size}:${file.lastModified}`)]);
            } else {
                values.push([name, control.value]);
            }
        }
    }
    return stableSerialize(values);
}

function sourceKind(source) {
    if (!source || typeof source !== 'object') return null;
    if (source.nodeType === 1 && typeof source.querySelectorAll === 'function') return 'element';
    if (typeof source.isDirty === 'function') return 'object';
    if (typeof source.getValue === 'function') return 'component';
    return null;
}

function readSource(entry) {
    try {
        if (entry.kind === 'element') return snapshotControls(entry.source);
        if (entry.kind === 'object') return entry.source.isDirty() ? DIRTY : CLEAN;
        return stableSerialize(entry.source.getValue());
    } catch (error) {
        console.warn(`[DirtyGuard] could not read source "${entry.key}"; keeping its last known state`, error);
        return entry.current;
    }
}

function isEntryDirty(entry) {
    if (entry.kind === 'object') return entry.current === DIRTY;
    return entry.current !== entry.baseline;
}

function restoreFocus(element) {
    if (!element || typeof document === 'undefined' || element === document.body) return;
    if (!element.isConnected || typeof element.focus !== 'function') return;
    try {
        element.focus();
    } catch {
        // 無法聚焦時忽略
    }
}

/** 讓 Tab / Shift+Tab 在對話框內循環。 */
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

/** 補上 ModalPanel.confirm 缺少的對話框語意與焦點處理；初始焦點放在「留在此頁」。 */
function prepareDialog(modal, focusText) {
    const dialog = modal && modal.element;
    if (!dialog) return;
    const id = nextUid('b4a-dirty-guard-dialog');
    dialog.setAttribute('role', 'alertdialog');
    dialog.setAttribute('aria-modal', 'true');
    const title = dialog.querySelector('.panel__title');
    if (title) {
        if (!title.id) title.id = `${id}-title`;
        dialog.setAttribute('aria-labelledby', title.id);
    }
    const body = modal.content ? modal.content.querySelector('p') : null;
    if (body) {
        if (!body.id) body.id = `${id}-message`;
        dialog.setAttribute('aria-describedby', body.id);
    }
    dialog.addEventListener('keydown', (event) => trapTab(event, dialog));
    const stay = [...dialog.querySelectorAll('button')].find((button) => button.textContent === focusText);
    if (stay) stay.focus();
}

export class DirtyGuard {
    /**
     * @param {object} [options]
     * @param {Function|null} [options.confirm=null] - (message, { dirtyKeys }) => boolean|Promise<boolean>；回傳 true 表示離開。null 用 ModalPanel.confirm
     * @param {string|Function|null} [options.message=null] - 確認訊息；函式形式收到 dirtyKeys。null 用 Locale 預設
     * @param {boolean} [options.beforeUnload=true] - 有未儲存變更時是否掛 beforeunload 提示
     */
    constructor({ confirm = null, message = null, beforeUnload = true } = {}) {
        if (confirm !== null && confirm !== undefined && typeof confirm !== 'function') {
            throw new TypeError('[DirtyGuard] confirm must be a function or null');
        }
        this._confirm = confirm || null;
        this._message = message;
        this._beforeUnload = beforeUnload !== false;
        this._entries = new Map();
        this._listeners = new Set();
        this._dirtyKeys = [];
        this._seq = 0;
        this._timer = null;
        this._documentListening = false;
        this._unloadListening = false;
        this._leaveFingerprint = null;
        this._pending = null;
        this._modal = null;
        this._destroyed = false;
        this._onDocumentActivity = () => this._schedule();
        this._onBeforeUnload = (event) => this._handleBeforeUnload(event);
    }

    /**
     * 開始追蹤一個來源。
     * @param {Element|object} source - 表單／容器元素、具 isDirty() 的物件，或具 getValue() 的元件
     * @param {{key?: string}} [options] - key 預設自動產生；重複的 key 會取代舊的追蹤
     * @returns {Function} untrack()
     */
    track(source, options = {}) {
        if (this._destroyed) return () => {};
        const kind = sourceKind(source);
        if (!kind) {
            throw new TypeError('[DirtyGuard] track() needs an Element, an object with isDirty(), or a component with getValue()');
        }
        const { key } = options || {};
        const entryKey = key === undefined || key === null ? `source-${++this._seq}` : String(key);
        if (this._entries.has(entryKey)) this._untrack(entryKey);

        const entry = { key: entryKey, kind, source, baseline: '', current: '', cleanup: null };
        entry.current = readSource(entry);
        entry.baseline = entry.current;
        if (kind === 'element') entry.cleanup = this._listenElement(entry);
        this._entries.set(entryKey, entry);
        this._syncDocumentListeners();
        this._sync();

        return () => {
            if (this._entries.get(entryKey) === entry) this._untrack(entryKey);
        };
    }

    /** 立即重新讀取所有來源（程式化改值後呼叫）；回傳是否有未儲存變更。 */
    check() {
        if (this._destroyed) return false;
        for (const entry of this._entries.values()) entry.current = readSource(entry);
        return this._sync();
    }

    isDirty() {
        return this.check();
    }

    /** 有未儲存變更的 key（依 track 順序）。 */
    getDirtyKeys() {
        this.check();
        return [...this._dirtyKeys];
    }

    /**
     * 以目前的值作為新基準（儲存成功後呼叫）。不給 key 表示全部。
     * isDirty 物件會呼叫它自己的 markClean()（若有）。
     */
    markClean(key) {
        if (this._destroyed) return;
        const entries = key === undefined || key === null
            ? [...this._entries.values()]
            : [this._entries.get(String(key))].filter(Boolean);
        for (const entry of entries) {
            if (entry.kind === 'object' && typeof entry.source.markClean === 'function') {
                try {
                    entry.source.markClean();
                } catch (error) {
                    console.error(`[DirtyGuard] markClean() of "${entry.key}" failed`, error);
                }
            }
            entry.current = readSource(entry);
            entry.baseline = entry.current;
        }
        this._sync();
    }

    /**
     * 未儲存狀態（dirty key 集合）改變時呼叫 listener(dirty, dirtyKeys)。
     * @returns {Function} 取消訂閱
     */
    onChange(listener) {
        if (typeof listener !== 'function') throw new TypeError('[DirtyGuard] onChange(listener) needs a function');
        if (this._destroyed) return () => {};
        this._listeners.add(listener);
        return () => {
            this._listeners.delete(listener);
        };
    }

    /**
     * 沒有未儲存變更時直接 resolve(true)；否則詢問使用者，選擇離開時 resolve(true)。
     * 對話框開啟期間重複呼叫會共用同一個結果。
     * @returns {Promise<boolean>}
     */
    confirmLeave() {
        if (this._destroyed || !this.check()) return Promise.resolve(true);
        if (this._pending) return this._pending.promise;

        let resolvePromise;
        const promise = new Promise((resolve) => { resolvePromise = resolve; });
        const pending = {
            promise,
            settled: false,
            settle: (ok) => {
                if (pending.settled) return;
                pending.settled = true;
                if (this._pending === pending) this._pending = null;
                if (ok && !this._destroyed) {
                    // 使用者已同意離開：暫停 beforeunload，避免整頁導向時再跳一次瀏覽器提示；
                    // 內容再有變動就恢復。
                    this._leaveFingerprint = this._fingerprint();
                    this._syncUnloadListener();
                }
                resolvePromise(ok);
            }
        };
        this._pending = pending;

        const message = this._resolveMessage();
        let result;
        try {
            result = this._confirm
                ? this._confirm(message, { dirtyKeys: [...this._dirtyKeys] })
                : this._openDefaultConfirm(message);
        } catch (error) {
            console.error('[DirtyGuard] confirm failed; staying on the page', error);
            result = false;
        }
        Promise.resolve(result).then(
            (ok) => pending.settle(ok === true),
            (error) => {
                console.error('[DirtyGuard] confirm failed; staying on the page', error);
                pending.settle(false);
            }
        );
        return promise;
    }

    /**
     * 包裝函式：confirmLeave() 為 true 才執行 fn（給路由鉤子、關閉按鈕使用）。
     * @returns {Function} async (...args) => fn 的回傳值；取消時為 undefined
     */
    wrap(fn) {
        if (typeof fn !== 'function') throw new TypeError('[DirtyGuard] wrap(fn) needs a function');
        const guard = this;
        return async function guarded(...args) {
            if (!(await guard.confirmLeave())) return undefined;
            return fn.apply(this, args);
        };
    }

    /** 移除所有監聽與計時器；開啟中的預設對話框會關閉，等待中的 confirmLeave() 得到 false。 */
    destroy() {
        if (this._destroyed) return;
        this._destroyed = true;
        for (const entry of this._entries.values()) {
            if (entry.cleanup) entry.cleanup();
        }
        this._entries.clear();
        this._dirtyKeys = [];
        this._syncDocumentListeners();
        this._syncUnloadListener();
        if (this._timer !== null) {
            clearTimeout(this._timer);
            this._timer = null;
        }
        this._listeners.clear();
        const pending = this._pending;
        const modal = this._modal;
        this._pending = null;
        this._modal = null;
        if (pending) pending.settle(false);
        if (modal) {
            try {
                modal.close();
            } catch {
                // 對話框已被外部銷毀
            }
        }
    }

    _untrack(key) {
        const entry = this._entries.get(key);
        if (!entry) return;
        if (entry.cleanup) entry.cleanup();
        this._entries.delete(key);
        this._syncDocumentListeners();
        this._sync();
    }

    _listenElement(entry) {
        const element = entry.source;
        const onEdit = () => {
            if (this._destroyed || this._entries.get(entry.key) !== entry) return;
            entry.current = readSource(entry);
            this._sync();
        };
        // 點擊（新增／刪除列）與 reset 在事件處理完成後才會反映到控制項，延後比對
        const onLater = () => this._schedule();
        element.addEventListener('input', onEdit);
        element.addEventListener('change', onEdit);
        element.addEventListener('click', onLater);
        element.addEventListener('reset', onLater);
        return () => {
            element.removeEventListener('input', onEdit);
            element.removeEventListener('change', onEdit);
            element.removeEventListener('click', onLater);
            element.removeEventListener('reset', onLater);
        };
    }

    _syncDocumentListeners() {
        if (typeof document === 'undefined') return;
        let needed = false;
        if (!this._destroyed) {
            for (const entry of this._entries.values()) {
                if (entry.kind !== 'element') {
                    needed = true;
                    break;
                }
            }
        }
        if (needed === this._documentListening) return;
        for (const type of DOCUMENT_EVENTS) {
            if (needed) document.addEventListener(type, this._onDocumentActivity, true);
            else document.removeEventListener(type, this._onDocumentActivity, true);
        }
        this._documentListening = needed;
    }

    _schedule() {
        if (this._destroyed || this._timer !== null) return;
        // 捕獲階段收到事件時元件尚未處理完，等整個事件分派結束再比對
        this._timer = setTimeout(() => {
            this._timer = null;
            this.check();
        }, 0);
    }

    _sync() {
        const keys = [];
        for (const entry of this._entries.values()) {
            if (isEntryDirty(entry)) keys.push(entry.key);
        }
        if (this._leaveFingerprint !== null && this._fingerprint() !== this._leaveFingerprint) {
            this._leaveFingerprint = null;
        }
        const changed = keys.length !== this._dirtyKeys.length
            || keys.some((key, index) => key !== this._dirtyKeys[index]);
        this._dirtyKeys = keys;
        this._syncUnloadListener();
        if (changed) this._notify();
        return keys.length > 0;
    }

    _fingerprint() {
        return [...this._entries.values()].map((entry) => `${entry.key}\u0001${entry.current}`).join('\u0002');
    }

    _syncUnloadListener() {
        if (typeof window === 'undefined') return;
        const needed = !this._destroyed && this._beforeUnload && this._dirtyKeys.length > 0
            && this._leaveFingerprint === null;
        if (needed === this._unloadListening) return;
        if (needed) window.addEventListener('beforeunload', this._onBeforeUnload);
        else window.removeEventListener('beforeunload', this._onBeforeUnload);
        this._unloadListening = needed;
    }

    _handleBeforeUnload(event) {
        if (!this.check() || this._leaveFingerprint !== null) return undefined;
        const message = this._resolveMessage();
        event.preventDefault();
        // 舊版瀏覽器需要非空的 returnValue 才會提示；現代瀏覽器只顯示內建文字
        event.returnValue = message;
        return message;
    }

    _notify() {
        const dirty = this._dirtyKeys.length > 0;
        const keys = Object.freeze([...this._dirtyKeys]);
        for (const listener of [...this._listeners]) {
            try {
                listener(dirty, keys);
            } catch (error) {
                console.error('[DirtyGuard] onChange listener failed', error);
            }
        }
    }

    _resolveMessage() {
        const custom = this._message;
        if (typeof custom === 'function') {
            try {
                const out = custom([...this._dirtyKeys]);
                if (out !== null && out !== undefined && out !== '') return String(out);
            } catch (error) {
                console.error('[DirtyGuard] message function failed; using the default message', error);
            }
        } else if (custom !== null && custom !== undefined && custom !== '') {
            return String(custom);
        }
        return Locale.t('dirtyGuard.message');
    }

    _openDefaultConfirm(message) {
        return new Promise((resolve) => {
            const previousFocus = typeof document !== 'undefined' ? document.activeElement : null;
            const stayText = Locale.t('dirtyGuard.stay');
            let done = false;
            let modal = null;
            const finish = (ok) => {
                if (done) return;
                done = true;
                if (modal && this._modal === modal) this._modal = null;
                restoreFocus(previousFocus);
                resolve(ok);
            };
            modal = ModalPanel.confirm({
                title: Locale.t('dirtyGuard.title'),
                message,
                confirmText: Locale.t('dirtyGuard.leave'),
                cancelText: stayText,
                onConfirm: () => finish(true),
                onCancel: () => finish(false)
            });
            if (!done) this._modal = modal;
            prepareDialog(modal, stayText);
        });
    }
}

/**
 * 建立未儲存變更守衛。
 * @param {object} [options] - 見 DirtyGuard constructor
 * @returns {DirtyGuard}
 */
export function createDirtyGuard(options = {}) {
    return new DirtyGuard(options || {});
}

export default createDirtyGuard;
