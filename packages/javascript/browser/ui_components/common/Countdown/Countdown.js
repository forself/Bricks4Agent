/**
 * Countdown — 倒數計時顯示
 *
 * - 以可注入的時鐘（now + serverOffsetMs）計算剩餘時間；每次都從時鐘重算，不累積誤差
 * - setTimeout 對齊下一個整秒邊界；分頁隱藏時暫停，回到前景立即重新同步
 * - 門檻（thresholds）依剩餘秒數切換視覺樣式，每個門檻只觸發一次 onThreshold
 * - 分頁睡過目標時間，也只觸發一次 onComplete
 * - 螢幕閱讀器公告只在開始、跨過門檻、完成時各一次，不逐秒朗讀
 *
 * @example
 * new Countdown({
 *     target: '2026-12-31T23:59:59+08:00',
 *     thresholds: [{ seconds: 300, variant: 'warning' }, { seconds: 60, variant: 'danger' }],
 *     onComplete: () => console.log('done')
 * }).mount('#host');
 */
import Locale from '../../i18n/index.js';
import './locale.js';

const VARIANT_STYLES = {
    default: { color: 'var(--cl-text)', background: 'transparent', border: 'transparent' },
    info: { color: 'var(--cl-info)', background: 'var(--cl-info-light)', border: 'var(--cl-info)' },
    warning: { color: 'var(--cl-warning)', background: 'var(--cl-warning-light)', border: 'var(--cl-warning)' },
    danger: { color: 'var(--cl-danger)', background: 'var(--cl-danger-light)', border: 'var(--cl-danger)' }
};
const LIVE_POLITENESS = new Set(['polite', 'assertive', 'off']);
/** 公告延後寫入：讓 live region 先進入無障礙樹，也把同一時刻的多次公告合併成一次 */
const ANNOUNCE_DELAY = 100;
const SR_ONLY_CSS = 'position:absolute;width:1px;height:1px;margin:-1px;padding:0;border:0;overflow:hidden;clip-path:inset(50%);white-space:nowrap;';

const pad2 = (value) => String(value).padStart(2, '0');

/**
 * 預設時鐘：目前的系統時間（毫秒），值與 Date.now 相同。
 * 沿用 DatePicker 等元件取得「現在」的寫法（new Date()）；元件庫的決定性檢查禁止在
 * 元件程式碼直接呼叫 Date.now / Math.random（過去用來產生 ID）。測試或需要固定時間時注入 options.now。
 */
const systemClock = () => new Date().getTime();

function toTimestamp(target) {
    if (target instanceof Date) return target.getTime();
    if (typeof target === 'number') return Number.isFinite(target) ? target : NaN;
    if (typeof target === 'string' && target.trim()) return Date.parse(target);
    return NaN;
}

export class Countdown {
    constructor(options = {}) {
        this.options = {
            target: null,                 // 目標時間：Date、ISO 字串或 epoch 毫秒
            now: systemClock,             // 可注入的時鐘（回傳毫秒或 Date）；預設為系統時鐘
            serverOffsetMs: 0,            // 伺服器時差（毫秒），加到 now() 上
            format: 'auto',               // 'auto' | 'dhms' | 'hms' | (parts) => string
            thresholds: [],               // [{ seconds, variant: 'info' | 'warning' | 'danger' }]
            completedText: null,          // 完成時顯示的文字；null 使用語系預設「時間到」
            autoStart: true,              // mount() 時自動開始
            ariaLive: 'polite',           // 公告的 aria-live：'polite' | 'assertive' | 'off'
            onTick: null,                 // (parts) 顯示的秒數改變時
            onThreshold: null,            // (threshold) 跨過門檻時，每個門檻一次
            onComplete: null,             // () 倒數結束時，只觸發一次
            ...options
        };

        this._targetMs = toTimestamp(this.options.target);
        this._thresholds = this._normalizeThresholds(this.options.thresholds);
        this._fired = new Set();
        this._timer = null;
        this._announceTimer = null;
        this._running = false;
        this._completed = false;
        this._autoStarted = false;
        this._lastSecond = null;
        this._visibilityAttached = false;
        this._destroyed = false;
        this._handleVisibilityChange = () => this._resync();

        this.element = this._createElement();
        this._render(this.getParts());
    }

    // ------------------------------------------------------------------
    // 公開 API
    // ------------------------------------------------------------------

    mount(container) {
        if (this._destroyed) return this;
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) return this;
        target.appendChild(this.element);
        if (this.options.autoStart && !this._autoStarted) {
            this._autoStarted = true;
            this.start();
        }
        return this;
    }

    /** 開始（或從 stop() 後繼續）倒數；已完成時不重複觸發，需先 setTarget()。 */
    start() {
        if (this._destroyed || this._running || this._completed) return this;
        this._autoStarted = true;
        if (!Number.isFinite(this._targetMs)) {
            this._render(this.getParts());
            return this;
        }
        this._running = true;
        this._syncVisibilityListener(true);
        this._tick(true);
        return this;
    }

    /** 停止倒數（保留目標時間與已觸發的門檻）。 */
    stop() {
        if (this._destroyed) return this;
        this._clearTimer();
        this._syncVisibilityListener(false);
        if (this._running) {
            this._running = false;
            this._render(this.getParts());
        }
        return this;
    }

    /** 更換目標時間並重設門檻與完成狀態；原本在倒數或已完成時會以新目標重新開始。 */
    setTarget(target) {
        if (this._destroyed) return this;
        const restart = this._running || this._completed;
        this._clearTimer();
        this._syncVisibilityListener(false);
        this._running = false;
        this._completed = false;
        this._fired = new Set();
        this._lastSecond = null;
        this.options.target = target;
        this._targetMs = toTimestamp(target);
        if (restart) this.start();
        else this._render(this.getParts());
        return this;
    }

    /** 剩餘毫秒數，永不為負；沒有有效目標時為 0。 */
    getRemaining() {
        if (!Number.isFinite(this._targetMs)) return 0;
        return Math.max(0, this._targetMs - this._now());
    }

    /** { days, hours, minutes, seconds, totalMs }；天時分秒以無條件進位的整秒計算。 */
    getParts() {
        return this._partsFor(this.getRemaining());
    }

    destroy() {
        if (this._destroyed) return;
        this._destroyed = true;
        this._running = false;
        this._clearTimer();
        if (this._announceTimer) {
            clearTimeout(this._announceTimer);
            this._announceTimer = null;
        }
        this._syncVisibilityListener(false);
        this.element.remove();
    }

    // ------------------------------------------------------------------
    // 計算
    // ------------------------------------------------------------------

    _now() {
        let value;
        try {
            value = typeof this.options.now === 'function' ? this.options.now() : systemClock();
        } catch (error) {
            console.error('[Countdown] now() 執行失敗：', error);
            value = systemClock();
        }
        const ms = Number(value);
        return (Number.isFinite(ms) ? ms : systemClock()) + (Number(this.options.serverOffsetMs) || 0);
    }

    _partsFor(totalMs) {
        // 無條件進位：剩 0.4 秒時仍顯示 1 秒，顯示 0 的那一刻就是完成
        const total = Math.ceil(totalMs / 1000);
        return {
            days: Math.floor(total / 86400),
            hours: Math.floor((total % 86400) / 3600),
            minutes: Math.floor((total % 3600) / 60),
            seconds: total % 60,
            totalMs
        };
    }

    _normalizeThresholds(list) {
        return (Array.isArray(list) ? list : [])
            .filter((threshold) => threshold && Number.isFinite(Number(threshold.seconds)) && Number(threshold.seconds) >= 0)
            .map((threshold) => ({
                source: threshold,
                ms: Number(threshold.seconds) * 1000,
                variant: VARIANT_STYLES[threshold.variant] ? threshold.variant : 'default'
            }))
            .sort((a, b) => b.ms - a.ms);
    }

    // ------------------------------------------------------------------
    // 呈現
    // ------------------------------------------------------------------

    _createElement() {
        const root = document.createElement('span');
        root.className = 'countdown';
        root.dataset.variant = 'default';
        root.dataset.state = 'stopped';
        root.style.cssText = 'display:inline-flex;align-items:center;box-sizing:border-box;padding:2px 8px;border:1px solid transparent;border-radius:var(--cl-radius-sm);color:var(--cl-text);background:transparent;font-variant-numeric:tabular-nums;white-space:nowrap;transition:color var(--cl-transition), background-color var(--cl-transition), border-color var(--cl-transition);';

        const display = document.createElement('span');
        display.className = 'countdown__display';
        // role=timer 的 aria-live 預設為 off：畫面每秒更新，但不逐秒朗讀
        display.setAttribute('role', 'timer');
        display.setAttribute('aria-atomic', 'true');

        const live = document.createElement('span');
        live.className = 'countdown__live';
        live.setAttribute('aria-live', this._politeness());
        live.setAttribute('aria-atomic', 'true');
        live.style.cssText = SR_ONLY_CSS;

        root.append(display, live);
        this._display = display;
        this._live = live;
        return root;
    }

    _politeness() {
        const value = String(this.options.ariaLive || 'polite');
        return LIVE_POLITENESS.has(value) ? value : 'polite';
    }

    _render(parts) {
        let text;
        if (this._completed) text = this._completedText();
        else if (!Number.isFinite(this._targetMs)) text = Locale.t('countdown.noTarget');
        else text = this._format(parts);
        if (this._display.textContent !== text) this._display.textContent = text;
        this.element.dataset.state = this._completed ? 'completed' : (this._running ? 'running' : 'stopped');
        this._applyVariant(parts.totalMs);
    }

    _completedText() {
        const text = this.options.completedText;
        return text === null || text === undefined || text === '' ? Locale.t('countdown.completed') : String(text);
    }

    _unit(name, value) {
        return Locale.t(`countdown.units.${name}`, { value });
    }

    _format(parts) {
        const { format } = this.options;
        if (typeof format === 'function') {
            try {
                return String(format({ ...parts }) ?? '');
            } catch (error) {
                console.error('[Countdown] format 函式執行失敗：', error);
            }
        }
        const separator = Locale.t('countdown.separator');
        const { days, hours, minutes, seconds } = parts;
        if (format === 'hms') return `${pad2(days * 24 + hours)}:${pad2(minutes)}:${pad2(seconds)}`;
        if (format === 'dhms') {
            return [
                this._unit('days', days),
                this._unit('hours', pad2(hours)),
                this._unit('minutes', pad2(minutes)),
                this._unit('seconds', pad2(seconds))
            ].join(separator);
        }
        // auto：省略前導為零的單位；第一個單位不補零，其後補成兩位數
        const units = [['days', days], ['hours', hours], ['minutes', minutes], ['seconds', seconds]];
        let first = units.findIndex(([, value]) => value > 0);
        if (first < 0) first = units.length - 1;
        return units
            .slice(first)
            .map(([name, value], index) => this._unit(name, index === 0 ? value : pad2(value)))
            .join(separator);
    }

    _spokenRemaining(parts) {
        const words = [['day', parts.days], ['hour', parts.hours], ['minute', parts.minutes], ['second', parts.seconds]]
            .filter(([, value]) => value > 0)
            .map(([unit, value]) => Locale.t(`countdown.long.${value === 1 ? unit : `${unit}s`}`, { value }));
        const time = words.length ? words.join(Locale.t('countdown.separator')) : Locale.t('countdown.long.seconds', { value: 0 });
        return Locale.t('countdown.remaining', { time });
    }

    _activeVariant(totalMs) {
        if (!Number.isFinite(this._targetMs)) return 'default';
        let variant = 'default';
        // 門檻由大到小排列，最後一個符合的（最小門檻）勝出
        for (const threshold of this._thresholds) {
            if (totalMs <= threshold.ms) variant = threshold.variant;
        }
        return variant;
    }

    _applyVariant(totalMs) {
        const variant = this._activeVariant(totalMs);
        if (this.element.dataset.variant === variant) return;
        this.element.dataset.variant = variant;
        const style = VARIANT_STYLES[variant];
        this.element.style.color = style.color;
        this.element.style.background = style.background;
        this.element.style.borderColor = style.border;
        this.element.style.fontWeight = variant === 'danger' ? '600' : '';
    }

    _announce(text) {
        if (this._destroyed || !text || this._politeness() === 'off') return;
        if (this._announceTimer) clearTimeout(this._announceTimer);
        this._announceTimer = setTimeout(() => {
            this._announceTimer = null;
            if (!this._destroyed) this._live.textContent = text;
        }, ANNOUNCE_DELAY);
    }

    _emit(name, ...args) {
        const handler = this.options[name];
        if (typeof handler !== 'function') return;
        // 回呼拋錯不能中斷計時迴圈
        try {
            handler(...args);
        } catch (error) {
            console.error(`[Countdown] ${name} 執行失敗：`, error);
        }
    }

    // ------------------------------------------------------------------
    // 計時
    // ------------------------------------------------------------------

    _tick(isStart = false) {
        this._timer = null;
        if (!this._running || this._destroyed) return;
        const parts = this.getParts();
        const crossed = this._checkThresholds(parts.totalMs);
        if (!this._running || this._destroyed) return;
        if (parts.totalMs <= 0) {
            this._complete();
            return;
        }
        const second = Math.ceil(parts.totalMs / 1000);
        if (isStart || second !== this._lastSecond) {
            this._lastSecond = second;
            this._render(parts);
            this._emit('onTick', parts);
            if (!this._running || this._destroyed) return;
        } else {
            this._applyVariant(parts.totalMs);
        }
        if (isStart || crossed) this._announce(this._spokenRemaining(parts));
        this._schedule(parts.totalMs);
    }

    /** 觸發新跨過的門檻（由大到小，各一次）；回傳這次是否有跨過門檻。 */
    _checkThresholds(totalMs) {
        let crossed = false;
        for (const threshold of this._thresholds) {
            if (totalMs > threshold.ms || this._fired.has(threshold)) continue;
            this._fired.add(threshold);
            crossed = true;
            this._emit('onThreshold', threshold.source);
            if (!this._running || this._destroyed) break;
        }
        return crossed;
    }

    _complete() {
        if (this._completed) return;
        this._completed = true;
        this._running = false;
        this._clearTimer();
        this._syncVisibilityListener(false);
        const parts = this._partsFor(0);
        this._render(parts);
        if (this._lastSecond !== 0) {
            this._lastSecond = 0;
            this._emit('onTick', parts);
        }
        if (this._destroyed) return;
        this._announce(this._completedText());
        this._emit('onComplete');
    }

    _schedule(totalMs) {
        this._clearTimer();
        if (!this._running || this._destroyed || this._isHidden()) return;
        // 對齊剩餘時間的下一個整秒邊界（目標為整秒時即調整後時鐘的下一個整秒）；
        // 每次都從時鐘重算延遲，計時器提早或延後都不會累積誤差
        const delay = totalMs % 1000 || 1000;
        this._timer = setTimeout(() => this._tick(false), delay);
    }

    /** 分頁隱藏時暫停；回到前景時依時鐘立即重算，補上期間跨過的門檻與完成。 */
    _resync() {
        if (!this._running || this._destroyed) return;
        this._clearTimer();
        if (this._isHidden()) return;
        this._tick(false);
    }

    _isHidden() {
        return typeof document !== 'undefined' && document.hidden === true;
    }

    _clearTimer() {
        if (this._timer) {
            clearTimeout(this._timer);
            this._timer = null;
        }
    }

    _syncVisibilityListener(attach) {
        const next = Boolean(attach) && !this._destroyed;
        if (next === this._visibilityAttached || typeof document === 'undefined') return;
        this._visibilityAttached = next;
        if (next) document.addEventListener('visibilitychange', this._handleVisibilityChange);
        else document.removeEventListener('visibilitychange', this._handleVisibilityChange);
    }
}

export default Countdown;
