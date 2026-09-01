/**
 * Mount-aware deferred work for generated renderers.
 *
 * Renderers register optional/reference-data hydration during init(), but the
 * queue never starts it until the renderer has actually mounted.  This keeps
 * first paint independent from non-structural network work and centralises
 * cancellation when a route destroys the renderer.
 */
export class DeferredHydrationQueue {
    constructor() {
        this._mounted = false;
        this._destroyed = false;
        this._records = new Set();
    }

    /**
     * @param {(signal: AbortSignal) => unknown|Promise<unknown>} task
     * @param {{ onError?: (error: unknown) => void }} options
     * @returns {{ promise: Promise<unknown>, cancel: () => void, signal: AbortSignal }}
     */
    defer(task, { onError } = {}) {
        if (typeof task !== 'function') {
            throw new TypeError('Deferred hydration task is required.');
        }
        if (this._destroyed) {
            throw new Error('A destroyed hydration queue cannot accept new tasks.');
        }

        const controller = new AbortController();
        let settle;
        const promise = new Promise(resolve => { settle = resolve; });
        const record = {
            task,
            onError,
            controller,
            promise,
            settle,
            settled: false,
            timer: null,
            state: 'pending',
        };
        const cancel = () => this._cancel(record);
        this._records.add(record);
        if (this._mounted) this._schedule(record);

        return { promise, cancel, signal: controller.signal };
    }

    markMounted() {
        if (this._destroyed || this._mounted) return;
        this._mounted = true;
        for (const record of this._records) this._schedule(record);
    }

    destroy() {
        if (this._destroyed) return;
        this._destroyed = true;
        this._mounted = false;
        for (const record of [...this._records]) this._cancel(record);
    }

    _schedule(record) {
        if (record.state !== 'pending' || this._destroyed) return;
        record.state = 'scheduled';
        record.timer = globalThis.setTimeout(async () => {
            record.timer = null;
            if (record.state === 'cancelled' || this._destroyed) return;
            record.state = 'running';
            try {
                const result = await record.task(record.controller.signal);
                this._settle(record, result);
            } catch (error) {
                if (!record.controller.signal.aborted) {
                    if (typeof record.onError === 'function') record.onError(error);
                    else console.error('[DeferredHydration] task failed.', error);
                }
                this._settle(record, undefined);
            }
        }, 0);
    }

    _cancel(record) {
        if (!record || record.settled) return;
        record.state = 'cancelled';
        if (record.timer !== null) globalThis.clearTimeout(record.timer);
        record.timer = null;
        record.controller.abort();
        this._settle(record, undefined);
    }

    _settle(record, value) {
        if (record.settled) return;
        record.settled = true;
        this._records.delete(record);
        record.settle(value);
    }
}

/**
 * Convenience for a component whose DOM has already mounted (for example a
 * BasePage.onMounted hook).  Generated renderers normally use their built-in
 * deferHydration() method instead.
 */
export function scheduleDeferredHydration(task, options) {
    const queue = new DeferredHydrationQueue();
    const handle = queue.defer(task, options);
    queue.markMounted();
    return {
        promise: handle.promise,
        signal: handle.signal,
        cancel() { queue.destroy(); },
    };
}

export default DeferredHydrationQueue;
