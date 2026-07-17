// Icon Canvas 真實瀏覽器冒煙(Edge/playwright-core,借 tim-web/poc 依賴)。
// 前置:於 repo 根目錄提供 http://127.0.0.1:8124。
import pkg from '../../../tim-web/poc/node_modules/playwright-core/index.js';
const { chromium } = pkg;

const browser = await chromium.launch({ channel: 'msedge', headless: true });
const page = await browser.newPage({ viewport: { width: 640, height: 480 }, deviceScaleFactor: 1.5 });
const errors = [];
page.on('pageerror', error => errors.push('pageerror: ' + error.message));
page.on('console', message => {
    if (message.type() === 'error') errors.push('console: ' + message.text());
});

await page.goto('http://127.0.0.1:8124/tools/theme-studio/index.html');
await page.waitForFunction(
    globalName => window[globalName] === true,
    '__studioReady',
    { timeout: 15000 }
);
await page.evaluate(async modulePath => {
    const { Icon } = await import(modulePath);
    const host = document.createElement('div');
    host.id = 'icon-smoke';
    host.style.cssText = 'position:fixed;left:0;top:0;padding:20px;background:var(--cl-bg);z-index:99999;color:rgb(20,30,40)';
    document.body.appendChild(host);
    window.__iconClicks = 0;
    window.__icons = [
        new Icon({ name: 'search', size: 'sm', color: 'var(--cl-primary)', title: '搜尋', onClick: () => window.__iconClicks++ }),
        new Icon({ name: 'edit', size: 'md', color: 'currentColor' }),
        new Icon({ name: 'refresh', size: 'lg', spin: true }),
        new Icon({ name: 'delete', size: 31 })
    ];
    Icon.register('smoke-custom', 'M2 2h20v20H2z');
    window.__customIcon = new Icon({ name: 'smoke-custom', size: 18, color: 'rgb(10, 200, 30)' });
    for (const icon of [...window.__icons, window.__customIcon]) icon.mount(host);
}, '/packages/javascript/browser/ui_components/common/Icon/Icon.js');
await page.waitForSelector('#icon-smoke .cl-icon canvas', { timeout: 8000 });

const results = [];
const test = (name, pass, detail = '') => results.push({ name, pass: !!pass, detail });
const snapshot = await page.evaluate(`(() => ({
  canvases: document.querySelectorAll('#icon-smoke canvas').length,
  svgs: document.querySelectorAll('#icon-smoke svg').length,
  sizes: [...document.querySelectorAll('#icon-smoke canvas')].map(c => [c.clientWidth, c.clientHeight, c.width, c.height]),
  role: document.querySelector('#icon-smoke .cl-icon').getAttribute('role'),
  aria: document.querySelector('#icon-smoke .cl-icon').getAttribute('aria-label'),
  animations: document.querySelectorAll('#icon-smoke canvas')[2].getAnimations().length
}))()`);
test('五個 Icon 全以 canvas 繪製', snapshot.canvases === 5 && snapshot.svgs === 0, JSON.stringify(snapshot));
test('sm/md/lg/數字尺寸相容', [16, 20, 24, 31, 18].every((n, i) => snapshot.sizes[i][0] === n && snapshot.sizes[i][1] === n), JSON.stringify(snapshot.sizes));
test('DPR 背景儲存縮放', snapshot.sizes.every(s => Math.abs(s[2] - s[0] * 1.5) <= 1), JSON.stringify(snapshot.sizes));
test('點擊 Icon 有 button/aria 語意', snapshot.role === 'button' && snapshot.aria === '搜尋', JSON.stringify(snapshot));
test('spin 使用 Web Animations API', snapshot.animations === 1, 'animations=' + snapshot.animations);

await page.click('#icon-smoke .cl-icon');
await page.focus('#icon-smoke .cl-icon');
await page.keyboard.press('Enter');
await page.keyboard.press('Space');
test('滑鼠與鍵盤點擊回呼', await page.evaluate('window.__iconClicks') === 3, 'clicks=' + await page.evaluate('window.__iconClicks'));

const before = await page.evaluate(`(() => [...document.querySelector('#icon-smoke canvas').getContext('2d').getImageData(0,0,24,24).data].reduce((a,v)=>a+v,0))()`);
await page.evaluate(`document.documentElement.style.setProperty('--cl-primary', 'rgb(240, 10, 20)')`);
await page.waitForTimeout(100);
const red = await page.evaluate(`(() => { const c=document.querySelector('#icon-smoke canvas'),d=c.getContext('2d').getImageData(0,0,c.width,c.height).data;let r=0,g=0,b=0,a=0;for(let i=0;i<d.length;i+=4){if(d[i+3]){r+=d[i];g+=d[i+1];b+=d[i+2];a++;}}return {r:r/a,g:g/a,b:b/a,a}; })()`);
test('ThemeBus token 變更後重繪為紅色', before > 0 && red.a > 0 && red.r > 180 && red.g < 80, JSON.stringify(red));

await page.evaluate(`window.__icons[1].setName('close'); window.__icons[1].setColor('rgb(1,2,220)'); window.__icons[1].setSize('lg')`);
const mutated = await page.evaluate(`(() => { const c=document.querySelectorAll('#icon-smoke canvas')[1];return {w:c.clientWidth,h:c.clientHeight,nonempty:[...c.getContext('2d').getImageData(0,0,c.width,c.height).data].some(Boolean)}; })()`);
test('setName/setColor/setSize 可重繪', mutated.w === 24 && mutated.h === 24 && mutated.nonempty, JSON.stringify(mutated));

const delayedSync = await page.evaluate(`(() => {
  const detachedButton = document.createElement('button');
  detachedButton.id = 'delayed-icon-host';
  detachedButton.style.color = 'var(--cl-primary)';
  window.__delayedIcon = new window.__icons[0].constructor({ name: 'check', size: 20 });
  window.__delayedIcon.mount(detachedButton);
  document.documentElement.style.setProperty('--cl-primary', 'rgb(15, 180, 35)');
  document.body.appendChild(detachedButton);
  const c=detachedButton.querySelector('canvas'),d=c.getContext('2d').getImageData(0,0,c.width,c.height).data;
  return [...d].some(Boolean);
})()`);
test('外層 mount 返回時 Canvas 已同步繪製', delayedSync, `nonempty=${delayedSync}`);
await page.waitForTimeout(100);
const delayedBefore = await page.evaluate(`(() => { const c=document.querySelector('#delayed-icon-host canvas'),d=c.getContext('2d').getImageData(0,0,c.width,c.height).data;let r=0,g=0,b=0,n=0;for(let i=0;i<d.length;i+=4){if(d[i+3]){r+=d[i];g+=d[i+1];b+=d[i+2];n++;}}return {r:r/n,g:g/n,b:b/n}; })()`);
test('detached parent 連入 DOM 後首次 currentColor 正確', delayedBefore.g > 140 && delayedBefore.r < 80, JSON.stringify(delayedBefore));
await page.evaluate(`document.documentElement.style.setProperty('--cl-primary', 'rgb(25, 45, 220)')`);
await page.waitForTimeout(100);
const delayedAfter = await page.evaluate(`(() => { const c=document.querySelector('#delayed-icon-host canvas'),d=c.getContext('2d').getImageData(0,0,c.width,c.height).data;let r=0,g=0,b=0,n=0;for(let i=0;i<d.length;i+=4){if(d[i+3]){r+=d[i];g+=d[i+1];b+=d[i+2];n++;}}return {r:r/n,g:g/n,b:b/n}; })()`);
test('mount 前 theme 變更不會退訂 ThemeBus', delayedAfter.b > 170 && delayedAfter.r < 80, JSON.stringify(delayedAfter));

await page.evaluate(`window.__customIcon.destroy()`);
test('destroy 移除 DOM', await page.evaluate(`document.querySelectorAll('#icon-smoke canvas').length`) === 4);
test('全程無瀏覽器錯誤', errors.length === 0, errors.join(' | '));

await page.evaluate(`window.__delayedIcon.destroy(); document.querySelector('#delayed-icon-host')?.remove(); document.documentElement.style.removeProperty('--cl-primary')`);
await browser.close();
let failed = 0;
for (const result of results) {
    if (!result.pass) failed++;
    console.log(`  ${result.pass ? 'ok ' : 'FAIL '} ${result.name}${result.pass ? '' : ' — ' + result.detail}`);
}
console.log(`\n結果: ${results.length - failed} passed, ${failed} failed`);
process.exit(failed ? 1 : 0);
