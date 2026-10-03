import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import Locale from '../../ui_components/i18n/index.js';
import { Countdown as BaseCountdown } from '../../ui_components/common/Countdown/Countdown.js';

// 記下每個實例，測試失敗提早結束時仍在 afterEach 銷毀
const live = new Set();
class Countdown extends BaseCountdown {
    constructor(options) {
        super(options);
        live.add(this);
    }
}

/** 整秒的基準時間 */
const T0 = Date.UTC(2026, 0, 1, 0, 0, 0);
const duration = (days, hours, minutes, seconds) => (((days * 24 + hours) * 60 + minutes) * 60 + seconds) * 1000;
const displayOf = (countdown) => countdown.element.querySelector('.countdown__display').textContent;

function setHidden(hidden) {
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => hidden });
    document.dispatchEvent(new Event('visibilitychange'));
}

function netListeners(addSpy, removeSpy, type) {
    const added = addSpy.mock.calls.filter(([name]) => name === type).length;
    const removed = removeSpy.mock.calls.filter(([name]) => name === type).length;
    return added - removed;
}

async function flush() {
    for (let i = 0; i < 5; i += 1) await Promise.resolve();
}

describe('Countdown', () => {
    let host;

    beforeEach(() => {
        vi.useFakeTimers();
        vi.setSystemTime(T0);
        Locale.setLang('zh-TW');
        host = document.createElement('div');
        document.body.appendChild(host);
    });

    afterEach(() => {
        live.forEach((countdown) => countdown.destroy());
        live.clear();
        host.remove();
        delete document.hidden;
        Locale.setLang('zh-TW');
        vi.useRealTimers();
        vi.restoreAllMocks();
    });

    it('has documented defaults and exposes a timer role plus a separate live region', () => {
        const countdown = new Countdown({ target: T0 + 5000 }).mount(host);
        expect(countdown.options).toMatchObject({
            serverOffsetMs: 0,
            format: 'auto',
            thresholds: [],
            completedText: null,
            autoStart: true,
            ariaLive: 'polite',
        });
        // 預設時鐘即系統時間（fake timers 下為 T0）
        expect(countdown.options.now()).toBe(Date.now());
        expect(countdown.element.querySelector('[role="timer"]')).not.toBeNull();
        const region = countdown.element.querySelector('.countdown__live');
        expect(region.getAttribute('aria-live')).toBe('polite');
        expect(region.getAttribute('aria-atomic')).toBe('true');
        expect(countdown.element.dataset.state).toBe('running');
        expect(displayOf(countdown)).toBe('5 秒');
    });

    it('formats remaining time: auto hides leading zero units; dhms, hms and custom formats', () => {
        const countdown = new Countdown({ target: T0 + duration(1, 2, 3, 4), autoStart: false }).mount(host);
        expect(displayOf(countdown)).toBe('1 天 02 小時 03 分 04 秒');
        countdown.setTarget(T0 + duration(0, 2, 0, 4));
        expect(displayOf(countdown)).toBe('2 小時 00 分 04 秒');
        countdown.setTarget(T0 + duration(0, 0, 5, 0));
        expect(displayOf(countdown)).toBe('5 分 00 秒');
        countdown.setTarget(T0 + 4000);
        expect(displayOf(countdown)).toBe('4 秒');

        const dhms = new Countdown({ target: T0 + duration(0, 2, 3, 4), format: 'dhms', autoStart: false }).mount(host);
        expect(displayOf(dhms)).toBe('0 天 02 小時 03 分 04 秒');

        const hms = new Countdown({ target: T0 + duration(1, 2, 3, 4), format: 'hms', autoStart: false }).mount(host);
        expect(displayOf(hms)).toBe('26:03:04');

        const custom = new Countdown({
            target: T0 + duration(0, 0, 3, 4),
            format: (parts) => `${parts.minutes}m${parts.seconds}s left`,
            autoStart: false,
        }).mount(host);
        expect(displayOf(custom)).toBe('3m4s left');
    });

    it('accepts Date, ISO strings and epoch ms; getRemaining() never goes negative', () => {
        expect(new Countdown({ target: new Date(T0 + 3000), autoStart: false }).getRemaining()).toBe(3000);
        expect(new Countdown({ target: new Date(T0 + 3000).toISOString(), autoStart: false }).getRemaining()).toBe(3000);

        const countdown = new Countdown({ target: T0 + 1500, autoStart: false });
        expect(countdown.getRemaining()).toBe(1500);
        // 無條件進位：剩 1.5 秒顯示 2 秒
        expect(countdown.getParts()).toEqual({ days: 0, hours: 0, minutes: 0, seconds: 2, totalMs: 1500 });
        countdown.setTarget(T0 - 5000);
        expect(countdown.getRemaining()).toBe(0);
        expect(countdown.getParts()).toEqual({ days: 0, hours: 0, minutes: 0, seconds: 0, totalMs: 0 });

        const invalid = new Countdown({ target: 'not a date' }).mount(host);
        expect(displayOf(invalid)).toBe('—');
        expect(invalid.getRemaining()).toBe(0);
        expect(invalid.element.dataset.state).toBe('stopped');
    });

    it('applies serverOffsetMs to an injected clock', () => {
        let clock = T0;
        const countdown = new Countdown({ target: T0 + 60_000, now: () => clock, serverOffsetMs: 5_000, autoStart: false });
        expect(countdown.getRemaining()).toBe(55_000);
        clock += 10_000;
        expect(countdown.getRemaining()).toBe(45_000);

        const behind = new Countdown({ target: T0 + 60_000, now: () => new Date(T0), serverOffsetMs: -2_000, autoStart: false });
        expect(behind.getRemaining()).toBe(62_000);
    });

    it('ticks on whole-second boundaries and corrects a late timer instead of drifting', () => {
        vi.setSystemTime(T0 + 250);
        const onTick = vi.fn();
        const timeouts = vi.spyOn(globalThis, 'setTimeout');
        const tickDelays = () => timeouts.mock.calls.map(([, delay]) => delay).filter((delay) => delay !== 100);
        const countdown = new Countdown({ target: T0 + 10_000, onTick }).mount(host);

        expect(onTick).toHaveBeenCalledTimes(1);
        expect(onTick.mock.calls[0][0]).toEqual({ days: 0, hours: 0, minutes: 0, seconds: 10, totalMs: 9750 });
        expect(tickDelays().at(-1)).toBe(750);

        vi.advanceTimersByTime(750);
        expect(displayOf(countdown)).toBe('9 秒');
        expect(onTick).toHaveBeenCalledTimes(2);
        expect(tickDelays().at(-1)).toBe(1000);

        // 主執行緒忙碌讓計時器晚了 300ms：下一次延遲縮短補回，而不是持續落後
        vi.setSystemTime(T0 + 1300);
        vi.advanceTimersByTime(1000);
        expect(displayOf(countdown)).toBe('8 秒');
        expect(tickDelays().at(-1)).toBe(700);
        vi.advanceTimersByTime(700);
        expect(displayOf(countdown)).toBe('7 秒');
        expect(onTick).toHaveBeenCalledTimes(4);
    });

    it('switches the variant and fires each threshold exactly once', () => {
        const onThreshold = vi.fn();
        const thresholds = [
            { seconds: 5, variant: 'warning' },
            { seconds: 2, variant: 'danger' },
            { seconds: 8, variant: 'info' },
        ];
        const countdown = new Countdown({ target: T0 + 10_000, thresholds, onThreshold }).mount(host);
        expect(countdown.element.dataset.variant).toBe('default');

        vi.advanceTimersByTime(2000);
        expect(onThreshold).toHaveBeenCalledTimes(1);
        expect(onThreshold).toHaveBeenLastCalledWith(thresholds[2]);
        expect(countdown.element.dataset.variant).toBe('info');
        expect(countdown.element.style.color).toBe('var(--cl-info)');

        vi.advanceTimersByTime(3000);
        expect(onThreshold).toHaveBeenCalledTimes(2);
        expect(onThreshold).toHaveBeenLastCalledWith(thresholds[0]);
        expect(countdown.element.dataset.variant).toBe('warning');

        vi.advanceTimersByTime(3000);
        expect(onThreshold).toHaveBeenCalledTimes(3);
        expect(onThreshold).toHaveBeenLastCalledWith(thresholds[1]);
        expect(countdown.element.dataset.variant).toBe('danger');
        expect(countdown.element.style.color).toBe('var(--cl-danger)');

        vi.advanceTimersByTime(5000);
        expect(onThreshold).toHaveBeenCalledTimes(3);
        expect(countdown.element.dataset.state).toBe('completed');

        // 一開始就落在門檻內：依大到小各觸發一次
        const late = vi.fn();
        const inside = new Countdown({ target: Date.now() + 1500, thresholds, onThreshold: late }).mount(host);
        expect(late.mock.calls.map(([threshold]) => threshold.seconds)).toEqual([8, 5, 2]);
        expect(inside.element.dataset.variant).toBe('danger');
        vi.advanceTimersByTime(5000);
        expect(late).toHaveBeenCalledTimes(3);
    });

    it('fires onComplete exactly once even when the tab slept past the target', () => {
        const onComplete = vi.fn();
        const onThreshold = vi.fn();
        const countdown = new Countdown({
            target: T0 + 10_000,
            thresholds: [{ seconds: 3, variant: 'danger' }],
            onComplete,
            onThreshold,
        }).mount(host);

        // 機器睡了一小時：時鐘跳過目標，逾期的計時器醒來後才執行
        vi.setSystemTime(T0 + 60 * 60 * 1000);
        vi.advanceTimersByTime(1000);
        expect(onThreshold).toHaveBeenCalledTimes(1);
        expect(onComplete).toHaveBeenCalledTimes(1);
        expect(displayOf(countdown)).toBe('時間到');
        expect(countdown.element.dataset.state).toBe('completed');
        expect(countdown.getRemaining()).toBe(0);

        document.dispatchEvent(new Event('visibilitychange'));
        countdown.start();
        vi.advanceTimersByTime(10_000);
        expect(onComplete).toHaveBeenCalledTimes(1);
        expect(vi.getTimerCount()).toBe(0);

        // 開始時已過目標：立即完成一次
        const past = vi.fn();
        new Countdown({ target: Date.now() - 1, onComplete: past }).mount(host);
        vi.advanceTimersByTime(5000);
        expect(past).toHaveBeenCalledTimes(1);
    });

    it('pauses while the document is hidden and resyncs immediately when visible again', () => {
        const docAdd = vi.spyOn(document, 'addEventListener');
        const docRemove = vi.spyOn(document, 'removeEventListener');
        const onTick = vi.fn();
        const onComplete = vi.fn();
        const countdown = new Countdown({ target: T0 + 5000, onTick, onComplete }).mount(host);
        expect(netListeners(docAdd, docRemove, 'visibilitychange')).toBe(1);

        vi.advanceTimersByTime(100);
        setHidden(true);
        expect(vi.getTimerCount()).toBe(0);
        vi.advanceTimersByTime(3000);
        expect(onTick).toHaveBeenCalledTimes(1);

        setHidden(false);
        expect(displayOf(countdown)).toBe('2 秒');
        expect(onTick).toHaveBeenCalledTimes(2);
        vi.advanceTimersByTime(2000);
        expect(onComplete).toHaveBeenCalledTimes(1);
        expect(netListeners(docAdd, docRemove, 'visibilitychange')).toBe(0);
    });

    it('announces only at start, at each threshold and at completion — never every second', async () => {
        const countdown = new Countdown({ target: T0 + 6000, thresholds: [{ seconds: 3, variant: 'warning' }] }).mount(host);
        const region = countdown.element.querySelector('.countdown__live');
        const announcements = [];
        const observer = new MutationObserver(() => announcements.push(region.textContent));
        observer.observe(region, { childList: true, characterData: true, subtree: true });

        const shown = new Set();
        for (let second = 0; second < 8; second += 1) {
            shown.add(displayOf(countdown));
            vi.advanceTimersByTime(1000);
            await flush();
        }
        observer.disconnect();
        expect(shown.size).toBeGreaterThanOrEqual(6);
        expect(announcements).toEqual(['剩餘 6 秒', '剩餘 3 秒', '時間到']);

        const urgent = new Countdown({ target: Date.now() + 2000, ariaLive: 'assertive' }).mount(host);
        expect(urgent.element.querySelector('.countdown__live').getAttribute('aria-live')).toBe('assertive');

        const silent = new Countdown({ target: Date.now() + 2000, ariaLive: 'off' }).mount(host);
        vi.advanceTimersByTime(5000);
        expect(silent.element.querySelector('.countdown__live').getAttribute('aria-live')).toBe('off');
        expect(silent.element.querySelector('.countdown__live').textContent).toBe('');
    });

    it('supports stop/start and setTarget, which resets thresholds and completion', () => {
        const onTick = vi.fn();
        const onThreshold = vi.fn();
        const onComplete = vi.fn();
        const countdown = new Countdown({
            target: T0 + 10_000,
            thresholds: [{ seconds: 30, variant: 'warning' }],
            autoStart: false,
            onTick,
            onThreshold,
            onComplete,
        }).mount(host);
        expect(countdown.element.dataset.state).toBe('stopped');
        vi.advanceTimersByTime(3000);
        expect(onTick).not.toHaveBeenCalled();

        countdown.start();
        expect(countdown.element.dataset.state).toBe('running');
        expect(displayOf(countdown)).toBe('7 秒');
        expect(onThreshold).toHaveBeenCalledTimes(1);

        countdown.stop();
        vi.advanceTimersByTime(3000);
        expect(onTick).toHaveBeenCalledTimes(1);
        expect(countdown.element.dataset.state).toBe('stopped');
        countdown.start();
        expect(displayOf(countdown)).toBe('4 秒');
        expect(onThreshold).toHaveBeenCalledTimes(1);

        vi.advanceTimersByTime(4000);
        expect(onComplete).toHaveBeenCalledTimes(1);

        // 已完成後換目標：重新開始，門檻與完成可以再觸發
        countdown.setTarget(Date.now() + 60_000);
        expect(countdown.element.dataset.state).toBe('running');
        expect(displayOf(countdown)).toBe('1 分 00 秒');
        vi.advanceTimersByTime(30_000);
        expect(onThreshold).toHaveBeenCalledTimes(2);
        vi.advanceTimersByTime(30_000);
        expect(onComplete).toHaveBeenCalledTimes(2);
    });

    it('keeps running when a callback throws', () => {
        const errors = vi.spyOn(console, 'error').mockImplementation(() => {});
        const onComplete = vi.fn();
        const onTick = vi.fn(() => {
            throw new Error('tick failed');
        });
        new Countdown({ target: T0 + 3000, onTick, onComplete }).mount(host);
        vi.advanceTimersByTime(3000);
        expect(onComplete).toHaveBeenCalledTimes(1);
        expect(errors).toHaveBeenCalled();
    });

    it('destroy() clears timers and the visibility listener and leaves no DOM', () => {
        const docAdd = vi.spyOn(document, 'addEventListener');
        const docRemove = vi.spyOn(document, 'removeEventListener');
        const winAdd = vi.spyOn(window, 'addEventListener');
        const onTick = vi.fn();
        const countdown = new Countdown({ target: T0 + 10_000, onTick }).mount(host);
        // 一個倒數計時器 + 一個延後寫入的開始公告
        expect(vi.getTimerCount()).toBe(2);
        expect(winAdd).not.toHaveBeenCalled();

        countdown.destroy();
        expect(vi.getTimerCount()).toBe(0);
        expect(host.childElementCount).toBe(0);
        expect(netListeners(docAdd, docRemove, 'visibilitychange')).toBe(0);
        vi.advanceTimersByTime(20_000);
        expect(onTick).toHaveBeenCalledTimes(1);

        expect(() => {
            countdown.destroy();
            countdown.start();
            countdown.stop();
            countdown.setTarget(T0 + 1000);
            countdown.getParts();
            countdown.mount(host);
        }).not.toThrow();
        expect(host.childElementCount).toBe(0);
        expect(vi.getTimerCount()).toBe(0);
    });

    it('follows Locale switching for units and the default completed text', () => {
        Locale.setLang('en');
        const countdown = new Countdown({ target: T0 + duration(0, 1, 2, 3), autoStart: false }).mount(host);
        expect(displayOf(countdown)).toBe('1h 02m 03s');

        Locale.setLang('zh-TW');
        countdown.setTarget(T0 + 2000);
        expect(displayOf(countdown)).toBe('2 秒');

        Locale.setLang('en');
        countdown.start();
        vi.advanceTimersByTime(2000);
        expect(displayOf(countdown)).toBe("Time's up");

        const custom = new Countdown({ target: T0, completedText: 'Closed' }).mount(host);
        expect(displayOf(custom)).toBe('Closed');
    });
});
