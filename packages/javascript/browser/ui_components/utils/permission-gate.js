/**
 * PermissionGate — 依「能力字串」隱藏或停用畫面上的元素（僅前端 UX）。
 *
 * 安全邊界：這只是介面層的便利措施，不是存取控制。每一個能力都必須由伺服器端
 * 再次驗證；使用者可以用開發者工具移除 hidden / disabled，前端閘門擋不住任何請求。
 *
 *   const gate = createPermissionGate({
 *       can: (capability, user) => user.permissions.includes(capability),
 *       context: currentUser
 *   });
 *   const handle = gate.apply(deleteButton, 'orders.delete', { mode: 'disable' });
 *   gate.scan(document.body);          // 處理 [data-permission] 宣告
 *   await gate.setContext(nextUser);   // 登入或角色變更後重新評估
 *   handle.release();                  // 還原元素原本的狀態
 *
 * 設計要點：
 * - 能力字串對本模組是不透明的；組合只透過 { anyOf: [] } / { allOf: [] }，不解析字串。
 * - can() 只有回傳 true（或 resolve 成 true）才算允許；false、其他值、丟例外或 reject 一律拒絕。
 * - 非同步結果回來前維持目前狀態（不閃爍）；較舊的評估結果晚到時直接丟棄。
 * - 同一個目標可被多個 handle（甚至多個 gate）套用：以目標為鍵彙整，
 *   任一 handle 拒絕即維持隱藏／停用，全部放行或釋放後才還原成第一次套用前的狀態。
 */
import Locale from '../i18n/index.js';
import './permission-gate.locale.js';

const MODES = Object.freeze(['hide', 'disable']);
const SCAN_SELECTOR = '[data-permission], [data-permission-any], [data-permission-all]';
const BLOCKED_EVENTS = ['click', 'auxclick', 'keydown'];

/** 宣告了 data-permission 卻沒有任何能力時使用：永遠拒絕（fail closed）。 */
const NEVER = Object.freeze({ allOf: Object.freeze([]), anyOf: Object.freeze([]), never: true });

/**
 * 目標 → 彙整紀錄。模組層級共用，讓不同 gate 套在同一目標上也能正確組合與還原。
 * 鍵是實際被操作的對象：Element，或具備對應方法的元件實例。
 */
const targetRecords = new WeakMap();

function warn(message, detail) {
    if (typeof console === 'undefined') return;
    if (detail === undefined) console.warn(`[PermissionGate] ${message}`);
    else console.warn(`[PermissionGate] ${message}`, detail);
}

function isElement(value) {
    return Boolean(value) && typeof value === 'object' && value.nodeType === 1
        && typeof value.getAttribute === 'function';
}

function assertMode(mode) {
    if (!MODES.includes(mode)) {
        throw new TypeError(`[PermissionGate] mode must be 'hide' or 'disable' (got ${String(mode)})`);
    }
}

function normalizeList(list, name) {
    if (list === undefined || list === null) return [];
    if (!Array.isArray(list)) {
        throw new TypeError(`[PermissionGate] ${name} must be an array of capability strings`);
    }
    const result = new Set();
    for (const item of list) {
        if (typeof item !== 'string' || item.trim() === '') {
            throw new TypeError(`[PermissionGate] ${name} entries must be non-empty strings`);
        }
        result.add(item);
    }
    return [...result];
}

function freezeRequirement(allOf, anyOf) {
    return Object.freeze({ allOf: Object.freeze(allOf), anyOf: Object.freeze(anyOf) });
}

/**
 * 'cap' → { allOf: ['cap'], anyOf: [] }
 * { anyOf: [...] } / { allOf: [...] } / 兩者並用（allOf 全部成立且 anyOf 至少一項成立）
 */
function normalizeRequirement(requirement) {
    if (typeof requirement === 'string') {
        if (requirement.trim() === '') {
            throw new TypeError('[PermissionGate] capability must be a non-empty string');
        }
        return freezeRequirement([requirement], []);
    }
    if (requirement && typeof requirement === 'object' && !Array.isArray(requirement)) {
        const allOf = normalizeList(requirement.allOf, 'allOf');
        const anyOf = normalizeList(requirement.anyOf, 'anyOf');
        if (allOf.length === 0 && anyOf.length === 0) {
            throw new TypeError('[PermissionGate] { anyOf } / { allOf } needs at least one capability');
        }
        return freezeRequirement(allOf, anyOf);
    }
    throw new TypeError('[PermissionGate] capability must be a string, { anyOf: [...] } or { allOf: [...] }');
}

/**
 * 依需求判定。check(capability) 回傳 true / false / Promise<boolean>。
 * 同步結果足以決定時同步回傳 boolean，否則回傳 Promise<boolean>。
 */
function decide(requirement, check) {
    if (requirement.never) return false;

    const waitAll = [];
    for (const capability of requirement.allOf) {
        const result = check(capability);
        if (result === false) return false;
        if (result !== true) waitAll.push(result);
    }

    let anySatisfied = requirement.anyOf.length === 0;
    const waitAny = [];
    for (const capability of requirement.anyOf) {
        const result = check(capability);
        if (result === true) {
            anySatisfied = true;
            break;
        }
        if (result !== false) waitAny.push(result);
    }
    if (!anySatisfied && waitAny.length === 0) return false;
    if (anySatisfied && waitAll.length === 0) return true;

    const allPart = Promise.all(waitAll).then((values) => values.every(Boolean));
    const anyPart = anySatisfied ? true : Promise.all(waitAny).then((values) => values.some(Boolean));
    return Promise.all([allPart, anyPart]).then(([all, any]) => all && any);
}

function splitList(value) {
    if (value === null || value === undefined) return [];
    return String(value).split(',').map((item) => item.trim()).filter(Boolean);
}

/** 讀取元素上的 data-permission* 宣告（不產生警告；警告在真的建立 handle 時才發）。 */
function readDeclaration(element) {
    const allOf = [...new Set([
        ...splitList(element.getAttribute('data-permission')),
        ...splitList(element.getAttribute('data-permission-all'))
    ])];
    const anyOf = [...new Set(splitList(element.getAttribute('data-permission-any')))];
    const rawMode = (element.getAttribute('data-permission-mode') || '').trim().toLowerCase();
    const mode = MODES.includes(rawMode) ? rawMode : null;
    const reason = element.getAttribute('data-permission-reason');
    const empty = allOf.length === 0 && anyOf.length === 0;

    return {
        requirement: empty ? NEVER : freezeRequirement(allOf, anyOf),
        mode,
        reason,
        signature: JSON.stringify([allOf, anyOf, mode, reason]),
        badMode: rawMode && !mode ? rawMode : null,
        empty
    };
}

/** 決定這個模式實際要操作的對象：元件本身（有對應方法時），否則是它的根元素。 */
function resolveKey(target, mode) {
    if (isElement(target)) return target;
    if (target && typeof target === 'object') {
        if (mode === 'hide' && typeof target.hide === 'function' && typeof target.show === 'function') return target;
        if (mode === 'disable' && typeof target.setDisabled === 'function') return target;
        if (isElement(target.element)) return target.element;
    }
    throw new TypeError('[PermissionGate] target must be an Element, or a component with hide()/show(), setDisabled() or an element property');
}

function getRecord(key) {
    let record = targetRecords.get(key);
    if (!record) {
        record = {
            key,
            kind: isElement(key) ? 'element' : 'component',
            handles: new Set(),
            hideSaved: null,
            disableSaved: null
        };
        targetRecords.set(key, record);
    }
    return record;
}

function createSaved() {
    // attrs: 第一次改寫某屬性前的原值（null 表示原本沒有），還原時逐一寫回
    return { attrs: new Map(), display: null, blocker: null, component: {} };
}

function safeCall(fn, label) {
    try {
        fn();
    } catch (error) {
        warn(`${label} failed`, error);
    }
}

function setAttr(element, saved, name, value) {
    if (!saved.attrs.has(name)) saved.attrs.set(name, element.getAttribute(name));
    if (element.getAttribute(name) !== value) element.setAttribute(name, value);
}

function restoreAttr(element, saved, name) {
    if (!saved.attrs.has(name)) return;
    const original = saved.attrs.get(name);
    if (original === null) element.removeAttribute(name);
    else element.setAttribute(name, original);
    saved.attrs.delete(name);
}

function restoreElement(element, saved) {
    if (saved.blocker) {
        for (const type of BLOCKED_EVENTS) element.removeEventListener(type, saved.blocker, true);
        saved.blocker = null;
    }
    for (const name of [...saved.attrs.keys()]) restoreAttr(element, saved, name);
    if (saved.display) {
        const { value, priority, hadStyle } = saved.display;
        if (value) element.style.setProperty('display', value, priority);
        else element.style.removeProperty('display');
        if (!hadStyle && element.getAttribute('style') === '') element.removeAttribute('style');
        saved.display = null;
    }
}

/** hidden 屬性被行內 display 或作者樣式蓋掉時（元素仍然顯示），才需要強制隱藏。 */
function stillDisplayed(element) {
    const inline = element.style ? element.style.getPropertyValue('display') : '';
    if (inline && inline !== 'none') return true;
    if (!element.isConnected) return false;
    const view = element.ownerDocument && element.ownerDocument.defaultView;
    if (!view || typeof view.getComputedStyle !== 'function') return false;
    try {
        return view.getComputedStyle(element).display !== 'none';
    } catch {
        return false;
    }
}

function forceHidden(element, saved) {
    if (!saved.display) {
        saved.display = {
            value: element.style.getPropertyValue('display'),
            priority: element.style.getPropertyPriority('display'),
            hadStyle: element.hasAttribute('style')
        };
    }
    element.style.setProperty('display', 'none', 'important');
    // 只有 hidden 屬性不足以隱藏時才補 aria-hidden
    setAttr(element, saved, 'aria-hidden', 'true');
}

function supportsNativeDisabled(element) {
    return 'disabled' in element;
}

/** 沒有原生 disabled 的元素（連結、role=button…）：攔下點擊與 Enter/Space 啟用。 */
function createActivationBlocker(element) {
    return (event) => {
        if (event.type === 'keydown') {
            if (event.target !== element) return;
            if (event.key !== 'Enter' && event.key !== ' ' && event.key !== 'Spacebar') return;
        }
        event.preventDefault();
        event.stopImmediatePropagation();
    };
}

function readComponentVisible(component) {
    try {
        const snapshot = typeof component.snapshot === 'function' ? component.snapshot() : null;
        if (snapshot && typeof snapshot.visibility === 'string') {
            return snapshot.visibility !== 'hidden' && snapshot.visibility !== 'none';
        }
    } catch {
        // 取不到快照就改用其他線索
    }
    if (typeof component.isVisible === 'function') {
        try {
            return Boolean(component.isVisible());
        } catch {
            // 繼續往下判斷
        }
    }
    if (component.options && typeof component.options.visibility === 'string') {
        return component.options.visibility === 'visible';
    }
    const element = component.element;
    if (isElement(element)) return !element.hidden && element.style.display !== 'none';
    return true;
}

function readComponentDisabled(component) {
    try {
        const snapshot = typeof component.snapshot === 'function' ? component.snapshot() : null;
        if (snapshot && typeof snapshot.availability === 'string') return snapshot.availability === 'disabled';
    } catch {
        // 取不到快照就改用其他線索
    }
    if (typeof component.isDisabled === 'function') {
        try {
            return Boolean(component.isDisabled());
        } catch {
            // 繼續往下判斷
        }
    }
    if (component.options && typeof component.options.disabled === 'boolean') return component.options.disabled;
    if (typeof component.disabled === 'boolean') return component.disabled;
    return false;
}

function applyHide(record, saved, displayChecks) {
    if (record.kind === 'component') {
        const component = record.key;
        if (!('visible' in saved.component)) {
            saved.component.visible = readComponentVisible(component);
            if (typeof component.setVisibility === 'function' && component.options
                && typeof component.options.visibility === 'string') {
                saved.component.visibility = component.options.visibility;
            }
        }
        safeCall(() => component.hide(), 'hide()');
        return;
    }
    setAttr(record.key, saved, 'hidden', '');
    displayChecks.push({ element: record.key, saved });
}

function restoreHide(record, saved) {
    if (record.kind === 'component') {
        const component = record.key;
        const state = saved.component;
        if (state.visibility !== undefined && typeof component.setVisibility === 'function') {
            safeCall(() => component.setVisibility(state.visibility), 'setVisibility()');
        } else if (state.visible) {
            safeCall(() => component.show(), 'show()');
        }
        return;
    }
    restoreElement(record.key, saved);
}

function applyReason(element, saved, reason) {
    if (reason) {
        setAttr(element, saved, 'title', reason);
        setAttr(element, saved, 'aria-description', reason);
    } else {
        // 明確給空字串：不顯示原因，並撤回先前寫上的原因
        restoreAttr(element, saved, 'title');
        restoreAttr(element, saved, 'aria-description');
    }
}

function applyDisable(record, saved, reason) {
    if (record.kind === 'component') {
        const component = record.key;
        if (!('disabled' in saved.component)) saved.component.disabled = readComponentDisabled(component);
        safeCall(() => component.setDisabled(true), 'setDisabled(true)');
        if (isElement(component.element)) applyReason(component.element, saved, reason);
        return;
    }
    const element = record.key;
    const native = supportsNativeDisabled(element);
    if (native) setAttr(element, saved, 'disabled', '');
    setAttr(element, saved, 'aria-disabled', 'true');
    applyReason(element, saved, reason);
    if (!native && !saved.blocker) {
        saved.blocker = createActivationBlocker(element);
        for (const type of BLOCKED_EVENTS) element.addEventListener(type, saved.blocker, true);
    }
}

function restoreDisable(record, saved) {
    if (record.kind === 'component') {
        const component = record.key;
        safeCall(() => component.setDisabled(Boolean(saved.component.disabled)), 'setDisabled()');
        if (isElement(component.element)) restoreElement(component.element, saved);
        return;
    }
    restoreElement(record.key, saved);
}

function reconcile(record, displayChecks) {
    let hide = false;
    let disableEntry = null;
    for (const entry of record.handles) {
        if (entry.decision !== false) continue;
        if (entry.mode === 'hide') hide = true;
        else if (!disableEntry) disableEntry = entry;
    }

    if (hide) {
        if (!record.hideSaved) record.hideSaved = createSaved();
        applyHide(record, record.hideSaved, displayChecks);
    } else if (record.hideSaved) {
        restoreHide(record, record.hideSaved);
        record.hideSaved = null;
    }

    if (disableEntry) {
        if (!record.disableSaved) record.disableSaved = createSaved();
        applyDisable(record, record.disableSaved, disableEntry.gate._resolveReason(disableEntry));
    } else if (record.disableSaved) {
        restoreDisable(record, record.disableSaved);
        record.disableSaved = null;
    }

    if (record.handles.size === 0 && !record.hideSaved && !record.disableSaved
        && targetRecords.get(record.key) === record) {
        targetRecords.delete(record.key);
    }
}

/** 批次處理：先完成所有寫入，再一次讀取 computed style，最後才補強制隱藏，避免反覆重算樣式。 */
function reconcileAll(records) {
    const displayChecks = [];
    for (const record of records) reconcile(record, displayChecks);
    if (displayChecks.length === 0) return;
    const needForce = displayChecks.filter(({ element }) => stillDisplayed(element));
    for (const { element, saved } of needForce) forceHidden(element, saved);
}

function createHandle(entry) {
    return Object.freeze({
        target: entry.target,
        mode: entry.mode,
        requirement: entry.requirement,
        get allowed() { return entry.decision; },
        get pending() { return entry.pending; },
        get released() { return entry.released; },
        get ready() { return entry.ready; },
        refresh: () => entry.gate._refreshEntry(entry),
        release: () => entry.gate._release(entry)
    });
}

function inertHandle(target, mode) {
    const ready = Promise.resolve(null);
    return Object.freeze({
        target,
        mode,
        requirement: null,
        allowed: null,
        pending: false,
        released: true,
        ready,
        refresh: () => ready,
        release: () => {}
    });
}

export class PermissionGate {
    /**
     * @param {object} options
     * @param {(capability: string, context: any) => boolean|Promise<boolean>} options.can - 能力判斷函式（必填）
     * @param {'hide'|'disable'} [options.mode='hide'] - 預設處理方式
     * @param {any} [options.context=null] - 傳給 can() 的第二個參數（例如目前使用者）
     * @param {string|Function|null} [options.deniedReason=null] - disable 模式的提示原因；null 用 Locale 預設
     */
    constructor({ can, mode = 'hide', context = null, deniedReason = null } = {}) {
        if (typeof can !== 'function') {
            throw new TypeError('[PermissionGate] createPermissionGate({ can }) requires a can(capability, context) function');
        }
        assertMode(mode);
        this._can = can;
        this._mode = mode;
        this._context = context;
        this._deniedReason = deniedReason;
        this._entries = new Set();
        this._scanned = new Map();
        this._queue = new Set();
        this._flushScheduled = false;
        this._destroyed = false;
    }

    /**
     * 對單一目標套用權限。
     * @param {Element|object} target - DOM 元素，或具 hide/show、setDisabled、element 的 B4A 元件
     * @param {string|{anyOf?: string[], allOf?: string[]}} requirement
     * @param {{mode?: 'hide'|'disable', reason?: string|Function}} [options]
     * @returns {object} handle
     */
    apply(target, requirement, options = {}) {
        const { mode, reason } = options || {};
        const resolvedMode = mode === undefined || mode === null ? this._mode : mode;
        if (this._destroyed) return inertHandle(target, resolvedMode);
        assertMode(resolvedMode);
        const normalized = normalizeRequirement(requirement);
        const entry = this._createEntry(target, normalized, resolvedMode, reason);
        this._evaluate([entry]);
        return entry.handle;
    }

    /**
     * 處理 root（含 root 本身）內所有 [data-permission] / [data-permission-any] / [data-permission-all]。
     * 已掃描過且宣告未變的元素沿用原 handle；宣告改變時先釋放舊的再重建。
     * @param {Element|Document} [root=document]
     * @returns {object[]} handles
     */
    scan(root = typeof document !== 'undefined' ? document : null) {
        if (this._destroyed) return [];
        if (!root || typeof root.querySelectorAll !== 'function') {
            throw new TypeError('[PermissionGate] scan(root) needs an Element or Document');
        }
        const elements = [];
        if (isElement(root) && typeof root.matches === 'function' && root.matches(SCAN_SELECTOR)) elements.push(root);
        elements.push(...root.querySelectorAll(SCAN_SELECTOR));

        const handles = [];
        const fresh = [];
        for (const element of elements) {
            const declaration = readDeclaration(element);
            const existing = this._scanned.get(element);
            if (existing && !existing.released && existing.signature === declaration.signature) {
                handles.push(existing.handle);
                continue;
            }
            if (declaration.badMode) {
                warn(`unknown data-permission-mode "${declaration.badMode}"; using the gate default`, element);
            }
            if (declaration.empty) {
                warn('element declares data-permission without any capability; it stays denied', element);
            }
            const entry = this._createEntry(element, declaration.requirement,
                declaration.mode || this._mode, declaration.reason);
            entry.signature = declaration.signature;
            if (existing) {
                // 宣告改變：新 handle 先沿用舊結果，新的評估回來前不閃爍
                entry.decision = existing.decision;
                this._release(existing);
            }
            this._scanned.set(element, entry);
            fresh.push(entry);
            handles.push(entry.handle);
        }
        if (fresh.length) this._evaluate(fresh);
        return handles;
    }

    /**
     * 重新評估所有已套用的目標（登入、角色或權限變更後呼叫）。
     * 非同步 can() 結果回來前維持目前狀態；過期的結果會被忽略。
     * @returns {Promise<void>}
     */
    refresh() {
        if (this._destroyed) return Promise.resolve();
        return this._evaluate([...this._entries]);
    }

    /**
     * 更新 can() 的 context，預設立即 refresh()。
     * @returns {Promise<void>}
     */
    setContext(context, { refresh = true } = {}) {
        if (this._destroyed) return Promise.resolve();
        this._context = context;
        return refresh ? this.refresh() : Promise.resolve();
    }

    getContext() {
        return this._context;
    }

    /** 釋放全部 handle 並還原所有目標；之後的呼叫都不會丟例外。 */
    destroy() {
        if (this._destroyed) return;
        const entries = [...this._entries];
        this._destroyed = true;
        for (const entry of entries) this._release(entry);
        this._scanned.clear();
        this._queue.clear();
    }

    _createEntry(target, requirement, mode, reason) {
        const key = resolveKey(target, mode);
        const record = getRecord(key);
        const entry = {
            gate: this,
            target,
            key,
            record,
            requirement,
            mode,
            reason: reason === undefined ? null : reason,
            decision: null,
            pending: false,
            released: false,
            seq: 0,
            signature: null,
            ready: Promise.resolve(null),
            handle: null
        };
        entry.handle = createHandle(entry);
        record.handles.add(entry);
        this._entries.add(entry);
        return entry;
    }

    /** 同一輪評估中，同一能力只問 can() 一次。 */
    _createChecker() {
        const cache = new Map();
        const context = this._context;
        return (capability) => {
            if (cache.has(capability)) return cache.get(capability);
            let outcome;
            try {
                const result = this._can(capability, context);
                if (result && typeof result.then === 'function') {
                    outcome = Promise.resolve(result).then(
                        (value) => value === true,
                        (error) => {
                            warn(`can("${capability}") rejected; treating it as denied`, error);
                            return false;
                        }
                    );
                } else {
                    outcome = result === true;
                }
            } catch (error) {
                warn(`can("${capability}") threw; treating it as denied`, error);
                outcome = false;
            }
            cache.set(capability, outcome);
            return outcome;
        };
    }

    _evaluate(entries) {
        const check = this._createChecker();
        const touched = new Set();
        const waiting = [];
        for (const entry of entries) {
            if (entry.released) continue;
            const seq = ++entry.seq;
            let outcome;
            try {
                outcome = decide(entry.requirement, check);
            } catch (error) {
                warn('permission check failed; treating it as denied', error);
                outcome = false;
            }
            if (typeof outcome === 'boolean') {
                entry.pending = false;
                entry.decision = outcome;
                entry.ready = Promise.resolve(outcome);
                touched.add(entry.record);
            } else {
                // 不閃爍：結果回來前不動 DOM，也不改 decision
                entry.pending = true;
                entry.ready = outcome.then(
                    (allowed) => this._settle(entry, seq, allowed === true),
                    (error) => {
                        warn('permission check failed; treating it as denied', error);
                        return this._settle(entry, seq, false);
                    }
                );
                waiting.push(entry.ready);
            }
        }
        if (touched.size) reconcileAll(touched);
        return Promise.all(waiting).then(() => undefined);
    }

    _settle(entry, seq, allowed) {
        // 過期（之後又評估過）、已釋放或 gate 已銷毀：丟棄這個結果
        if (this._destroyed || entry.released || seq !== entry.seq) return entry.decision;
        entry.pending = false;
        entry.decision = allowed;
        this._queue.add(entry.record);
        if (!this._flushScheduled) {
            this._flushScheduled = true;
            queueMicrotask(() => {
                this._flushScheduled = false;
                const records = [...this._queue];
                this._queue.clear();
                reconcileAll(records);
            });
        }
        return allowed;
    }

    _refreshEntry(entry) {
        if (this._destroyed || entry.released) return Promise.resolve(entry.decision);
        return this._evaluate([entry]).then(() => entry.decision);
    }

    _release(entry) {
        if (entry.released) return;
        entry.released = true;
        entry.pending = false;
        entry.seq += 1;
        this._entries.delete(entry);
        if (this._scanned.get(entry.target) === entry) this._scanned.delete(entry.target);
        entry.record.handles.delete(entry);
        reconcileAll([entry.record]);
    }

    /** handle 的 reason → gate 的 deniedReason → Locale 預設；函式形式收到 (requirement, context)。 */
    _resolveReason(entry) {
        const pick = (value) => {
            if (value === null || value === undefined) return undefined;
            if (typeof value === 'function') {
                try {
                    const out = value(entry.requirement, this._context);
                    return out === null || out === undefined ? undefined : String(out);
                } catch (error) {
                    warn('reason function threw; using the default reason', error);
                    return undefined;
                }
            }
            return String(value);
        };
        const own = pick(entry.reason);
        if (own !== undefined) return own;
        const shared = pick(this._deniedReason);
        if (shared !== undefined) return shared;
        return Locale.t('permissionGate.deniedReason');
    }
}

/**
 * 建立權限閘門。
 * @param {object} options - 見 PermissionGate constructor
 * @returns {PermissionGate}
 */
export function createPermissionGate(options = {}) {
    return new PermissionGate(options);
}

export default createPermissionGate;
