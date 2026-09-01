import assert from 'node:assert/strict';
import test from 'node:test';
import { DeferredHydrationQueue, scheduleDeferredHydration } from './DeferredHydration.js';
import { DynamicListRenderer } from './DynamicListRenderer.js';

test('deferred hydration does not start before mount', async () => {
    const queue = new DeferredHydrationQueue();
    let calls = 0;
    const handle = queue.defer(async signal => {
        assert.equal(signal.aborted, false);
        calls += 1;
        return 'hydrated';
    });

    await new Promise(resolve => setTimeout(resolve, 5));
    assert.equal(calls, 0);

    queue.markMounted();
    assert.equal(await handle.promise, 'hydrated');
    assert.equal(calls, 1);
    queue.destroy();
});

test('destroy cancels queued hydration and aborts its signal', async () => {
    const queue = new DeferredHydrationQueue();
    let calls = 0;
    const handle = queue.defer(() => { calls += 1; });

    queue.destroy();
    await handle.promise;
    await new Promise(resolve => setTimeout(resolve, 5));

    assert.equal(calls, 0);
    assert.equal(handle.signal.aborted, true);
});

test('mounted convenience schedules work and reports optional errors', async () => {
    const expected = new Error('optional lookup failed');
    let observed = null;
    const handle = scheduleDeferredHydration(() => { throw expected; }, {
        onError: error => { observed = error; },
    });

    assert.equal(await handle.promise, undefined);
    assert.equal(observed, expected);
    handle.cancel();
});

test('DynamicListRenderer owns mount and destroy hydration lifecycle', async () => {
    const renderer = new DynamicListRenderer();
    renderer.element = {
        parentNode: null,
        remove() { this.parentNode = null; },
        querySelector() { return null; },
    };
    const host = {
        appendChild(element) { element.parentNode = this; },
    };
    let calls = 0;
    const handle = renderer.deferHydration(() => { calls += 1; });

    await new Promise(resolve => setTimeout(resolve, 5));
    assert.equal(calls, 0);
    renderer.mount(host);
    await handle.promise;
    assert.equal(calls, 1);

    const cancelled = renderer.deferHydration(() => { calls += 10; });
    renderer.destroy();
    await cancelled.promise;
    assert.equal(cancelled.signal.aborted, true);
    assert.equal(calls, 1);
});
