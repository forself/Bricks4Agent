// security.js 純函式安全測試 —— 零依賴,直接 node 執行:
//   node packages/javascript/browser/ui_components/utils/security.id-url.test.mjs
// 涵蓋 XSS/開放重定向修補的來源端原語:
//   - isSafeId:id 字元白名單(TOC/錨點 id 收斂的單一事實來源)
//   - sanitizeUrl:協定相對 URL(含反斜線變體)阻擋
//   - escapeAttr:屬性脈絡跳脫(TOC 內插的縱深防禦)
// (需 DOM 的 sanitizeHTML / WebTextEditor TOC 測試在 tim-web 以 vitest+jsdom 執行,
//  見 TimWeb/tests/unit/frontend-security-boundaries.test.mjs)
import assert from 'node:assert';
import { isSafeId, sanitizeUrl, escapeAttr } from './security.js';

let pass = 0, fail = 0;
const t = (name, fn) => { try { fn(); pass++; console.log('  ok  ' + name); } catch (e) { fail++; console.log('  FAIL ' + name + ' — ' + e.message); } };

console.log('== isSafeId:id 字元白名單 ==');
t('英數/底線/連字號合法', () => assert.ok(isSafeId('wte-heading-wte-1-0')));
t('純字母合法', () => assert.ok(isSafeId('introduction')));
t('大小寫+數字合法', () => assert.ok(isSafeId('Section_12-A')));
t('空字串非法', () => assert.ok(!isSafeId('')));
t('含空白非法', () => assert.ok(!isSafeId('a b')));
t('含引號非法', () => assert.ok(!isSafeId('a"b')));
t('含角括號非法', () => assert.ok(!isSafeId('a<b>')));
t('屬性跳脫 payload 非法', () => assert.ok(!isSafeId('a"><img src=x onerror=alert(1)>')));
t('含冒號(CSS/協定)非法', () => assert.ok(!isSafeId('a:b')));
t('非字串非法', () => { assert.ok(!isSafeId(null)); assert.ok(!isSafeId(undefined)); assert.ok(!isSafeId(123)); });

console.log('== sanitizeUrl:協定相對 URL 阻擋(含反斜線繞過) ==');
t('// 協定相對 → 空', () => assert.equal(sanitizeUrl('//evil.example/x'), ''));
t('/\\ 反斜線變體 → 空', () => assert.equal(sanitizeUrl('/\\evil.example/x'), ''));
t('\\/ 反斜線變體 → 空', () => assert.equal(sanitizeUrl('\\/evil.example/x'), ''));
t('\\\\ 雙反斜線 → 空', () => assert.equal(sanitizeUrl('\\\\evil.example/x'), ''));
t('單一斜線相對路徑保留', () => assert.equal(sanitizeUrl('/timweb/list'), '/timweb/list'));
t('錨點保留', () => assert.equal(sanitizeUrl('#section-1'), '#section-1'));
t('https 保留', () => assert.equal(sanitizeUrl('https://ok.example/x'), 'https://ok.example/x'));
t('mailto 保留', () => assert.equal(sanitizeUrl('mailto:a@b.c'), 'mailto:a@b.c'));
t('javascript: 協定 → 空', () => assert.equal(sanitizeUrl('javascript:alert(1)'), ''));

console.log('== escapeAttr:屬性脈絡跳脫 ==');
t('雙引號被跳脫', () => assert.ok(!escapeAttr('a"b').includes('"')));
t('單引號被跳脫', () => assert.ok(!escapeAttr("a'b").includes("'")));
t('角括號被跳脫', () => { const r = escapeAttr('a<b>'); assert.ok(!r.includes('<') && !r.includes('>')); });
t('屬性跳脫 payload 無原始引號/角括號殘留', () => {
    const r = escapeAttr('a"><img src=x onerror=alert(1)>');
    assert.ok(!r.includes('"') && !r.includes('<') && !r.includes('>'));
});

console.log(`\n結果: ${pass} passed, ${fail} failed`);
process.exit(fail ? 1 : 0);
