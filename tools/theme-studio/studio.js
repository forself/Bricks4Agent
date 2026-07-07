/**
 * Theme Studio — 全域視覺調校台(自舉:完全以 Bricks4Agent 元件搭成)
 *
 * 給網站開發者與 AI 代理:所見即所得調整設計 token → 即時預覽全部元件 → 存成客製樣式表。
 * 儲存:localStorage(工作階段)+ 匯出 theme.tokens.json(機器可讀,AI 用)與 theme.custom.css(客製樣式表)。
 * 因為全庫走 var(--cl-*),改 token 只需 root.style.setProperty 即即時重繪所有元件。
 */
// 相對路徑:import() 依 studio.js URL、fetch 依 index.html URL 解析,兩者同在 tools/theme-studio/
// → 不論伺服器根目錄(Live Server 工作區根 / python Bricks4Agent 根)皆正確
const LIB = '../../packages/javascript/browser/ui_components';
const {
    TextInput, TextArea, NumberInput, Slider, Dropdown, ToggleSwitch, FormField
} = await import(`${LIB}/form/index.js`);
const {
    BasicButton, DownloadButton, UploadButton, ColorPicker, Notification, SimpleDialog, Tooltip, Divider, Tag
} = await import(`${LIB}/common/index.js`);
const { TabContainer } = await import(`${LIB}/layout/index.js`);
const { CardPanel } = await import(`${LIB}/layout/Panel/index.js`);
const { ComponentFactory } = await import(`${LIB}/binding/index.js`);
const { SAMPLE_OPTIONS, GALLERY_SKIP, STAGE_SAMPLES } = await import('./sample-data.js');

const root = document.documentElement;
const overrides = {};                    // 全域:{ '--cl-primary': '#123456', ... } 只存有調整的
// 各元件覆蓋:{ 元件名: { className:'b4a-c-元件名', tokens:{ '--cl-x':值 } } };作用域為具名 class
const componentTweaks = {};
const cardBodyByName = {};                // 元件名 → gallery 卡片容器(即時預覽:inline 設 token,子元件 var() 繼承)
const LS_KEY = 'b4a-theme-studio-overrides';

const defaultClass = (name) => 'b4a-c-' + name;

/* ── token 讀/寫(host 省略=全域 :root;給 host 元素則解析該作用域的繼承值)── */
const probe = document.createElement('span');
probe.style.display = 'none';
document.body.appendChild(probe);

function resolveColor(token, host) {
    let p = probe, temp = false;
    if (host) { p = document.createElement('span'); p.style.display = 'none'; host.appendChild(p); temp = true; }
    p.style.color = '';
    p.style.color = `var(${token})`;
    const c = getComputedStyle(p).color; // rgb(...)
    if (temp) p.remove();
    return c;
}
function resolvePx(token, host) {
    return parseFloat(getComputedStyle(host || root).getPropertyValue(token)) || 0;
}
function resolveRaw(token, host) {
    return getComputedStyle(host || root).getPropertyValue(token).trim();
}
function setToken(token, value) {
    root.style.setProperty(token, value);
    overrides[token] = value;
}
function setScopedToken(name, token, value) {
    const tw = componentTweaks[name] || (componentTweaks[name] = { className: defaultClass(name), tokens: {} });
    tw.tokens[token] = value;
    const el = cardBodyByName[name];
    if (el) el.style.setProperty(token, value);   // 即時預覽(子元件 var() 繼承此 inline token)
}
function clearScope(name) {
    const tw = componentTweaks[name];
    const el = cardBodyByName[name];
    if (tw && el) Object.keys(tw.tokens).forEach(t => el.style.removeProperty(t));
    delete componentTweaks[name];
}
// 控制項情境:無 scope=全域;有 scope=寫入該元件作用域、值從卡片元素解析
function tokenCtx(scope) {
    if (!scope) return { rc: resolveColor, rp: resolvePx, rr: resolveRaw, set: setToken, reg: (c) => controls.push(c) };
    const host = () => cardBodyByName[scope];
    return {
        rc: (t) => resolveColor(t, host()),
        rp: (t) => resolvePx(t, host()),
        rr: (t) => resolveRaw(t, host()),
        set: (t, v) => setScopedToken(scope, t, v),
        reg: () => {}
    };
}

/* ── token 編輯器設定(全用元件當控制項)── */
const COLOR_TOKENS = [
    ['--cl-primary', '主色'], ['--cl-success', '成功'], ['--cl-warning', '警告'],
    ['--cl-danger', '危險'], ['--cl-info', '資訊'], ['--cl-text', '內文'],
    ['--cl-text-secondary', '次要文字'], ['--cl-bg', '背景'], ['--cl-bg-secondary', '次背景'],
    ['--cl-border', '邊框']
];
const RADIUS_TOKENS = [['--cl-radius-sm', '小圓角', 0, 16], ['--cl-radius-md', '中圓角', 0, 24], ['--cl-radius-lg', '大圓角', 0, 32], ['--cl-radius-xl', '特大圓角', 0, 48]];
const FONT_SIZE_TOKENS = [['--cl-font-size-md', '正文', 10, 20], ['--cl-font-size-lg', '大', 10, 24], ['--cl-font-size-xl', '標題小', 12, 28], ['--cl-font-size-2xl', '標題', 14, 40]];
const RAW_TOKENS = [['--cl-shadow-md', '陰影(中)'], ['--cl-transition', '過渡'], ['--cl-font-family', '字體'], ['--cl-font-family-cjk', '中文字體'], ['--cl-font-family-mono', '等寬字體']];

const controls = [];   // 供重置時還原

function buildColorSection(scope) {
    const ctx = tokenCtx(scope);
    const box = document.createElement('div');
    box.className = 'ts-controls';
    COLOR_TOKENS.forEach(([token, label]) => {
        const row = document.createElement('div');
        row.style.cssText = 'display:flex; flex-direction:column; gap:4px;';
        const lab = document.createElement('label');
        lab.textContent = label;
        lab.style.cssText = 'font-size:var(--cl-font-size-md); color:var(--cl-text-secondary);';
        row.appendChild(lab);
        const cp = new ColorPicker({ value: ctx.rc(token), onChange: (c) => ctx.set(token, c) });
        (cp.mount ? cp.mount(row) : row.appendChild(cp.element));
        box.appendChild(row);
        ctx.reg({ token, set: (v) => cp.setValue && cp.setValue(v) });
    });
    return box;
}
function buildSliderSection(tokens, unit, scope) {
    const ctx = tokenCtx(scope);
    const box = document.createElement('div');
    box.className = 'ts-controls';
    tokens.forEach(([token, label, min, max]) => {
        const s = new Slider({ label, min, max, step: 1, value: ctx.rp(token), unit,
            onInput: (v) => ctx.set(token, v + unit) });
        s.mount(box);
        ctx.reg({ token, set: (v) => s.setValue(parseFloat(v)) });
    });
    return box;
}
function buildRawSection(scope) {
    const ctx = tokenCtx(scope);
    const box = document.createElement('div');
    box.className = 'ts-controls';
    RAW_TOKENS.forEach(([token, label]) => {
        const ti = new TextInput({ label, value: ctx.rr(token), onChange: (v) => ctx.set(token, v) });
        ti.mount(box);
        ctx.reg({ token, set: (v) => ti.setValue(v) });
    });
    return box;
}
function buildAdvancedSection() {
    const box = document.createElement('div');
    box.className = 'ts-controls';
    const ta = new TextArea({ label: '任意 token(JSON:{"--cl-x":"值"});套用可覆寫上面所有', rows: 8, monospace: true,
        placeholder: '{\n  "--cl-primary": "#3355ff"\n}' });
    ta.mount(box);
    const applyBtn = new BasicButton({ type: 'custom', customLabel: '套用 JSON', onClick: () => {
        try {
            const obj = JSON.parse(ta.getValue() || '{}');
            Object.entries(obj).forEach(([k, v]) => setToken(k.startsWith('--') ? k : '--' + k, v));
            Notification.success('已套用 JSON token');
        } catch (e) { Notification.error('JSON 解析失敗:' + e.message); }
    }});
    applyBtn.mount(box);
    controls.push({ token: '__json', set: () => ta.setValue('') });
    return box;
}

/* ── 儲存 / 匯出 / 匯入 ── */
function componentsExport() {
    const out = {};
    for (const [name, tw] of Object.entries(componentTweaks)) {
        if (tw.tokens && Object.keys(tw.tokens).length) out[name] = { className: tw.className, tokens: tw.tokens };
    }
    return out;
}
function tokensJson() {
    const components = componentsExport();
    const payload = { meta: { name: 'TimWeb Theme', generatedBy: 'Bricks4Agent Theme Studio' }, tokens: overrides };
    if (Object.keys(components).length) payload.components = components;
    return JSON.stringify(payload, null, 2);
}
function customCss() {
    const decl = (o) => Object.entries(o).map(([k, v]) => `    ${k}: ${v};`).join('\n');
    const parts = [
        '/*',
        ' * theme.custom.css — 由 Theme Studio 產生;載於 theme.css 之後覆蓋。',
        ' * 順序::root(全域)在前,各 .b4a-c-* 元件覆蓋在後 → cascade 讓元件覆蓋贏過全域。',
        ' */',
        ':root {', decl(overrides), '}'
    ];
    const comps = componentsExport();
    const names = Object.keys(comps);
    if (names.length) {
        parts.push('', '/* 各元件覆蓋:於正式站把對應 class 加到元件根元素即套用(同元件可建立多種具名變體)。*/');
        for (const name of names) {
            parts.push(`.${comps[name].className} {`, decl(comps[name].tokens), '}');
        }
    }
    return parts.join('\n') + '\n';
}
function download(filename, text, mime) {
    const blob = new Blob([text], { type: mime });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = filename; a.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
// 套回各元件覆蓋(載入/匯入用):重建 componentTweaks 並即時預覽到卡片
function applyComponents(compObj) {
    for (const [name, tw] of Object.entries(compObj || {})) {
        const className = tw.className || defaultClass(name);
        const tokens = tw.tokens || {};
        componentTweaks[name] = { className, tokens: { ...tokens } };
        const el = cardBodyByName[name];
        if (el) Object.entries(tokens).forEach(([t, v]) => el.style.setProperty(t, v));
    }
}
function saveLocal() {
    localStorage.setItem(LS_KEY, JSON.stringify({ tokens: overrides, components: componentsExport() }));
    Notification.success('已儲存到本機(localStorage)');
}
function loadLocal() {
    const raw = localStorage.getItem(LS_KEY);
    if (!raw) return;
    try {
        const data = JSON.parse(raw);
        const tokens = data.tokens || data;   // 向後相容舊格式(直接是 tokens 物件)
        Object.entries(tokens).forEach(([k, v]) => { if (k.startsWith('--')) { root.style.setProperty(k, v); overrides[k] = v; } });
        if (data.components) applyComponents(data.components);
    } catch {}
}
function applyImported(obj) {
    const tokens = obj.tokens || obj;
    Object.entries(tokens).forEach(([k, v]) => { if (typeof v === 'string') setToken(k.startsWith('--') ? k : '--' + k, v); });
    controls.forEach(c => { if (overrides[c.token] != null) c.set(overrides[c.token]); });
    if (obj.components) applyComponents(obj.components);
    Notification.success('已匯入 tokens' + (obj.components ? '(含元件覆蓋)' : ''));
}
function resetAll() {
    Object.keys(overrides).forEach(k => root.style.removeProperty(k));
    for (const k in overrides) delete overrides[k];
    Object.keys(componentTweaks).forEach(clearScope);   // 一併清掉各元件覆蓋(含卡片 inline 預覽)
    controls.forEach(c => { if (c.token.startsWith('--')) c.set(c.token.includes('font-size') || c.token.includes('radius') ? resolvePx(c.token) : (c.token.includes('cl-') && COLOR_TOKENS.some(t => t[0] === c.token) ? resolveColor(c.token) : resolveRaw(c.token))); });
    localStorage.removeItem(LS_KEY);
    Notification.info('已重置為預設');
}

/* ── 命令式回饋元件的可觸發展示(alert/message 類:自身無 mount,由 BasicButton 觸發)── */
function demoRow(body) {
    const row = document.createElement('div');
    row.style.cssText = 'display:flex; flex-wrap:wrap; gap:6px;';
    body.appendChild(row);
    return row;
}
const IMPERATIVE_DEMOS = {
    // 通知 toast:四種語意各一鈕
    Notification: (body) => {
        const row = demoRow(body);
        [['success', '成功'], ['error', '錯誤'], ['warning', '警告'], ['info', '資訊']].forEach(([type, label]) =>
            new BasicButton({ type: 'custom', customLabel: label, onClick: () => Notification[type](`這是${label}通知`) }).mount(row));
    },
    // 對話框:取代 alert/confirm/prompt
    SimpleDialog: (body) => {
        const row = demoRow(body);
        new BasicButton({ type: 'custom', customLabel: 'Alert', onClick: () => SimpleDialog.alert('這是提示訊息') }).mount(row);
        new BasicButton({ type: 'custom', customLabel: 'Confirm', onClick: () => SimpleDialog.confirm('確定要執行嗎?') }).mount(row);
        new BasicButton({ type: 'custom', customLabel: 'Prompt', onClick: () => SimpleDialog.prompt('請輸入:') }).mount(row);
    },
    // 提示泡泡:懸停附著於目標元素,故建個目標再 attach
    Tooltip: (body) => {
        const span = document.createElement('span');
        span.textContent = '懸停我看提示';
        span.style.cssText = 'display:inline-block; padding:6px 10px; border:1px dashed var(--cl-border); border-radius:var(--cl-radius-sm); cursor:help; color:var(--cl-text);';
        body.appendChild(span);
        new Tooltip({ text: '這是提示內容' }).attach(span);
    }
};

/* ── 可見標記(缺範例/不內嵌都現形,不靜默缺席)── */
function marker(body, text, variant, note) {
    const t = new Tag({ text, variant });
    t.render ? t.render(body) : (t.mount ? t.mount(body) : (body.textContent = text));
    if (note) {
        const n = document.createElement('div');
        n.textContent = note;
        n.style.cssText = 'margin-top:6px; font-size:var(--cl-font-size-xs); color:var(--cl-text-muted);';
        body.appendChild(n);
    }
}

/* ── 每卡 ⚙ 設定鈕 + 右側調校抽屜(個別調整該元件;作用域=具名 class)── */
function addGear(titleEl, name) {
    const g = document.createElement('button');
    g.type = 'button';
    g.title = '調校此元件的樣式';
    g.textContent = '⚙';
    g.style.cssText = 'border:none; background:transparent; cursor:pointer; font-size:14px; line-height:1; color:var(--cl-text-muted); padding:2px 4px; border-radius:var(--cl-radius-sm); flex:0 0 auto;';
    g.addEventListener('mouseenter', () => { g.style.color = 'var(--cl-primary)'; g.style.background = 'var(--cl-bg-secondary)'; });
    g.addEventListener('mouseleave', () => { g.style.color = 'var(--cl-text-muted)'; g.style.background = 'transparent'; });
    g.addEventListener('click', () => openTweakDrawer(name));
    titleEl.appendChild(g);
}
function openTweakDrawer(name) {
    const existing = document.getElementById('ts-drawer-overlay');
    if (existing) existing.remove();
    const tw = componentTweaks[name] || (componentTweaks[name] = { className: defaultClass(name), tokens: {} });

    const overlay = document.createElement('div');
    overlay.id = 'ts-drawer-overlay';
    overlay.style.cssText = 'position:fixed; inset:0; background:rgba(0,0,0,0.3); z-index:9000;';
    overlay.addEventListener('click', (e) => { if (e.target === overlay) overlay.remove(); });

    const panel = document.createElement('div');
    panel.style.cssText = 'position:absolute; top:0; right:0; width:340px; max-width:92vw; height:100%; overflow:auto; background:var(--cl-bg); border-left:1px solid var(--cl-border); box-shadow:var(--cl-shadow-lg, 0 4px 24px rgba(0,0,0,0.2)); padding:16px; box-sizing:border-box;';
    overlay.appendChild(panel);

    const head = document.createElement('div');
    head.style.cssText = 'display:flex; align-items:center; justify-content:space-between; margin-bottom:4px;';
    const h = document.createElement('div');
    h.textContent = '元件調校:' + name;
    h.style.cssText = 'font-size:var(--cl-font-size-lg); font-weight:600; color:var(--cl-text);';
    head.appendChild(h);
    new BasicButton({ type: 'custom', variant: 'secondary', customLabel: '關閉', onClick: () => overlay.remove() }).mount(head);
    panel.appendChild(head);

    const hint = document.createElement('div');
    hint.textContent = '調整只作用於此元件;匯出時併入 theme.custom.css 的 :root 之後,套用時把下方 class 加到該元件根元素。';
    hint.style.cssText = 'font-size:var(--cl-font-size-xs); color:var(--cl-text-muted); margin-bottom:10px; line-height:1.5;';
    panel.appendChild(hint);

    const clsWrap = document.createElement('div');
    new TextInput({ label: '套用的 class 名稱(可改名建立變體)', value: tw.className,
        onChange: (v) => { tw.className = (v || defaultClass(name)).trim().replace(/^\.+/, ''); } }).mount(clsWrap);
    panel.appendChild(clsWrap);

    const addSection = (titleText, node) => {
        const s = document.createElement('div');
        s.style.cssText = 'margin-top:12px;';
        const st = document.createElement('div');
        st.textContent = titleText;
        st.style.cssText = 'font-size:var(--cl-font-size-sm); font-weight:600; color:var(--cl-text-secondary); border-bottom:1px solid var(--cl-border-light); padding-bottom:4px; margin-bottom:8px;';
        s.appendChild(st);
        s.appendChild(node);
        panel.appendChild(s);
    };
    addSection('語意色', buildColorSection(name));
    addSection('圓角', buildSliderSection(RADIUS_TOKENS, 'px', name));
    addSection('字級', buildSliderSection(FONT_SIZE_TOKENS, 'px', name));
    addSection('效果 / 字體', buildRawSection(name));

    const clearWrap = document.createElement('div');
    clearWrap.style.cssText = 'margin-top:16px;';
    new BasicButton({ type: 'custom', variant: 'secondary', customLabel: '清除此元件覆蓋',
        onClick: () => { clearScope(name); overlay.remove(); Notification.info('已清除「' + name + '」的覆蓋'); } }).mount(clearWrap);
    panel.appendChild(clearWrap);

    document.body.appendChild(overlay);
}

/* ── 全尺寸「舞台」:點非內嵌卡片 → 大彈窗渲染,頂部可切換其他重元件(地圖/繪圖/大圖表/富文本)── */
let stageInst = null;
function renderStage(bodyEl, name, titleEl) {
    try { if (stageInst && typeof stageInst.destroy === 'function') stageInst.destroy(); } catch { /* best-effort */ }
    stageInst = null;
    bodyEl.innerHTML = '';
    bodyEl.id = 'ts-stage-body';
    if (titleEl) titleEl.textContent = name;
    try {
        const opts = { container: bodyEl, containerId: bodyEl.id, ...(STAGE_SAMPLES[name] || {}) };
        const inst = ComponentFactory.create(name, opts);
        if (!inst) throw new Error('factory null');
        if (bodyEl.childElementCount === 0) {
            if (typeof inst.mount === 'function') inst.mount(bodyEl);
            else if (inst.element) bodyEl.appendChild(inst.element);
            else if (typeof inst.render === 'function') inst.render(bodyEl);
            else throw new Error('no mount');
        }
        stageInst = inst;
        window.__stageInst = inst;   // 測試 hook:供自動化直接操作舞台實例
    } catch (e) {
        const box = document.createElement('div');
        box.style.cssText = 'padding:24px; color:var(--cl-text-muted); font-size:var(--cl-font-size-md);';
        box.textContent = '此元件目前無法在舞台渲染:' + e.message;
        bodyEl.appendChild(box);
    }
}
function openStage(name) {
    const prev = document.getElementById('ts-stage-overlay');
    if (prev) prev.remove();
    const names = Object.keys(STAGE_SAMPLES).sort();
    let idx = Math.max(0, names.indexOf(name));

    const overlay = document.createElement('div');
    overlay.id = 'ts-stage-overlay';
    overlay.style.cssText = 'position:fixed; inset:0; background:rgba(0,0,0,0.45); z-index:9500; display:flex; align-items:center; justify-content:center;';
    overlay.addEventListener('click', (e) => { if (e.target === overlay) overlay.remove(); });

    const modal = document.createElement('div');
    modal.style.cssText = 'width:88vw; height:82vh; background:var(--cl-bg); border-radius:var(--cl-radius-lg); box-shadow:var(--cl-shadow-lg, 0 8px 40px rgba(0,0,0,0.3)); display:flex; flex-direction:column; overflow:hidden;';
    overlay.appendChild(modal);

    const bar = document.createElement('div');
    bar.style.cssText = 'display:flex; align-items:center; gap:8px; padding:10px 14px; border-bottom:1px solid var(--cl-border); flex-wrap:wrap;';
    const lbl = document.createElement('span');
    lbl.textContent = '舞台:';
    lbl.style.cssText = 'font-weight:600; color:var(--cl-text);';
    bar.appendChild(lbl);
    const cur = document.createElement('strong');   // 目前元件名(renderStage 會更新)
    cur.style.cssText = 'color:var(--cl-primary); min-width:110px;';

    const stageBody = document.createElement('div');
    stageBody.style.cssText = 'flex:1; overflow:auto; padding:12px; background:var(--cl-bg-secondary); position:relative;';

    const go = (i) => { idx = (i + names.length) % names.length; if (dd.setValue) dd.setValue(names[idx]); renderStage(stageBody, names[idx], cur); };
    const dd = new Dropdown({ items: names.map(n => ({ label: n, value: n })), value: names[idx],
        onChange: (v) => { idx = names.indexOf(v); renderStage(stageBody, v, cur); } });
    dd.mount(bar);
    bar.appendChild(cur);
    new BasicButton({ type: 'custom', variant: 'secondary', customLabel: '‹ 上一個', onClick: () => go(idx - 1) }).mount(bar);
    new BasicButton({ type: 'custom', variant: 'secondary', customLabel: '下一個 ›', onClick: () => go(idx + 1) }).mount(bar);
    const spacer = document.createElement('span'); spacer.style.cssText = 'margin-left:auto;'; bar.appendChild(spacer);
    new BasicButton({ type: 'custom', customLabel: '關閉', onClick: () => overlay.remove() }).mount(bar);
    modal.appendChild(bar);
    modal.appendChild(stageBody);

    document.body.appendChild(overlay);
    renderStage(stageBody, names[idx], cur);
}

/* ── 元件展示廊(自舉;掃「全 catalog」→ 有範例即渲染、命令式給觸發鈕、其餘現形為待補/不內嵌)── */
async function buildGallery(host) {
    let catalog;
    try { catalog = await (await fetch(`${LIB}/metadata/component-catalog.json`)).json(); } catch { catalog = { components: [] }; }
    const comps = catalog.components || [];
    const byCat = {};
    for (const c of comps) (byCat[c.category || 'other'] ??= []).push(c.registry_name);

    const stat = { ok: 0, demo: 0, skip: 0, todo: 0, stage: 0, total: comps.length };
    for (const cat of Object.keys(byCat).sort()) {
        const section = document.createElement('section');
        const h = document.createElement('h3');
        h.textContent = cat + ` (${byCat[cat].length})`;
        h.style.cssText = 'margin:16px 0 8px; color:var(--cl-text); font-size:var(--cl-font-size-lg); border-bottom:1px solid var(--cl-border); padding-bottom:4px;';
        section.appendChild(h);
        const grid = document.createElement('div');
        grid.style.cssText = 'display:grid; grid-template-columns:repeat(auto-fill,minmax(220px,1fr)); gap:12px;';
        // 先把 section/grid 掛進活的 DOM —— containerId 自渲染的元件建構時會 getElementById,目標須已在 document 內
        section.appendChild(grid);
        host.appendChild(section);
        for (const name of byCat[cat].sort()) {
            const card = document.createElement('div');
            card.style.cssText = 'border:1px solid var(--cl-border-light); border-radius:var(--cl-radius-md); padding:12px; background:var(--cl-bg); min-height:80px;';
            const title = document.createElement('div');
            title.style.cssText = 'display:flex; align-items:center; justify-content:space-between; gap:6px; margin-bottom:8px;';
            const nameSpan = document.createElement('span');
            nameSpan.textContent = name;
            nameSpan.style.cssText = 'font-size:var(--cl-font-size-sm); color:var(--cl-text-muted); overflow:hidden; text-overflow:ellipsis; white-space:nowrap;';
            title.appendChild(nameSpan);
            card.appendChild(title);
            const body = document.createElement('div');
            body.id = `ts-cell-${name}`;
            card.appendChild(body);
            grid.appendChild(card);   // body 進 DOM 後才實例化

            // 1) 命令式回饋元件(alert/message):給實際可觸發的展示鈕
            if (IMPERATIVE_DEMOS[name]) {
                try { IMPERATIVE_DEMOS[name](body); stat.demo++; }
                catch (e) { marker(body, '展示失敗', 'danger', e.message); stat.skip++; }
                continue;
            }
            // 2) 明確不內嵌(地圖/繪圖/大圖/基礎設施):可上舞台者給「開啟舞台」鈕,否則純說明
            if (GALLERY_SKIP.has(name)) {
                if (STAGE_SAMPLES[name]) {
                    marker(body, '非內嵌展示', 'info');
                    const openBtn = document.createElement('button');
                    openBtn.type = 'button';
                    openBtn.textContent = '↗ 開啟舞台';
                    openBtn.title = '在全尺寸舞台渲染(可頂部切換其他重元件)';
                    openBtn.style.cssText = 'margin-top:8px; border:1px solid var(--cl-primary); background:transparent; color:var(--cl-primary); cursor:pointer; font-size:var(--cl-font-size-sm); padding:4px 10px; border-radius:var(--cl-radius-sm);';
                    openBtn.addEventListener('click', () => openStage(name));
                    body.appendChild(openBtn);
                    stat.stage++;
                } else {
                    marker(body, '非內嵌展示', 'info', '需獨立畫布/服務');
                }
                stat.skip++;
                continue;
            }
            // 3) 有範例:實例化(container 元素 + containerId 兩種慣例都給)
            if (SAMPLE_OPTIONS[name]) {
                try {
                    const opts = { container: body, containerId: body.id, ...SAMPLE_OPTIONS[name] };
                    const inst = ComponentFactory.create(name, opts);
                    if (!inst) throw new Error('factory null');
                    if (body.childElementCount === 0) {
                        if (typeof inst.mount === 'function') inst.mount(body);
                        else if (inst.element) body.appendChild(inst.element);
                        else if (typeof inst.render === 'function') inst.render(body);
                        else throw new Error('no mount');
                    }
                    cardBodyByName[name] = body;   // 即時預覽的作用域錨點
                    addGear(title, name);          // 該元件可個別調校
                    stat.ok++;
                } catch (e) { marker(body, '需手動設定', 'warning', e.message); stat.skip++; }
                continue;
            }
            // 4) 未分流:待補範例 —— 現形(這就是覆蓋缺口,自舉的意義所在)
            marker(body, '待補範例', 'warning');
            stat.todo++;
        }
    }
    return stat;
}

/* ── 組裝畫面 ── */
async function main() {
    const app = document.getElementById('app');

    // Header + 動作列(全用元件)
    const header = document.createElement('header');
    header.style.cssText = 'display:flex; align-items:center; gap:8px; flex-wrap:wrap; padding:12px 16px; border-bottom:2px solid var(--cl-border); background:var(--cl-bg);';
    const titleWrap = document.createElement('div');
    titleWrap.style.cssText = 'font-size:var(--cl-font-size-xl); font-weight:600; color:var(--cl-text); margin-right:auto;';
    titleWrap.textContent = 'Theme Studio — 全域視覺調校';
    header.appendChild(titleWrap);

    const darkToggle = new ToggleSwitch({ label: '深色', onChange: (on) => root.setAttribute('data-theme', on ? 'dark' : '') });
    darkToggle.mount(header);
    new BasicButton({ type: 'custom', customLabel: '儲存', onClick: saveLocal }).mount(header);
    new DownloadButton({ type: 'json', showLabel: true, tooltip: '匯出 theme.tokens.json', onClick: () => download('theme.tokens.json', tokensJson(), 'application/json') }).mount(header);
    new DownloadButton({ type: 'css', showLabel: true, tooltip: '匯出 theme.custom.css', onClick: () => download('theme.custom.css', customCss(), 'text/css') }).mount(header);
    new UploadButton({ label: '匯入 tokens', accept: '.json', onSelect: async (files) => {
        const f = files && files[0]; if (!f) return;
        try { applyImported(JSON.parse(await f.text())); } catch (e) { Notification.error('匯入失敗:' + e.message); }
    }}).mount(header);
    new BasicButton({ type: 'custom', variant: 'secondary', customLabel: '重置', onClick: resetAll }).mount(header);
    app.appendChild(header);

    // 主體:左 token 編輯器(TabContainer)+ 右 gallery
    const body = document.createElement('div');
    body.style.cssText = 'display:grid; grid-template-columns:360px 1fr; gap:0; height:calc(100vh - 60px);';
    const left = document.createElement('div');
    left.id = 'ts-tabs';
    left.style.cssText = 'overflow:auto; border-right:1px solid var(--cl-border); padding:12px; background:var(--cl-bg-secondary);';
    const right = document.createElement('div');
    right.style.cssText = 'overflow:auto; padding:16px; background:var(--cl-bg-tertiary);';
    body.appendChild(left); body.appendChild(right);
    app.appendChild(body);

    // TabContainer 用 containerId 自渲染(需 left 已在 DOM);tab 需 {id,title,content}
    new TabContainer({
        containerId: 'ts-tabs',
        tabs: [
            { id: 'colors', title: '語意色', content: buildColorSection() },
            { id: 'radius', title: '圓角', content: buildSliderSection(RADIUS_TOKENS, 'px') },
            { id: 'fontsize', title: '字級', content: buildSliderSection(FONT_SIZE_TOKENS, 'px') },
            { id: 'effects', title: '效果/字體', content: buildRawSection() },
            { id: 'advanced', title: '進階 JSON', content: buildAdvancedSection() }
        ]
    });

    const stat = await buildGallery(right);

    loadLocal();
    controls.forEach(c => { if (overrides[c.token] != null) c.set(overrides[c.token]); });
    Notification.info(`Theme Studio 就緒:${stat.total} 元件 — 渲染 ${stat.ok}、觸發展示 ${stat.demo}、可上舞台 ${stat.stage}、不內嵌/手動 ${stat.skip - stat.stage}` + (stat.todo ? `、待補範例 ${stat.todo}` : ''));

    // 測試 hook(供自動化驗證)
    window.__ts = { setToken, setScopedToken, resolveColor, overrides, componentTweaks, cardBodyByName, tokensJson, customCss, openStage, gallery: stat };
    window.__studioReady = true;
}

// 安全網:studio 就緒後,來自元件內部(非同步)的錯誤不應讓工具崩潰 —— 記錄但吞掉
window.addEventListener('error', (ev) => {
    if (window.__studioReady && ev.error && /ui_components\//.test(String(ev.error.stack || ''))) {
        (window.__galleryErrors ??= []).push(String(ev.error.message));
        ev.preventDefault();
    }
}, true);

main().catch(e => { window.__studioError = String(e && e.stack || e); });
