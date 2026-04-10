import { Router } from './Router.js';
import { Store } from './Store.js';
import { ApiService } from './ApiService.js';
import { Layout } from './Layout.js';
import { createRoutes } from '../pages/routes.js';
import { loadFrontendDefinitionBootstrap } from '../definition/load-definition.js';
import { setDefinitionBundle } from '../definition/definition-store.js';

class App {
    constructor() {
        this.router = null;
        this.store = null;
        this.api = null;
        this.layout = null;
        this.rootElement = document.getElementById('app');
    }

    async init() {
        try {
            const { architecture, frontendDefinition } = await loadFrontendDefinitionBootstrap();
            setDefinitionBundle({ architecture, frontendDefinition });

            this.api = new ApiService({
                baseUrl: '/api',
                timeout: 30000
            });

            this.store = new Store({
                user: null,
                isAuthenticated: false,
                theme: localStorage.getItem('theme') || 'light',
                sidebarCollapsed: false,
                notifications: [],
                loading: false
            });

            this.layout = new Layout({
                store: this.store,
                api: this.api,
                definition: frontendDefinition
            });

            const routes = createRoutes(frontendDefinition);

            this.router = new Router({
                routes,
                store: this.store,
                api: this.api,
                layout: this.layout
            });

            this.rootElement.innerHTML = '';
            this.layout.mount(this.rootElement);
            document.title = frontendDefinition.app?.title || document.title;

            this._bindGlobalEvents();
            await this._restoreSession();
            this._configureRouteGuards();

            this.router.start();
        } catch (error) {
            console.error('[App] init failed', error);
            this._showError('Failed to initialize the storefront');
        }
    }

    async _restoreSession() {
        const token = this.api.getToken();
        if (!token) {
            return;
        }

        try {
            const user = await this.api.get('/auth/me');
            this.store.setMany({
                user,
                isAuthenticated: true
            });
            this.layout?.updateUser(user);
        } catch (error) {
            console.warn('[App] session restore failed', error);
            this.api.clearToken();
            this.store.setMany({
                user: null,
                isAuthenticated: false
            });
        }
    }

    _configureRouteGuards() {
        this.router.beforeEach((to, from, next) => {
            const isAuthenticated = this.store.get('isAuthenticated');
            const user = this.store.get('user');

            if (to.meta?.requiresAuth && !isAuthenticated) {
                next('/login');
                return;
            }

            if (to.meta?.requiresAdmin && user?.role !== 'admin') {
                next('/');
                return;
            }

            next();
        });
    }

    _bindGlobalEvents() {
        this.store.subscribe('theme', (theme) => {
            document.documentElement.setAttribute('data-theme', theme);
            localStorage.setItem('theme', theme);
        });

        this.store.subscribe('loading', (loading) => {
            this.layout?.setLoading(loading);
        });

        this.store.subscribe('user', (user) => {
            this.layout?.updateUser(user);
        });

        window.addEventListener('unauthorized', () => {
            this.store.setMany({
                user: null,
                isAuthenticated: false
            });
            this.router?.navigate('/login');
        });

        window.addEventListener('unhandledrejection', (event) => {
            console.error('[App] unhandled promise rejection', event.reason);
        });
    }

    _showError(message) {
        this.rootElement.innerHTML = `
            <div class="app-error">
                <div class="error-icon">!</div>
                <h2>Startup error</h2>
                <p>${message}</p>
                <button onclick="location.reload()">Reload</button>
            </div>
        `;
    }
}

const app = new App();

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => app.init());
} else {
    app.init();
}

export { app };
export default App;
