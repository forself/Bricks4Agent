/**
 * 定義網站外殼：hash 路由，每個路由一個 DynamicPageRenderer。
 *
 * 原始頁定義（definitions/{pageId}.json）保留 api；渲染用的定義經 PageDefinitionAdapter.toNewFormat
 * 轉換，api 呼叫則照 DefinitionRuntimePage 的作法讀原始定義，改接瀏覽器內的記憶體資料來源。
 * 原型不連任何後端；資料只存在目前分頁，重新整理即清空。
 */
import Locale from './runtime/ui_components/i18n/index.js';
import './locale.js';
import { BasicButton } from './runtime/ui_components/common/BasicButton/index.js';
import { ModalPanel, ToastPanel } from './runtime/ui_components/layout/Panel/index.js';
import { PageDefinitionAdapter } from './runtime/page-generator/PageDefinitionAdapter.js';
import { DynamicPageRenderer } from './runtime/page-generator/DynamicPageRenderer.js';
import { MemoryStore } from './memory-store.js';
import {
    PAGE_ID_PATTERN,
    applyListColumns,
    buildSiteModel,
    parseRoute,
    resourceEndpoint,
    routeHref
} from './site-model.js';

const API_KEYS = ['list', 'get', 'create', 'update', 'delete'];
const DEFAULT_PAGE_SIZE = 20;

ModalPanel.defaults.manageFocus = true;

function t(key, params) {
    return Locale.t(`definitionSite.${key}`, params);
}

function createElement(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined && text !== null) node.textContent = String(text);
    return node;
}

async function fetchJson(url) {
    const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store' });
    if (!response.ok) throw new Error(`${url} (HTTP ${response.status})`);
    return response.json();
}

function navigate(href) {
    if (window.location.hash === href) {
        window.dispatchEvent(new HashChangeEvent('hashchange'));
    } else {
        window.location.hash = href;
    }
}

function appendQuery(endpoint, params) {
    const search = new URLSearchParams();
    for (const [key, value] of Object.entries(params || {})) {
        if (value === null || value === undefined || value === '') continue;
        search.set(key, String(value));
    }
    const query = search.toString();
    return query ? `${endpoint}?${query}` : endpoint;
}

/** 渲染用定義：列表改走帶列操作（檢視、編輯、刪除）的列表模式，並只顯示可文字化的欄位 */
function toRuntimeDefinition(page) {
    const runtime = PageDefinitionAdapter.toNewFormat(page.definition);
    if (!runtime?.page || !Array.isArray(runtime.fields)) {
        throw new Error(`page ${page.id} cannot be prepared for rendering`);
    }
    if (page.type === 'list') {
        runtime.page = { ...runtime.page, view: 'list' };
        runtime.fields = applyListColumns(runtime.fields, { yes: t('yes'), no: t('no') });
    }
    return runtime;
}

class PageView {
    constructor({ model, page, recordId, store, host }) {
        this.model = model;
        this.page = page;
        this.recordId = recordId;
        this.store = store;
        this.host = host;
        this.renderer = null;
        this.controls = [];
        this.listState = { filters: {}, page: 1, pageSize: DEFAULT_PAGE_SIZE };
        this.destroyed = false;
    }

    get api() {
        return this.page.definition.api || {};
    }

    linkedPage(type) {
        const id = this.page.links[type];
        return id ? this.model.byId.get(id) : null;
    }

    async mount() {
        const { page } = this;
        const section = createElement('section', 'ds-page');
        section.dataset.pageId = page.id;
        section.dataset.pageType = page.type;

        const header = createElement('div', 'ds-page__header');
        header.appendChild(createElement('h2', 'ds-page__title', page.title));
        const toolbar = createElement('div', 'ds-page__toolbar');
        header.appendChild(toolbar);
        section.appendChild(header);

        this.notice = createElement('p', 'ds-notice');
        this.notice.hidden = true;
        section.appendChild(this.notice);

        const body = createElement('div', 'ds-page__body');
        section.appendChild(body);
        this.host.replaceChildren(section);

        if (page.type === 'list' && page.links.createForm) {
            const create = new BasicButton({
                type: BasicButton.TYPES.CUSTOM,
                variant: 'primary',
                showIcon: false,
                customLabel: t('create'),
                onClick: () => navigate(routeHref(page.links.createForm))
            });
            create.element.classList.add('ds-page__create');
            create.element.dataset.siteAction = 'create';
            create.mount(toolbar);
            this.controls.push(create);
        }

        const data = await this.loadInitialRecord();
        if (this.destroyed) return;

        const renderer = new DynamicPageRenderer({
            definition: toRuntimeDefinition(page),
            mode: page.type,
            data,
            pageSize: DEFAULT_PAGE_SIZE,
            onSave: (values) => this.save(values),
            onCancel: () => this.leaveForm(),
            onSearch: (filters, pageNumber, pageSize) => this.loadList(filters, pageNumber, pageSize),
            onAction: (action, row) => this.handleRowAction(action, row),
            onBack: () => this.backFromDetail(),
            onEdit: () => this.editFromDetail()
        });
        this.renderer = renderer;
        await renderer.init();
        if (this.destroyed) {
            // 初始化期間已換頁：destroy() 可能已處理過，重複呼叫 destroy 是安全的
            renderer.destroy();
            return;
        }
        renderer.mount(body);

        if (page.type === 'list') {
            await this.loadList({}, 1, DEFAULT_PAGE_SIZE);
        }
    }

    showNotice(message) {
        this.notice.textContent = message;
        this.notice.hidden = false;
    }

    async loadInitialRecord() {
        const { page } = this;
        if (page.type === 'list') return null;
        if (!this.recordId) {
            if (page.type === 'detail') this.showNotice(t('noRecord'));
            return null;
        }
        const base = page.type === 'detail' ? this.api.get : (this.api.get || this.api.update);
        if (!base) {
            this.showNotice(t('noEndpoint'));
            // 讀不到紀錄就不是在編輯它：表單維持新增模式，送出時不會被當成更新
            this.recordId = null;
            return null;
        }
        try {
            return await this.store.get(resourceEndpoint(base, this.recordId));
        } catch {
            this.showNotice(t('recordMissing'));
            // 找不到的紀錄不可被「更新」；表單改為新增模式
            this.recordId = null;
            return null;
        }
    }

    async loadList(filters = {}, pageNumber = 1, pageSize = DEFAULT_PAGE_SIZE) {
        const endpoint = this.api.list;
        if (!endpoint) {
            this.showNotice(t('noEndpoint'));
            return;
        }
        this.listState = { filters, page: pageNumber, pageSize };
        try {
            const result = await this.store.get(appendQuery(endpoint, { ...filters, page: pageNumber, pageSize }));
            if (this.destroyed) return;
            this.renderer?.getRenderer?.()?.setData?.(result.items, result.total);
            this.host.dataset.recordCount = String(result.total);
        } catch (error) {
            ToastPanel.error(t('loadFailed', { message: error.message }));
        }
    }

    async save(values) {
        const editing = Boolean(this.recordId);
        if (editing && !this.api.update) {
            ToastPanel.warning(t('cannotEdit'));
            return;
        }
        const endpoint = editing ? resourceEndpoint(this.api.update, this.recordId) : this.api.create;
        if (!endpoint) {
            ToastPanel.warning(t('noEndpoint'));
            return;
        }
        let record;
        try {
            record = editing ? await this.store.put(endpoint, values) : await this.store.post(endpoint, values);
        } catch (error) {
            ToastPanel.error(t('saveFailed', { message: error.message }));
            return;
        }
        ToastPanel.success(t(editing ? 'updated' : 'created'));
        const { links } = this.page;
        if (links.list) navigate(routeHref(links.list));
        else if (links.detail) navigate(routeHref(links.detail, record.id));
        else if (this.api.update) navigate(routeHref(this.page.id, record.id));
        // 只能新增的表單（沒有列表、明細，也沒有 api.update）：留在新增模式並清空表單，可以接著送下一筆
        else navigate(routeHref(this.page.id));
    }

    leaveForm() {
        const { links } = this.page;
        if (this.recordId && links.detail) navigate(routeHref(links.detail, this.recordId));
        else if (links.list) navigate(routeHref(links.list));
        else window.history.back();
    }

    backFromDetail() {
        if (this.page.links.list) navigate(routeHref(this.page.links.list));
        else window.history.back();
    }

    /** 編輯連到同一資源中第一個有 api.update 的表單；沒有這種表單時，有表單就是只能新增，否則是沒有連結。 */
    editTarget() {
        const form = this.linkedPage('editForm');
        if (form) return { form };
        return { error: this.linkedPage('createForm') ? 'cannotEdit' : 'notLinked' };
    }

    editFromDetail() {
        if (!this.recordId) {
            ToastPanel.info(t('noRecord'));
            return;
        }
        const { form, error } = this.editTarget();
        if (error === 'cannotEdit') ToastPanel.warning(t('cannotEdit'));
        else if (error) ToastPanel.info(t('notLinked'));
        else navigate(routeHref(form.id, this.recordId));
    }

    handleRowAction(action, row) {
        const id = row?.id;
        if (id === null || id === undefined) return;
        if (action === 'view') {
            const detail = this.linkedPage('detail');
            if (detail) navigate(routeHref(detail.id, id));
            else ToastPanel.info(t('notLinked'));
            return;
        }
        if (action === 'edit') {
            const { form, error } = this.editTarget();
            if (error === 'cannotEdit') ToastPanel.warning(t('cannotEdit'));
            else if (error) ToastPanel.info(t('notLinked'));
            else navigate(routeHref(form.id, id));
            return;
        }
        if (action === 'delete') {
            if (!this.api.delete) {
                ToastPanel.warning(t('noEndpoint'));
                return;
            }
            ModalPanel.confirm({
                title: t('deleteTitle'),
                message: t('deleteMessage'),
                onConfirm: () => { void this.deleteRecord(id); }
            });
        }
    }

    async deleteRecord(id) {
        try {
            await this.store.delete(resourceEndpoint(this.api.delete, id));
        } catch (error) {
            ToastPanel.error(t('deleteFailed', { message: error.message }));
            return;
        }
        ToastPanel.success(t('deleted'));
        const { filters, page, pageSize } = this.listState;
        await this.loadList(filters, page, pageSize);
    }

    destroy() {
        this.destroyed = true;
        this.renderer?.destroy();
        this.renderer = null;
        this.controls.splice(0).forEach(control => control.destroy?.());
    }
}

class SiteApp {
    constructor({ model, store, main, nav }) {
        this.model = model;
        this.store = store;
        this.main = main;
        this.nav = nav;
        this.view = null;
        this.routeToken = 0;
        this.navLinks = new Map();
    }

    start() {
        this.nav.setAttribute('aria-label', t('navLabel'));
        for (const page of this.model.pages) {
            const link = createElement('a', 'ds-nav__link', page.title);
            link.href = routeHref(page.id);
            link.dataset.pageId = page.id;
            this.nav.appendChild(link);
            this.navLinks.set(page.id, link);
        }
        window.addEventListener('hashchange', () => { void this.route(); });
        void this.route();
    }

    markCurrent(pageId) {
        for (const [id, link] of this.navLinks) {
            const current = id === pageId;
            link.classList.toggle('ds-nav__link--current', current);
            if (current) link.setAttribute('aria-current', 'page');
            else link.removeAttribute('aria-current');
        }
    }

    async route() {
        const token = ++this.routeToken;
        let parsed = parseRoute(window.location.hash);
        if (parsed && parsed.pageId === null && this.model.defaultPageId) {
            parsed = { pageId: this.model.defaultPageId, recordId: null };
            window.history.replaceState(null, '', routeHref(parsed.pageId));
        }

        this.view?.destroy();
        this.view = null;
        delete this.main.dataset.recordCount;

        const page = parsed ? this.model.byId.get(parsed.pageId) : null;
        if (!page) {
            const notice = createElement('p', 'ds-notice', t('unknownPage'));
            this.main.replaceChildren(notice);
            this.main.dataset.pageId = '';
            this.main.dataset.routeState = 'not-found';
            this.markCurrent(null);
            return;
        }

        this.main.dataset.routeState = 'loading';
        this.main.dataset.pageId = page.id;
        this.markCurrent(page.id);
        const view = new PageView({
            model: this.model,
            page,
            recordId: parsed.recordId,
            store: this.store,
            host: this.main
        });
        this.view = view;
        try {
            await view.mount();
            if (token === this.routeToken) this.main.dataset.routeState = 'ready';
        } catch (error) {
            if (token !== this.routeToken) return;
            this.main.dataset.routeState = 'error';
            this.main.replaceChildren(createElement('p', 'ds-notice ds-notice--error', t('loadFailed', { message: error.message })));
            console.error(error);
        }
    }
}

async function boot() {
    const main = document.querySelector('[data-site-main]');
    const nav = document.querySelector('[data-site-nav]');
    const titleElement = document.querySelector('[data-site-title]');
    try {
        const site = await fetchJson('site.json');
        const pageIds = Array.isArray(site?.pages) ? site.pages : [];
        const definitions = {};
        for (const id of pageIds) {
            if (typeof id !== 'string' || !PAGE_ID_PATTERN.test(id)) throw new Error('site.json lists an invalid page id');
            definitions[id] = await fetchJson(`definitions/${id}.json`);
        }
        const model = buildSiteModel(site, definitions);
        const title = model.title || t('defaultTitle');
        document.title = title;
        titleElement.textContent = title;

        const collections = new Set();
        for (const page of model.pages) {
            for (const key of API_KEYS) {
                const value = page.definition.api?.[key];
                if (typeof value === 'string' && value !== '') collections.add(value);
            }
        }
        const footer = createElement('footer', 'ds-footer', t('memoryNotice'));
        main.after(footer);

        new SiteApp({ model, store: new MemoryStore({ collections: [...collections] }), main, nav }).start();
    } catch (error) {
        main.dataset.routeState = 'error';
        main.replaceChildren(createElement('p', 'ds-notice ds-notice--error', t('bootFailed', { message: error.message })));
        console.error(error);
    }
}

void boot();
