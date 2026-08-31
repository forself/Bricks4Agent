import { ComponentFactory } from '../../packages/javascript/browser/ui_components/binding/index.js';
import { Notification } from '../../packages/javascript/browser/ui_components/common/index.js';
import { SAMPLE_OPTIONS, SAMPLE_RUNTIME_OPTIONS, GALLERY_SKIP } from './sample-data.js';

const CATALOG_URL = new URL('../../packages/javascript/browser/ui_components/metadata/component-catalog.json', import.meta.url);
const LS_KEY = 'b4a-theme-studio-overrides';

const TOKEN_BINDINGS = {
    '--cl-primary': 'theme.tokens.primary',
    '--cl-success': 'theme.tokens.success',
    '--cl-warning': 'theme.tokens.warning',
    '--cl-danger': 'theme.tokens.danger',
    '--cl-info': 'theme.tokens.info',
    '--cl-text': 'theme.tokens.text',
    '--cl-text-secondary': 'theme.tokens.textSecondary',
    '--cl-bg': 'theme.tokens.bg',
    '--cl-bg-secondary': 'theme.tokens.bgSecondary',
    '--cl-border': 'theme.tokens.border',
    '--cl-radius-sm': 'theme.tokens.radiusSm',
    '--cl-radius-md': 'theme.tokens.radiusMd',
    '--cl-radius-lg': 'theme.tokens.radiusLg',
    '--cl-radius-xl': 'theme.tokens.radiusXl',
    '--cl-font-size-md': 'theme.tokens.fontMd',
    '--cl-font-size-lg': 'theme.tokens.fontLg',
    '--cl-font-size-xl': 'theme.tokens.fontXl',
    '--cl-font-size-2xl': 'theme.tokens.font2xl',
    '--cl-shadow-md': 'theme.tokens.shadowMd',
    '--cl-transition': 'theme.tokens.transition',
    '--cl-font-family': 'theme.tokens.fontFamily',
    '--cl-font-family-cjk': 'theme.tokens.fontFamilyCjk',
    '--cl-font-family-mono': 'theme.tokens.fontFamilyMono',
};

const NUMERIC_TOKENS = new Set([
    '--cl-radius-sm', '--cl-radius-md', '--cl-radius-lg', '--cl-radius-xl',
    '--cl-font-size-md', '--cl-font-size-lg', '--cl-font-size-xl', '--cl-font-size-2xl',
]);

const COLOR_TOKENS = new Set([
    '--cl-primary', '--cl-success', '--cl-warning', '--cl-danger', '--cl-info',
    '--cl-text', '--cl-text-secondary', '--cl-bg', '--cl-bg-secondary', '--cl-border',
]);
const SAFE_TOKEN_PATTERN = /^--cl-[a-z0-9]+(?:-[a-z0-9]+)*$/;
const SAFE_CLASS_PATTERN = /^[A-Za-z][A-Za-z0-9_-]{0,127}$/;
const BLOCKED_OBJECT_KEYS = new Set(['__proto__', 'prototype', 'constructor']);
const UNSAFE_CSS_VALUE_PATTERN = /[;{}@\r\n\f\\]|\/\*|\*\/|(?:url|expression)\s*\(|javascript\s*:|!\s*important/i;
const MAX_CSS_VALUE_LENGTH = 512;

function clone(value) {
    return typeof structuredClone === 'function'
        ? structuredClone(value)
        : JSON.parse(JSON.stringify(value));
}

function safeClassName(name) {
    return `b4a-c-${String(name || '').replace(/[^A-Za-z0-9_-]/g, '-')}`.slice(0, 128);
}

function assertPlainRecord(value, path) {
    if (!value || typeof value !== 'object' || Array.isArray(value)) {
        throw new Error(`${path} 必須是物件。`);
    }
    const prototype = Object.getPrototypeOf(value);
    if (prototype !== Object.prototype && prototype !== null) {
        throw new Error(`${path} 必須是純資料物件。`);
    }
    for (const key of Reflect.ownKeys(value)) {
        if (typeof key !== 'string' || BLOCKED_OBJECT_KEYS.has(key)) {
            throw new Error(`${path} 含不安全的 key。`);
        }
        const descriptor = Object.getOwnPropertyDescriptor(value, key);
        if (!descriptor || descriptor.get || descriptor.set) {
            throw new Error(`${path}.${key} 必須是資料欄位。`);
        }
    }
    return value;
}

function normalizeTokenName(value, path = 'token') {
    const raw = String(value || '').trim();
    const token = raw.startsWith('--') ? raw : `--${raw}`;
    if (!SAFE_TOKEN_PATTERN.test(token)) {
        throw new Error(`${path} 必須符合 --cl-* token 格式。`);
    }
    return token;
}

function normalizeCssValue(value, path = 'token value') {
    if (typeof value === 'number') {
        if (!Number.isFinite(value)) throw new Error(`${path} 必須是有限數值。`);
        return String(value);
    }
    if (typeof value !== 'string') throw new Error(`${path} 必須是字串或數值。`);
    const normalized = value.trim();
    if (normalized.length > MAX_CSS_VALUE_LENGTH) throw new Error(`${path} 過長。`);
    if (UNSAFE_CSS_VALUE_PATTERN.test(normalized)) {
        throw new Error(`${path} 含不安全的 CSS 結構或 URL。`);
    }
    return normalized;
}

function normalizeClassName(value, fallback, path = 'className') {
    if (value !== undefined && typeof value !== 'string') {
        throw new Error(`${path} 必須是字串。`);
    }
    const normalized = String(value || fallback).trim().replace(/^\./, '');
    if (!SAFE_CLASS_PATTERN.test(normalized)) {
        throw new Error(`${path} 必須是單一安全 class 名稱。`);
    }
    return normalized;
}

function normalizeTokenRecord(value, path) {
    const record = assertPlainRecord(value, path);
    const output = {};
    for (const [rawToken, rawValue] of Object.entries(record)) {
        const token = normalizeTokenName(rawToken, `${path}.${rawToken}`);
        const candidate = NUMERIC_TOKENS.has(token) && typeof rawValue === 'number'
            ? `${rawValue}px`
            : rawValue;
        output[token] = normalizeCssValue(candidate, `${path}.${rawToken}`);
    }
    return output;
}

function downloadText(filename, text, type) {
    const blob = new Blob([text], { type });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = filename;
    anchor.hidden = true;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export function createThemeStudioController() {
    const root = document.documentElement;
    const overrides = {};
    const componentTweaks = {};
    const galleryHosts = new Map();
    const galleryInstances = [];
    let renderer = null;
    let catalog = [];
    let selectedComponent = '';
    let gallery = { total: 0, rendered: 0, skipped: 0, failed: 0 };

    function cssValue(token, host = root) {
        return getComputedStyle(host).getPropertyValue(token).trim();
    }

    function tokenStateValue(token, host = root) {
        const raw = cssValue(token, host);
        if (NUMERIC_TOKENS.has(token)) return Number.parseFloat(raw) || 0;
        if (COLOR_TOKENS.has(token)) {
            const probe = document.createElement('span');
            probe.hidden = true;
            probe.style.color = `var(${token})`;
            host.appendChild(probe);
            const color = getComputedStyle(probe).color;
            probe.remove();
            return color || raw;
        }
        return raw;
    }

    function tokenState() {
        const output = {};
        for (const [token, path] of Object.entries(TOKEN_BINDINGS)) {
            const key = path.split('.').at(-1);
            output[key] = tokenStateValue(token);
        }
        return output;
    }

    function componentState() {
        if (!selectedComponent) {
            return { className: '', primary: tokenStateValue('--cl-primary'), bg: tokenStateValue('--cl-bg'), text: tokenStateValue('--cl-text'), border: tokenStateValue('--cl-border') };
        }
        const tweak = componentTweaks[selectedComponent] || { className: safeClassName(selectedComponent), tokens: {} };
        const host = galleryHosts.get(selectedComponent) || root;
        const value = (token) => tweak.tokens[token] || tokenStateValue(token, host);
        return {
            className: tweak.className,
            primary: value('--cl-primary'),
            bg: value('--cl-bg'),
            text: value('--cl-text'),
            border: value('--cl-border'),
        };
    }

    function initialState() {
        return {
            theme: {
                dark: root.getAttribute('data-theme') === 'dark',
                components: catalog.map((component) => ({ value: component.registry_name, label: component.registry_name })),
                selectedComponent,
                component: componentState(),
                tokens: tokenState(),
            },
        };
    }

    function setRendererState(path, value) {
        renderer?.setState?.(path, value);
    }

    function syncAllState() {
        const next = initialState();
        const flatten = (value, prefix) => {
            if (value && typeof value === 'object' && !Array.isArray(value)) {
                Object.entries(value).forEach(([key, child]) => flatten(child, prefix ? `${prefix}.${key}` : key));
            } else {
                setRendererState(prefix, value);
            }
        };
        flatten(next, '');
    }

    function setToken(token, value) {
        const name = normalizeTokenName(token);
        const candidate = NUMERIC_TOKENS.has(name) && typeof value === 'number' ? `${value}px` : value;
        const normalized = normalizeCssValue(candidate, name);
        root.style.setProperty(name, normalized);
        overrides[name] = normalized;
        const binding = TOKEN_BINDINGS[name];
        if (binding) setRendererState(binding, NUMERIC_TOKENS.has(name) ? Number.parseFloat(normalized) : normalized);
    }

    function applyScopedStyles(name) {
        const host = galleryHosts.get(name);
        const tweak = componentTweaks[name];
        if (!host || !tweak) return;
        if (tweak.className) host.classList.add(tweak.className);
        Object.entries(tweak.tokens || {}).forEach(([token, value]) => host.style.setProperty(token, value));
    }

    function setScopedToken(name, token, value) {
        if (!name || !catalog.some((component) => component.registry_name === name)) {
            throw new Error('元件 scoped token 必須指向 catalog 元件。');
        }
        const tokenName = normalizeTokenName(token);
        const normalized = normalizeCssValue(value, `${name}.${tokenName}`);
        const tweak = componentTweaks[name] ||= { className: safeClassName(name), tokens: {} };
        tweak.tokens[tokenName] = normalized;
        const host = galleryHosts.get(name);
        host?.style.setProperty(tokenName, normalized);
    }

    function clearScoped(name) {
        const tweak = componentTweaks[name];
        const host = galleryHosts.get(name);
        if (tweak && host) {
            Object.keys(tweak.tokens || {}).forEach((token) => host.style.removeProperty(token));
            if (SAFE_CLASS_PATTERN.test(tweak.className || '')) host.classList.remove(tweak.className);
        }
        delete componentTweaks[name];
        syncAllState();
    }

    function normalizeComponentTweaks(source, path = 'components') {
        const record = assertPlainRecord(source, path);
        const catalogNames = new Set(catalog.map((component) => component.registry_name));
        const output = {};
        for (const [name, rawTweak] of Object.entries(record)) {
            if (!catalogNames.has(name)) throw new Error(`${path}.${name} 不是 catalog 元件。`);
            const tweak = assertPlainRecord(rawTweak, `${path}.${name}`);
            const extraKeys = Object.keys(tweak).filter((key) => key !== 'className' && key !== 'tokens');
            if (extraKeys.length) throw new Error(`${path}.${name} 含未知欄位：${extraKeys.join(', ')}。`);
            const tokens = normalizeTokenRecord(
                Object.prototype.hasOwnProperty.call(tweak, 'tokens') ? tweak.tokens : {},
                `${path}.${name}.tokens`,
            );
            output[name] = {
                className: normalizeClassName(tweak.className, safeClassName(name), `${path}.${name}.className`),
                tokens,
            };
        }
        return output;
    }

    function exportedComponents() {
        const output = normalizeComponentTweaks(componentTweaks);
        for (const [name, tweak] of Object.entries(output)) {
            if (!Object.keys(tweak.tokens).length) delete output[name];
        }
        return output;
    }

    function tokensJson() {
        const safeOverrides = normalizeTokenRecord(overrides, 'tokens');
        const payload = {
            meta: { name: 'Bricks4Agent Theme', generatedBy: 'Bricks4Agent self-hosted Theme Studio' },
            tokens: safeOverrides,
        };
        const components = exportedComponents();
        if (Object.keys(components).length) payload.components = components;
        return JSON.stringify(payload, null, 2);
    }

    function customCss() {
        const declarations = (values) => Object.entries(values).map(([key, value]) => `    ${key}: ${value};`).join('\n');
        const safeOverrides = normalizeTokenRecord(overrides, 'tokens');
        const lines = ['/* Generated by Bricks4Agent self-hosted Theme Studio. */', ':root {', declarations(safeOverrides), '}'];
        Object.entries(exportedComponents()).forEach(([, tweak]) => {
            lines.push('', `.${tweak.className} {`, declarations(tweak.tokens), '}');
        });
        return `${lines.join('\n')}\n`;
    }

    function normalizeImported(payload) {
        const record = assertPlainRecord(payload, 'Theme JSON');
        const isEnvelope = Object.prototype.hasOwnProperty.call(record, 'tokens')
            || Object.prototype.hasOwnProperty.call(record, 'components')
            || Object.prototype.hasOwnProperty.call(record, 'meta');
        if (!isEnvelope) {
            return { tokens: normalizeTokenRecord(record, 'tokens'), components: {} };
        }
        const extraKeys = Object.keys(record).filter((key) => !['meta', 'tokens', 'components'].includes(key));
        if (extraKeys.length) throw new Error(`Theme JSON 含未知欄位：${extraKeys.join(', ')}。`);
        return {
            tokens: normalizeTokenRecord(
                Object.prototype.hasOwnProperty.call(record, 'tokens') ? record.tokens : {},
                'tokens',
            ),
            components: normalizeComponentTweaks(
                Object.prototype.hasOwnProperty.call(record, 'components') ? record.components : {},
                'components',
            ),
        };
    }

    function clearAppliedTheme() {
        for (const token of Object.keys(overrides)) root.style.removeProperty(token);
        for (const [name, tweak] of Object.entries(componentTweaks)) {
            const host = galleryHosts.get(name);
            if (!host) continue;
            for (const token of Object.keys(tweak.tokens || {})) {
                if (SAFE_TOKEN_PATTERN.test(token)) host.style.removeProperty(token);
            }
            if (SAFE_CLASS_PATTERN.test(tweak.className || '')) host.classList.remove(tweak.className);
        }
        for (const token of Object.keys(overrides)) delete overrides[token];
        for (const name of Object.keys(componentTweaks)) delete componentTweaks[name];
    }

    function applyImported(payload) {
        const normalized = normalizeImported(payload);
        clearAppliedTheme();
        Object.assign(overrides, normalized.tokens);
        Object.assign(componentTweaks, normalized.components);
        for (const [token, value] of Object.entries(overrides)) root.style.setProperty(token, value);
        for (const name of Object.keys(componentTweaks)) applyScopedStyles(name);
        syncAllState();
    }

    function saveLocal() {
        localStorage.setItem(LS_KEY, tokensJson());
        Notification.success('主題設定已儲存。');
    }

    function loadLocal() {
        const raw = localStorage.getItem(LS_KEY);
        if (!raw) return;
        try {
            applyImported(JSON.parse(raw));
        } catch (error) {
            console.warn('[ThemeStudio] Ignored invalid local state:', error);
        }
    }

    function resetAll() {
        Object.keys(overrides).forEach((token) => root.style.removeProperty(token));
        Object.keys(overrides).forEach((token) => delete overrides[token]);
        Object.keys(componentTweaks).forEach(clearScoped);
        localStorage.removeItem(LS_KEY);
        syncAllState();
        Notification.info('主題設定已重設。');
    }

    function mountInstance(instance, host) {
        if (!instance) return false;
        if (host.childElementCount === 0) {
            if (typeof instance.mount === 'function') instance.mount(host);
            else if (instance.element) host.appendChild(instance.element);
            else if (typeof instance.render === 'function') instance.render(host);
        }
        galleryInstances.push(instance);
        return host.childElementCount > 0;
    }

    async function buildGallery() {
        const galleryHost = renderer?.getHost?.('theme-gallery');
        if (!galleryHost) return;
        galleryHost.replaceChildren();
        galleryHost.dataset.studioZone = 'gallery';
        galleryHosts.clear();
        galleryInstances.splice(0).reverse().forEach((instance) => instance?.destroy?.());
        const byCategory = new Map();
        catalog.forEach((component) => {
            const category = component.category || 'other';
            if (!byCategory.has(category)) byCategory.set(category, []);
            byCategory.get(category).push(component.registry_name);
        });
        const stats = { total: catalog.length, rendered: 0, skipped: 0, failed: 0 };
        [...byCategory.entries()].sort(([left], [right]) => left.localeCompare(right)).forEach(([category, names]) => {
            const section = document.createElement('section');
            section.className = 'theme-gallery-section';
            const heading = document.createElement('h2');
            heading.textContent = `${category} (${names.length})`;
            section.appendChild(heading);
            const grid = document.createElement('div');
            grid.className = 'theme-gallery-grid';
            section.appendChild(grid);
            galleryHost.appendChild(section);
            names.sort().forEach((name) => {
                const card = document.createElement('article');
                card.className = 'theme-gallery-card';
                const label = document.createElement('h3');
                label.textContent = name;
                const body = document.createElement('div');
                body.className = 'theme-gallery-preview';
                body.id = `theme-preview-${name}`;
                card.append(label, body);
                grid.appendChild(card);
                galleryHosts.set(name, body);
                if (GALLERY_SKIP.has(name) || !SAMPLE_OPTIONS[name]) {
                    body.textContent = '此元件需情境資料，請由生成頁面驗證。';
                    stats.skipped += 1;
                    return;
                }
                try {
                    const instance = ComponentFactory.create(name, {
                        container: body,
                        containerId: body.id,
                        ...clone(SAMPLE_OPTIONS[name]),
                        ...(SAMPLE_RUNTIME_OPTIONS[name] || {}),
                    });
                    if (mountInstance(instance, body)) stats.rendered += 1;
                    else stats.failed += 1;
                    applyScopedStyles(name);
                } catch (error) {
                    body.textContent = `預覽失敗：${error?.message || error}`;
                    stats.failed += 1;
                }
            });
        });
        gallery = stats;
    }

    const commands = {
        'theme.mode': (_context, enabled) => {
            root.setAttribute('data-theme', enabled ? 'dark' : '');
            setRendererState('theme.dark', Boolean(enabled));
        },
        'theme.save': () => saveLocal(),
        'theme.export-json': () => downloadText('theme.tokens.json', tokensJson(), 'application/json'),
        'theme.export-css': () => downloadText('theme.custom.css', customCss(), 'text/css'),
        'theme.import-json': async (_context, files) => {
            const file = files?.[0];
            if (!file) return;
            if (files.length !== 1 || !String(file.name || '').toLowerCase().endsWith('.json') || file.size > 1024 * 1024) {
                Notification.error('只接受一個 1 MB 以下的 JSON 檔。');
                return;
            }
            try {
                applyImported(JSON.parse(await file.text()));
                Notification.success('主題已匯入。');
            } catch (error) {
                Notification.error(`匯入失敗：${error?.message || error}`);
            }
        },
        'theme.reset': () => resetAll(),
        'theme.token-change': (context, value) => setToken(context.node?.options?.token, value),
        'theme.apply-json': () => {
            const source = renderer?.getComponent?.('theme-advanced-json')?.getValue?.() || '{}';
            try {
                applyImported(JSON.parse(source));
                Notification.success('JSON token 已套用。');
            } catch (error) {
                Notification.error(`JSON 解析失敗：${error?.message || error}`);
            }
        },
        'theme.select-component': (_context, value) => {
            selectedComponent = value || '';
            syncAllState();
        },
        'theme.component-class': (_context, value) => {
            if (!selectedComponent) return;
            const tweak = componentTweaks[selectedComponent] ||= { className: safeClassName(selectedComponent), tokens: {} };
            const host = galleryHosts.get(selectedComponent);
            if (host && tweak.className) host.classList.remove(tweak.className);
            const candidate = String(value || safeClassName(selectedComponent)).replace(/^\.+/, '').replace(/[^A-Za-z0-9_-]/g, '-');
            tweak.className = SAFE_CLASS_PATTERN.test(candidate) ? candidate : safeClassName(candidate);
            if (host) host.classList.add(tweak.className);
            syncAllState();
        },
        'theme.component-token': (context, value) => {
            if (!selectedComponent) return;
            setScopedToken(selectedComponent, context.node?.options?.token, value);
            syncAllState();
        },
        'theme.component-clear': () => clearScoped(selectedComponent),
    };

    async function prepare() {
        const response = await fetch(CATALOG_URL);
        if (!response.ok) throw new Error(`Catalog request failed (${response.status}).`);
        const payload = await response.json();
        catalog = Array.isArray(payload?.components) ? payload.components : [];
        selectedComponent = catalog[0]?.registry_name || '';
        return initialState();
    }

    async function attachRenderer(nextRenderer) {
        renderer = nextRenderer;
        const upload = renderer.getComponent?.('theme-import-json');
        if (upload?.fileInput) upload.fileInput.id = 'theme-import-file';
        loadLocal();
        syncAllState();
        await buildGallery();
        return api;
    }

    function destroy() {
        galleryInstances.splice(0).reverse().forEach((instance) => instance?.destroy?.());
        galleryHosts.clear();
        renderer = null;
    }

    const api = {
        commands,
        prepare,
        attachRenderer,
        destroy,
        setToken,
        setScopedToken,
        resolveColor: (token, host) => tokenStateValue(token, host || root),
        overrides,
        componentTweaks,
        galleryHosts,
        tokensJson,
        customCss,
        applyImported,
        get renderer() { return renderer; },
        get gallery() { return gallery; },
        get galleryHost() { return renderer?.getHost?.('theme-gallery') || null; },
    };

    return api;
}

export default createThemeStudioController;
