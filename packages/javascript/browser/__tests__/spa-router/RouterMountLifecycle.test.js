import { afterEach, describe, expect, it, vi } from 'vitest';

import { Router as TemplateRouter } from '../../../../../templates/spa/frontend/core/Router.js';
import { Router as GeneratorRouter } from '../../../../../tools/spa-generator/frontend/core/Router.js';

const roots = [];

afterEach(() => {
    for (const root of roots.splice(0)) root.remove();
    vi.restoreAllMocks();
    history.replaceState(null, '', '/');
});

function createHarness(Router, routes) {
    const root = document.createElement('main');
    document.body.appendChild(root);
    roots.push(root);
    const router = new Router({
        mode: 'history',
        routes,
        layout: { getContentArea: () => root },
        store: { set: vi.fn() },
    });
    return { router, root };
}

function deferred() {
    let resolve;
    const promise = new Promise((done) => { resolve = done; });
    return { promise, resolve };
}

function lastLoading(router) {
    const calls = router.store.set.mock.calls.filter(([key]) => key === 'loading');
    return calls.length > 0 ? calls[calls.length - 1][1] : undefined;
}

function slowPageFixture() {
    const mount = deferred();
    const started = deferred();
    const lifecycle = [];
    class SlowPage {
        async mount(container) {
            started.resolve();
            await mount.promise;
            container.innerHTML = '<div>slow</div>';
            lifecycle.push('slow-mounted');
        }
        destroy() { lifecycle.push('slow-destroyed'); }
    }
    return { SlowPage, mount, started, lifecycle };
}

const routerVariants = [
    ['SPA template router', TemplateRouter],
    ['SPA generator router', GeneratorRouter],
];

describe.each(routerVariants)('%s mount lifecycle', (_label, Router) => {
    it('replaces the 404 view when a valid page is visited afterward', async () => {
        class Page {
            mount(container) { container.textContent = 'ready'; }
            destroy() {}
        }
        const { router, root } = createHarness(Router, [{ path: '/ready', component: Page, exact: true }]);
        vi.spyOn(console, 'warn').mockImplementation(() => {});
        history.replaceState(null, '', '/missing');
        await router._handleRouteChange();
        expect(root.querySelector('.page-404')).not.toBeNull();
        history.replaceState(null, '', '/ready');
        await router._handleRouteChange();
        expect(root.textContent).toBe('ready');
        expect(root.querySelector('.page-404')).toBeNull();
    });

    it('awaits mount and destroys a late page after a newer navigation wins', async () => {
        const slowMount = deferred();
        const slowStarted = deferred();
        const lifecycle = [];
        class SlowPage {
            async mount(container) {
                slowStarted.resolve();
                await slowMount.promise;
                container.innerHTML = '<div>slow</div>';
                this.element = container.firstElementChild;
                lifecycle.push('slow-mounted');
            }
            destroy() { lifecycle.push('slow-destroyed'); this.element?.remove(); }
        }
        class FastPage {
            async mount(container) {
                await Promise.resolve();
                container.innerHTML = '<div>fast</div>';
                this.element = container.firstElementChild;
                lifecycle.push('fast-mounted');
            }
            destroy() { lifecycle.push('fast-destroyed'); this.element?.remove(); }
        }
        const { router, root } = createHarness(Router, [
            { path: '/slow', component: SlowPage, exact: true },
            { path: '/fast', component: FastPage, exact: true },
        ]);

        history.replaceState(null, '', '/slow');
        const slowNavigation = router._handleRouteChange();
        await slowStarted.promise;
        history.replaceState(null, '', '/fast');
        await router._handleRouteChange();
        expect(root.textContent).toBe('fast');
        slowMount.resolve();
        await slowNavigation;

        expect(root.textContent).toBe('fast');
        expect(lastLoading(router)).toBe(false);
        expect(lifecycle).toEqual(['fast-mounted', 'slow-mounted', 'slow-destroyed']);
        expect(router.currentPage).toBeInstanceOf(FastPage);
        expect(router.currentRoute.path).toBe('/fast');
    });

    it('destroys a page and reports a mount rejection', async () => {
        const lifecycle = [];
        class BrokenPage {
            async mount() { throw new Error('mount failed'); }
            destroy() { lifecycle.push('destroyed'); }
        }
        const { router, root } = createHarness(Router, [
            { path: '/broken', component: BrokenPage, exact: true },
        ]);
        vi.spyOn(console, 'error').mockImplementation(() => {});

        history.replaceState(null, '', '/broken');
        await router._handleRouteChange();

        expect(lifecycle).toEqual(['destroyed']);
        expect(root.textContent).toContain('載入錯誤');
        expect(router.currentPage).toBeNull();
    });

    it('does not construct pages when the layout has no content area', async () => {
        let constructed = 0;
        class UnmountedPage {
            constructor() { constructed += 1; }
            mount() {}
            destroy() {}
        }
        const router = new Router({
            mode: 'history',
            routes: [{ path: '/empty', component: UnmountedPage, exact: true }],
            layout: { getContentArea: () => null },
        });
        history.replaceState(null, '', '/empty');

        await router._handleRouteChange();

        expect(constructed).toBe(0);
        expect(router.currentPage).toBeNull();
        expect(router.currentRoute.path).toBe('/empty');
    });

    it('keeps the connected mount container usable after mount resolves', async () => {
        let page;
        class RetainedContainerPage {
            constructor() { page = this; }
            async mount(container) {
                this.container = container;
                container.innerHTML = '<p>initial</p>';
            }
            destroy() { this.container?.remove(); }
        }
        const { router, root } = createHarness(Router, [
            { path: '/retained', component: RetainedContainerPage, exact: true },
        ]);
        history.replaceState(null, '', '/retained');

        await router._handleRouteChange();
        page.container.innerHTML = '<p>updated later</p>';

        expect(page.container.isConnected).toBe(true);
        expect(page.container.getAttribute('data-router-navigation-host')).toBeTruthy();
        expect(page.container.style.display).toBe('contents');
        expect(root.textContent).toBe('updated later');
    });

    it('destroys a nested child if navigation is cancelled while its parent mounts', async () => {
        const parentMount = deferred();
        const parentStarted = deferred();
        const lifecycle = [];
        class ParentPage {
            async mount(container) {
                parentStarted.resolve();
                await parentMount.promise;
                this.container = container;
                container.innerHTML = '<section><div data-router-outlet></div></section>';
                this.element = container.firstElementChild;
            }
            getRouterOutlet() { return this.container?.querySelector('[data-router-outlet]'); }
            destroy() { lifecycle.push('parent-destroyed'); this.element?.remove(); }
        }
        class ChildPage {
            constructor() { lifecycle.push('child-created'); }
            async mount(container) {
                this.container = container;
                container.innerHTML = '<div>child</div>';
            }
            destroy() { lifecycle.push('child-destroyed'); }
        }
        class FastPage {
            async mount(container) { container.innerHTML = '<div>fast</div>'; }
            destroy() {}
        }
        const { router, root } = createHarness(Router, [
            { path: '/parent', component: ParentPage, children: [{ path: '/child', component: ChildPage, exact: true }] },
            { path: '/fast', component: FastPage, exact: true },
        ]);

        history.replaceState(null, '', '/parent/child');
        const nestedNavigation = router._handleRouteChange();
        await parentStarted.promise;
        expect(lifecycle).toContain('child-created');
        history.replaceState(null, '', '/fast');
        await router._handleRouteChange();
        parentMount.resolve();
        await nestedNavigation;

        expect(lifecycle).toContain('child-destroyed');
        expect(lifecycle).toContain('parent-destroyed');
        expect(root.textContent).toBe('fast');
    });

    it('mounts nested parents and children into connected hosts retained for later updates', async () => {
        let parentPage;
        let childPage;
        class ParentPage {
            constructor() { parentPage = this; }
            async mount(container) {
                this.container = container;
                container.innerHTML = '<section><div data-router-outlet></div></section>';
            }
            getRouterOutlet() { return this.container.querySelector('[data-router-outlet]'); }
            destroy() { this.container.remove(); }
        }
        class ChildPage {
            constructor() { childPage = this; }
            async mount(container) {
                this.container = container;
                container.innerHTML = '<p>child initial</p>';
            }
            destroy() { this.container?.replaceChildren(); }
        }
        const { router, root } = createHarness(Router, [
            { path: '/parent', component: ParentPage, children: [{ path: '/child', component: ChildPage, exact: true }] },
        ]);
        history.replaceState(null, '', '/parent/child');

        await router._handleRouteChange();
        parentPage.container.insertBefore(document.createTextNode('parent updated '), parentPage.container.firstChild);
        childPage.container.innerHTML = '<p>child updated</p>';

        expect(parentPage.container.isConnected).toBe(true);
        expect(childPage.container.isConnected).toBe(true);
        expect(root.textContent).toContain('parent updated');
        expect(root.textContent).toContain('child updated');
    });

    it('keeps the visible page and a cleared loading state when a guard rejects the next navigation', async () => {
        class HomePage {
            mount(container) { container.textContent = 'home'; }
            destroy() {}
        }
        let blockedCreated = 0;
        class BlockedPage {
            constructor() { blockedCreated += 1; }
            mount(container) { container.textContent = 'blocked'; }
            destroy() {}
        }
        const { router, root } = createHarness(Router, [
            { path: '/home', component: HomePage, exact: true },
            { path: '/blocked', component: BlockedPage, exact: true, beforeEnter: () => false },
        ]);
        history.replaceState(null, '', '/home?tab=1');
        await router._handleRouteChange();

        history.replaceState(null, '', '/blocked?tab=2');
        await router._handleRouteChange();

        expect(blockedCreated).toBe(0);
        expect(root.textContent).toBe('home');
        expect(lastLoading(router)).toBe(false);
        expect(router.currentPage).toBeInstanceOf(HomePage);
        expect(router.getCurrentRoute()).toMatchObject({ path: '/home', query: { tab: '1' } });
    });

    it.each([
        ['a global beforeEach guard', (router) => router.beforeEach(async (to, _from, next) => {
            await Promise.resolve();
            if (to.path === '/blocked') next(false);
        }), undefined],
        ['a route beforeEnter guard', () => {}, async () => false],
    ])('lets an in-flight navigation finish when the next one is rejected by %s', async (_guardLabel, installGuard, beforeEnter) => {
        const slow = slowPageFixture();
        let blockedCreated = 0;
        class BlockedPage {
            constructor() { blockedCreated += 1; }
            mount(container) { container.textContent = 'blocked'; }
            destroy() {}
        }
        const { router, root } = createHarness(Router, [
            { path: '/slow', component: slow.SlowPage, exact: true },
            { path: '/blocked', component: BlockedPage, exact: true, beforeEnter },
        ]);
        installGuard(router);

        history.replaceState(null, '', '/slow');
        const slowNavigation = router._handleRouteChange();
        await slow.started.promise;
        history.replaceState(null, '', '/blocked');
        await router._handleRouteChange();

        // 被拒絕的導航不得拆掉仍在載入的頁面，也不得把 loading 卡在 true。
        expect(blockedCreated).toBe(0);
        expect(root.querySelector('[data-router-navigation-host]')).not.toBeNull();
        expect(lastLoading(router)).toBe(true);

        slow.mount.resolve();
        await slowNavigation;

        expect(root.textContent).toBe('slow');
        expect(slow.lifecycle).toEqual(['slow-mounted']);
        expect(lastLoading(router)).toBe(false);
        expect(router.currentPage).toBeInstanceOf(slow.SlowPage);
        expect(router.currentRoute.path).toBe('/slow');
    });

    it('builds an in-flight page from its own params when the next navigation is rejected', async () => {
        const lazyModule = deferred();
        const importStarted = deferred();
        const created = [];
        class UserPage {
            constructor(options) { created.push({ params: options.params, query: options.query }); }
            mount(container) { container.textContent = `user ${created[created.length - 1].params.id}`; }
            destroy() {}
        }
        const { router, root } = createHarness(Router, [
            { path: '/users/:id', component: () => { importStarted.resolve(); return lazyModule.promise; }, exact: true },
        ]);
        router.beforeEach((to, _from, next) => { if (to.params.id === '2') next(false); });

        history.replaceState(null, '', '/users/1?view=a');
        const firstNavigation = router._handleRouteChange();
        await importStarted.promise;
        history.replaceState(null, '', '/users/2?view=b');
        await router._handleRouteChange();
        lazyModule.resolve({ default: UserPage });
        await firstNavigation;

        expect(created).toEqual([{ params: { id: '1' }, query: { view: 'a' } }]);
        expect(root.textContent).toBe('user 1');
        expect(lastLoading(router)).toBe(false);
        expect(router.getCurrentRoute()).toMatchObject({ path: '/users/:id', params: { id: '1' }, query: { view: 'a' } });
    });

    it('shows the 404 view and clears loading when it supersedes an in-flight navigation', async () => {
        const slow = slowPageFixture();
        const { router, root } = createHarness(Router, [
            { path: '/slow', component: slow.SlowPage, exact: true },
        ]);
        vi.spyOn(console, 'warn').mockImplementation(() => {});

        history.replaceState(null, '', '/slow');
        const slowNavigation = router._handleRouteChange();
        await slow.started.promise;
        expect(lastLoading(router)).toBe(true);
        history.replaceState(null, '', '/missing');
        await router._handleRouteChange();

        expect(root.querySelector('.page-404')).not.toBeNull();
        expect(root.querySelector('[data-router-navigation-host]')).toBeNull();
        expect(lastLoading(router)).toBe(false);

        slow.mount.resolve();
        await slowNavigation;

        expect(root.querySelector('.page-404')).not.toBeNull();
        expect(root.textContent).not.toContain('slow');
        expect(slow.lifecycle).toEqual(['slow-mounted', 'slow-destroyed']);
        expect(lastLoading(router)).toBe(false);
        expect(router.currentPage).toBeNull();
    });

    it('escapes the 404 and error views and wires their buttons without inline handlers', async () => {
        class BrokenPage {
            async mount() { throw new Error('<img src=x onerror=alert(2)>'); }
            destroy() {}
        }
        const { router, root } = createHarness(Router, [
            { path: '/broken', component: BrokenPage, exact: true },
        ]);
        vi.spyOn(console, 'warn').mockImplementation(() => {});
        vi.spyOn(console, 'error').mockImplementation(() => {});
        const currentPath = vi.spyOn(router, '_getCurrentPath').mockReturnValue('/<img src=x onerror=alert(1)>');

        await router._handleRouteChange();

        expect(root.querySelector('img')).toBeNull();
        expect(root.querySelector('[onclick]')).toBeNull();
        expect(root.querySelector('.page-404 p').textContent).toContain('<img src=x onerror=alert(1)>');
        const navigate = vi.spyOn(router, 'navigate').mockImplementation(() => {});
        root.querySelector('[data-router-home]').click();
        expect(navigate).toHaveBeenCalledWith('/');

        currentPath.mockReturnValue('/broken');
        await router._handleRouteChange();

        expect(root.querySelector('img')).toBeNull();
        expect(root.querySelector('[onclick]')).toBeNull();
        expect(root.querySelector('.page-error p').textContent).toBe('<img src=x onerror=alert(2)>');
        expect(root.querySelector('[data-router-reload]')).not.toBeNull();
    });
});
