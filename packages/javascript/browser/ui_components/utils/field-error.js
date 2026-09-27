/**
 * 輸入元件共用的欄位錯誤呈現。
 *
 * 各輸入元件的 setError(message, { display }) / clearError() 委派到這裡，讓錯誤外觀與
 * 無障礙標示一致：
 *
 * - 錯誤狀態：target 元素加上 aria-invalid="true"；visual 元素以 outline 標示紅框。
 *   用 outline 而非 border，避免與元件自己在 hover、focus、展開時改寫的邊框互相覆蓋。
 * - 錯誤文字：display 為 true（預設）時產生 role="alert" 的訊息元素，並以
 *   aria-describedby 連到每個 target。
 * - display:false 只標示錯誤狀態、不產生文字，給自己顯示錯誤文字的外層使用
 *   （例如 FormField、SearchForm），避免同一個錯誤出現兩次。
 *
 * 訊息位置：預設附加在 container（未指定時為第一個 target 的父元素）末端；
 * 指定 after 時改插在該元素正後方。after 指向元件根元素時，訊息位於元件外，
 * 元件的 destroy() 必須呼叫 clearFieldError() 把它移除。
 *
 * setFieldError(owner, '') 等同 clearFieldError(owner)。
 */

/**
 * 以 setFieldError 實作 setError(message, { display }) 的元件帶有此標記。
 *
 * SearchForm 預設模式靠它辨認「本版才新增 setError 的元件」，不去呼叫，
 * 讓既有查詢表單的畫面維持原樣；設定 markInvalidFields: true 才會標示這些元件。
 */
export const FIELD_ERROR_CONTRACT = Symbol.for('bricks4agent.fieldErrorContract');

let fieldErrorSeq = 0;

const toList = (value) => (Array.isArray(value) ? value : [value]).filter(Boolean);

function state(owner) {
    if (!owner.__fieldError) {
        owner.__fieldError = { targets: [], visuals: [], messageEl: null, saved: new Map() };
    }
    return owner.__fieldError;
}

function addDescribedBy(target, id) {
    const ids = (target.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean);
    if (!ids.includes(id)) ids.push(id);
    target.setAttribute('aria-describedby', ids.join(' '));
}

function removeDescribedBy(target, id) {
    const ids = (target.getAttribute('aria-describedby') || '').split(/\s+/).filter(item => item && item !== id);
    if (ids.length) target.setAttribute('aria-describedby', ids.join(' '));
    else target.removeAttribute('aria-describedby');
}

function remember(entry, element) {
    if (!entry.saved.has(element)) {
        entry.saved.set(element, {
            ariaInvalid: element.getAttribute('aria-invalid'),
            outline: element.style.outline,
            outlineOffset: element.style.outlineOffset,
        });
    }
    return entry.saved.get(element);
}

function restore(entry, element, { aria = false, visual = false } = {}) {
    const saved = entry.saved.get(element);
    if (!saved) return;
    if (aria) {
        if (saved.ariaInvalid === null) element.removeAttribute('aria-invalid');
        else element.setAttribute('aria-invalid', saved.ariaInvalid);
    }
    if (visual) {
        element.style.outline = saved.outline;
        element.style.outlineOffset = saved.outlineOffset;
    }
}

function unmark(entry) {
    if (entry.messageEl) entry.targets.forEach(target => removeDescribedBy(target, entry.messageEl.id));
    entry.targets.forEach(target => restore(entry, target, { aria: true }));
    entry.visuals.forEach(element => restore(entry, element, { visual: true }));
    entry.targets = [];
    entry.visuals = [];
    entry.saved.clear();
}

function mark(entry, targets, visuals) {
    const sameTargets = targets.length === entry.targets.length && targets.every((t, i) => t === entry.targets[i]);
    const sameVisuals = visuals.length === entry.visuals.length && visuals.every((v, i) => v === entry.visuals[i]);
    if (!sameTargets || !sameVisuals) unmark(entry);

    targets.forEach(target => {
        remember(entry, target);
        target.setAttribute('aria-invalid', 'true');
    });
    visuals.forEach(element => {
        remember(entry, element);
        element.style.outline = '1px solid var(--cl-danger)';
        element.style.outlineOffset = '-1px';
    });
    entry.targets = targets;
    entry.visuals = visuals;
}

function removeMessage(entry) {
    if (!entry.messageEl) return;
    entry.targets.forEach(target => removeDescribedBy(target, entry.messageEl.id));
    entry.messageEl.remove();
    entry.messageEl = null;
}

function placeMessage(entry, { container, after, fullRow }) {
    if (!entry.messageEl) {
        const el = document.createElement('div');
        el.className = 'b4a-field-error';
        el.id = `b4a-field-error-${++fieldErrorSeq}`;
        el.setAttribute('role', 'alert');
        el.style.cssText = 'margin-top:4px;font-size:var(--cl-font-size-sm);line-height:1.4;color:var(--cl-danger);';
        entry.messageEl = el;
    }
    const el = entry.messageEl;
    // 在橫向 flex-wrap 或 grid 容器內獨佔一整列
    el.style.flexBasis = fullRow ? '100%' : '';
    el.style.gridColumn = fullRow ? '1 / -1' : '';

    if (after) {
        const parent = after.parentNode;
        if (!parent) {
            el.remove();
            return false;
        }
        if (after.nextSibling !== el) parent.insertBefore(el, after.nextSibling);
        return true;
    }
    if (!container) {
        el.remove();
        return false;
    }
    if (el.parentNode !== container || container.lastChild !== el) container.appendChild(el);
    return true;
}

/**
 * @param {object} owner - 元件實例（狀態記在 owner 上）
 * @param {string} message - 錯誤訊息；空值等同清除
 * @param {object} options
 * @param {Element|Element[]} options.target - 加上 aria-invalid 與 aria-describedby 的元素
 * @param {Element|Element[]|null} [options.visual] - 畫紅框的元素；預設同 target，null 表示不畫
 * @param {Element} [options.container] - 訊息附加的容器；預設為第一個 target 的父元素
 * @param {Element} [options.after] - 訊息改插在此元素正後方
 * @param {boolean} [options.fullRow=false] - 訊息在橫向 flex-wrap 或 grid 容器中獨佔一列
 * @param {boolean} [options.display=true] - false 時只標示狀態、不產生文字
 * @returns {object} owner
 */
export function setFieldError(owner, message, {
    target,
    visual,
    container,
    after,
    fullRow = false,
    display = true,
} = {}) {
    if (!owner) return owner;
    const text = message === null || message === undefined ? '' : String(message);
    if (!text) return clearFieldError(owner);

    const targets = toList(target);
    if (!targets.length) return owner;
    const visuals = visual === undefined ? targets : toList(visual);

    const entry = state(owner);
    mark(entry, targets, visuals);

    if (display === false) {
        removeMessage(entry);
        return owner;
    }

    const placed = placeMessage(entry, {
        container: container || targets[0].parentElement,
        after,
        fullRow,
    });
    entry.messageEl.textContent = text;
    targets.forEach(t => (placed ? addDescribedBy(t, entry.messageEl.id) : removeDescribedBy(t, entry.messageEl.id)));
    return owner;
}

/** 清除 setFieldError 產生的狀態與文字。 */
export function clearFieldError(owner) {
    const entry = owner?.__fieldError;
    if (!entry) return owner;
    removeMessage(entry);
    unmark(entry);
    return owner;
}

/** 目前顯示中的錯誤訊息；沒有錯誤或只標示狀態時為空字串。 */
export function getFieldError(owner) {
    const el = owner?.__fieldError?.messageEl;
    return el?.isConnected ? el.textContent : '';
}

/** 是否處於錯誤狀態（含 display:false）。 */
export function hasFieldError(owner) {
    return Boolean(owner?.__fieldError?.targets.length);
}
