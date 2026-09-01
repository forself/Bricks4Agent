import assert from 'node:assert/strict';
import test from 'node:test';

import { createLazyModuleLoader, resolveLazyModuleValue } from './LazyModuleLoader.js';

test('createLazyModuleLoader deduplicates concurrent imports and caches the export', async () => {
    let imports = 0;
    class Page {}
    const load = createLazyModuleLoader(async () => {
        imports += 1;
        await Promise.resolve();
        return { Page };
    }, { exportName: 'Page' });

    const [first, second] = await Promise.all([load(), load.preload()]);
    assert.equal(first, Page);
    assert.equal(second, Page);
    assert.equal(await load(), Page);
    assert.equal(load.peek(), Page);
    assert.equal(imports, 1);
});

test('createLazyModuleLoader retries after a rejected import', async () => {
    let imports = 0;
    const load = createLazyModuleLoader(async () => {
        imports += 1;
        if (imports === 1) throw new Error('temporary failure');
        return { default: 'ready' };
    });

    await assert.rejects(load(), /temporary failure/);
    assert.equal(await load(), 'ready');
    assert.equal(imports, 2);
});

test('createLazyModuleLoader rejects missing exports with a useful label', async () => {
    const load = createLazyModuleLoader(async () => ({}), {
        exportName: 'RoutePage',
        label: 'route page class',
    });
    await assert.rejects(load(), /route page class/);
});

test('resolveLazyModuleValue only invokes branded lazy loaders', async () => {
    class ExistingPage {}
    const load = createLazyModuleLoader(async () => ({ ExistingPage }), {
        exportName: 'ExistingPage',
    });
    assert.equal(await resolveLazyModuleValue(load), ExistingPage);
    assert.equal(await resolveLazyModuleValue(ExistingPage), ExistingPage);
});
