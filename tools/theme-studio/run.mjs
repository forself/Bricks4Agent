// Theme Studio 真實 Edge 冒煙測試(用 tim-web/poc 既有 playwright-core)。
// 前置:於 Bricks4Agent 根啟動 python -m http.server 8124 --bind 127.0.0.1
import pkg from '../../../tim-web/poc/node_modules/playwright-core/index.js';
const { chromium } = pkg;

const browser = await chromium.launch({ channel: 'msedge', headless: true });
const page = await browser.newPage();
const fatal = [];   // pageerror / 資源 404:致命
const noise = [];   // 被 ComponentFactory 攔截的元件實例化警告:非致命(卡片顯示「需手動設定」)
page.on('pageerror', e => fatal.push('pageerror: ' + e.message + '\n  ' + String(e.stack || '').split('\n').slice(1, 3).join('\n  ')));
page.on('response', r => { if (r.status() >= 400) fatal.push('HTTP ' + r.status() + ': ' + r.url()); });
page.on('console', m => {
    if (m.type() !== 'error') return;
    const txt = m.text();
    // 唯一容許的噪音:ComponentFactory 對「無明確範例」元件的實例化警告(卡片降級為「需手動設定」)。
    // 「Container not found」不再放行 —— 那代表自渲染元件(containerId)的容器缺失,是真 bug。
    if (/\[ComponentFactory\]/.test(txt)) noise.push(txt);
    else fatal.push('console: ' + txt);
});

await page.goto('http://127.0.0.1:8124/tools/theme-studio/index.html');
await page.waitForFunction('window.__studioReady === true || window.__studioError', { timeout: 12000 }).catch(() => {});

const results = [];
const t = (name, pass, detail) => results.push({ name, pass: !!pass, detail });

const err = await page.evaluate('window.__studioError || null');
t('studio 無致命錯誤啟動', !err, err);
if (err) {
    console.log('  FAIL studio 啟動錯誤:\n' + err);
    if (fatal.length) console.log('[頁面訊息]\n' + fatal.join('\n'));
    await browser.close();
    process.exit(1);
}
const gallery = await page.evaluate('(window.__ts && window.__ts.gallery) || null');
t('gallery 展示元件 > 15', gallery && gallery.ok > 15, JSON.stringify(gallery));
t('gallery 覆蓋全 catalog(無靜默缺席)', gallery && (gallery.ok + gallery.demo + gallery.skip + gallery.todo) === gallery.total, JSON.stringify(gallery));
t('alert/message 類有觸發展示(demo>=2)', gallery && gallery.demo >= 2, 'demo=' + (gallery && gallery.demo));
t('無致命錯誤(pageerror/404)', fatal.length === 0, fatal.slice(0, 4).join(' | '));

// 左側 token 編輯器渲染(ColorPicker/Slider/textarea 由元件產生)
const controlCount = await page.evaluate(`document.querySelectorAll('#app input, #app [class*="color"], #app [class*="slider"]').length`);
t('token 控制項有渲染', controlCount > 5, 'controls=' + controlCount);

// 即時生效:改 --cl-primary 為純紅 → gallery 內某元件的顏色跟著變
const liveOk = await page.evaluate(`(() => {
  const before = getComputedStyle(document.documentElement).getPropertyValue('--cl-primary').trim();
  window.__ts.setToken('--cl-primary', 'rgb(255, 0, 0)');
  const after = getComputedStyle(document.documentElement).getPropertyValue('--cl-primary').trim();
  return after === 'rgb(255, 0, 0)' && after !== before;
})()`);
t('改 token 即時生效(--cl-primary)', liveOk);

// 匯出:tokens.json 與 custom.css 含剛改的 override
const exp = await page.evaluate(`({ json: window.__ts.tokensJson(), css: window.__ts.customCss() })`);
t('匯出 tokens.json 含 override', /--cl-primary/.test(exp.json) && /rgb\(255, 0, 0\)/.test(exp.json), '');
t('匯出 custom.css 含 :root override', /:root/.test(exp.css) && /--cl-primary:\s*rgb\(255, 0, 0\)/.test(exp.css), '');

// 個別元件覆蓋:設 BasicButton 的 --cl-radius-md,匯出應含 .b4a-c-BasicButton{}(且在 :root 之後)
const scoped = await page.evaluate(`(() => {
  window.__ts.setScopedToken('BasicButton', '--cl-radius-md', '2px');
  const css = window.__ts.customCss();
  const json = window.__ts.tokensJson();
  const rootIdx = css.indexOf(':root');
  const clsIdx = css.indexOf('.b4a-c-BasicButton');
  const live = getComputedStyle(document.getElementById('ts-cell-BasicButton')).getPropertyValue('--cl-radius-md').trim();
  return { css, json, rootIdx, clsIdx, live };
})()`);
t('元件覆蓋即時預覽(卡片 --cl-radius-md=2px)', scoped.live === '2px', 'live=' + scoped.live);
t('匯出 custom.css 含 .b4a-c-BasicButton 且在 :root 之後', scoped.clsIdx > scoped.rootIdx && /\.b4a-c-BasicButton\s*\{[^}]*--cl-radius-md:\s*2px/.test(scoped.css), `root=${scoped.rootIdx} cls=${scoped.clsIdx}`);
t('tokens.json 含 components.BasicButton', /"components"/.test(scoped.json) && /"BasicButton"/.test(scoped.json) && /"--cl-radius-md":\s*"2px"/.test(scoped.json), '');

// 舞台:非內嵌重元件可開全尺寸彈窗渲染(用 OrgChart:self-container、免外部服務)
t('gallery 可上舞台數 >= 10', gallery && gallery.stage >= 10, 'stage=' + (gallery && gallery.stage));
const stg = await page.evaluate(`(async () => {
  window.__ts.openStage('OrgChart');
  await new Promise(r => setTimeout(r, 700));
  const overlay = document.getElementById('ts-stage-overlay');
  const body = document.getElementById('ts-stage-body');
  const rendered = !!body && (!!body.querySelector('svg') || body.childElementCount > 0);
  if (overlay) overlay.remove();
  return { open: !!overlay, rendered };
})()`);
t('舞台開啟並渲染非內嵌重元件(OrgChart)', stg.open && stg.rendered, JSON.stringify(stg));

await page.evaluate(`window.__ts.setToken('--cl-radius-md','14px'); window.__ts.setToken('--cl-primary','rgb(124,58,237)')`);
await page.screenshot({ path: 'screenshot.png', fullPage: false });
await browser.close();
let pass = 0, fail = 0;
for (const r of results) { if (r.pass) { pass++; console.log('  ok  ' + r.name); } else { fail++; console.log('  FAIL ' + r.name + ' — ' + (r.detail || '')); } }
if (noise.length) console.log(`\n[非致命:${noise.length} 個元件需手動 container(gallery 已優雅降級)]`);
console.log(`\n結果: ${pass} passed, ${fail} failed`);
process.exit(fail ? 1 : 0);
