import { escapeHtml } from '../../../../packages/javascript/browser/ui_components/utils/security.js';

export class Layout {
    constructor(options = {}) {
        this.store = options.store;
        this.api = options.api;
        this.definition = options.definition || null;
        this.element = null;
        this._contentArea = null;
        this._loadingOverlay = null;
    }

    mount(container) {
        this.element = document.createElement('div');
        this.element.className = 'app-layout';
        this.element.innerHTML = this._render();

        container.appendChild(this.element);

        this._contentArea = this.element.querySelector('.layout-content');
        this._loadingOverlay = this.element.querySelector('.layout-loading');

        this._bindEvents();
        this._setActiveNav(location.hash.slice(1) || '/');
        this.updateUser(this.store?.get('user') || null);
    }

    _render() {
        const appTitle = this.definition?.app?.title || 'Bricks Commerce';
        const appSubtitle = this.definition?.app?.subtitle || 'SPA proof';
        const navigationItems = Array.isArray(this.definition?.navigation?.items) && this.definition.navigation.items.length > 0
            ? this.definition.navigation.items
            : [
                { path: '/', label: 'Home', visibility: ['guest', 'member', 'admin'] },
                { path: '/products', label: 'Products', visibility: ['member', 'admin'] },
                { path: '/orders', label: 'Orders', visibility: ['member', 'admin'] },
                { path: '/admin/products', label: 'Admin Products', visibility: ['admin'] },
                { path: '/register', label: 'Register', visibility: ['guest'] },
                { path: '/login', label: 'Login', visibility: ['guest'] }
            ];

        return `
            <aside class="layout-sidebar" data-collapsed="false">
                <div class="sidebar-header">
                    <div class="sidebar-logo">
                        <span class="logo-icon">B</span>
                        <div>
                            <div class="logo-text">${escapeHtml(appTitle)}</div>
                            <small class="logo-subtext">${escapeHtml(appSubtitle)}</small>
                        </div>
                    </div>
                    <button class="sidebar-toggle" type="button" aria-label="Toggle sidebar">Toggle</button>
                </div>

                <nav class="sidebar-nav">
                    ${navigationItems.map((item) => {
                        const visibility = Array.isArray(item.visibility)
                            ? item.visibility
                            : (typeof item.visibility === 'string' ? [item.visibility] : []);
                        const visibilityAttr = visibility.length > 0
                            ? ` data-visibility="${escapeHtml(visibility.join(' '))}"`
                            : '';

                        return `<a href="#${item.path}" class="nav-item" data-path="${item.path}"${visibilityAttr}>${escapeHtml(item.label || item.path)}</a>`;
                    }).join('')}
                </nav>
            </aside>

            <main class="layout-main">
                <header class="layout-header">
                    <div class="header-breadcrumb">${escapeHtml(appTitle)}</div>
                    <div class="header-actions">
                        <span class="header-user-name" data-user-name>Guest</span>
                        <span class="header-user-role" data-user-role></span>
                        <button class="header-action" type="button" data-theme-toggle>Theme</button>
                        <button class="header-action" type="button" data-logout>Logout</button>
                    </div>
                </header>

                <div class="layout-content"></div>
            </main>

            <div class="layout-loading" style="display:none;">
                <div class="loading-spinner"></div>
            </div>
        `;
    }

    _bindEvents() {
        this.element.querySelector('.sidebar-toggle')?.addEventListener('click', () => {
            const sidebar = this.element.querySelector('.layout-sidebar');
            const collapsed = sidebar?.dataset.collapsed === 'true';
            if (sidebar) {
                sidebar.dataset.collapsed = collapsed ? 'false' : 'true';
            }
        });

        this.element.querySelector('[data-theme-toggle]')?.addEventListener('click', () => {
            const currentTheme = this.store?.get('theme') || 'light';
            const nextTheme = currentTheme === 'light' ? 'dark' : 'light';
            this.store?.set('theme', nextTheme);
            document.documentElement.setAttribute('data-theme', nextTheme);
        });

        this.element.querySelector('[data-logout]')?.addEventListener('click', () => {
            this.api?.clearToken?.();
            this.store?.setMany?.({
                user: null,
                isAuthenticated: false
            });
            location.hash = '/login';
        });

        window.addEventListener('hashchange', () => {
            this._setActiveNav(location.hash.slice(1) || '/');
        });

        const initialTheme = this.store?.get('theme') || 'light';
        document.documentElement.setAttribute('data-theme', initialTheme);
    }

    _getNavigationVisibility(user) {
        if (!user) {
            return new Set(['guest']);
        }

        const visibility = new Set(['member']);
        if (user.role === 'admin') {
            visibility.add('admin');
        }

        return visibility;
    }

    _syncNavigationVisibility(user) {
        const allowedVisibility = this._getNavigationVisibility(user);
        const navItems = this.element?.querySelectorAll('.nav-item') || [];

        navItems.forEach((item) => {
            const visibility = (item.dataset.visibility || '')
                .split(' ')
                .map((value) => value.trim())
                .filter(Boolean);
            const isVisible = visibility.length === 0 || visibility.some((role) => allowedVisibility.has(role));
            item.style.display = isVisible ? '' : 'none';
            item.hidden = !isVisible;
            item.setAttribute('aria-hidden', String(!isVisible));
        });
    }

    _setActiveNav(path) {
        const navItems = this.element?.querySelectorAll('.nav-item') || [];
        navItems.forEach((item) => {
            const itemPath = item.dataset.path || '/';
            const active = path === itemPath || (itemPath !== '/' && path.startsWith(itemPath));
            item.classList.toggle('active', active);
        });

        this._updateBreadcrumb(path);
    }

    _updateBreadcrumb(path) {
        const breadcrumb = this.element?.querySelector('.header-breadcrumb');
        if (!breadcrumb) {
            return;
        }

        const route = Array.isArray(this.definition?.routes)
            ? this.definition.routes.find((item) => item.path === path || (item.path?.includes(':') && this._pathMatches(item.path, path)))
            : null;

        breadcrumb.textContent = route?.title || this.definition?.app?.title || 'Bricks Commerce';
    }

    _pathMatches(pattern, path) {
        const regex = new RegExp(`^${String(pattern).replace(/[.*+?^${}()|[\]\\]/g, '\\$&').replace(/:([^/]+)/g, '[^/]+')}$`);
        return regex.test(path);
    }

    getContentArea() {
        return this._contentArea;
    }

    setLoading(loading) {
        if (this._loadingOverlay) {
            this._loadingOverlay.style.display = loading ? 'flex' : 'none';
        }
    }

    updateUser(user) {
        const nameElement = this.element?.querySelector('[data-user-name]');
        const roleElement = this.element?.querySelector('[data-user-role]');
        const logoutButton = this.element?.querySelector('[data-logout]');

        if (nameElement) {
            nameElement.textContent = user?.name || 'Guest';
        }

        if (roleElement) {
            roleElement.textContent = user?.role === 'admin' ? 'Admin' : user ? 'Member' : '';
        }

        if (logoutButton) {
            logoutButton.style.display = user ? 'inline-flex' : 'none';
        }

        this._syncNavigationVisibility(user);
    }

    destroy() {
        this.element?.remove();
        this.element = null;
        this._contentArea = null;
        this._loadingOverlay = null;
    }
}

export default Layout;
