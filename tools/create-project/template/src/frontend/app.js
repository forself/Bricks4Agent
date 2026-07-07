/**
 * {{PROJECT_NAME}} — 起始頁(可整檔替換)。
 * 示範三件事:lib 相對深度(src/frontend/ → ../../lib/)、元件掛載慣例、escapeHtml。
 * 規則:只用 lib/ui_components(缺元件回上游補);禁根絕對 /lib/、禁 ../Bricks4Agent/(publish 驗證器會擋)。
 */
import { BasicButton, Notification, Tag, escapeHtml } from '../../lib/ui_components/index.js';

const app = document.getElementById('app');
const shell = document.createElement('div');
shell.className = 'app-shell';
app.appendChild(shell);

const h1 = document.createElement('h1');
h1.textContent = '{{PROJECT_NAME}}';
h1.style.cssText = 'margin:0; font-size:var(--cl-font-size-2xl); color:var(--cl-text);';
shell.appendChild(h1);

const p = document.createElement('p');
// 動態字串一律 escapeHtml(這裡是常量,僅示範慣例)
p.innerHTML = escapeHtml('腳手架就緒:開發=junction 直連 Bricks4Agent,發佈= scripts\\publish.ps1 產密封 dist\\。');
p.style.cssText = 'margin:0; color:var(--cl-text-secondary);';
shell.appendChild(p);

// 注意:各元件掛載 API 依其類別(Tag=render、BasicButton=mount);見 AGENT-UI-GUIDE
new Tag({ text: 'Bricks4Agent', variant: 'primary' }).render(shell);
new BasicButton({
    type: 'custom',
    customLabel: '測試通知',
    onClick: () => Notification.success('元件庫連通:theme 鏈與模組載入正常')
}).mount(shell);

// 測試 hook(冒煙腳本用)
window.__appReady = true;
