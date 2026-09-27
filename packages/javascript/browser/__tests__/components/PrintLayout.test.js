import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import Locale from '../../ui_components/i18n/index.js';
import { Icon } from '../../ui_components/common/Icon/Icon.js';
import { PrintLayout } from '../../ui_components/layout/PrintLayout/PrintLayout.js';
import PrintLayoutDefault, { PrintLayout as PrintLayoutNamed } from '../../ui_components/layout/PrintLayout/index.js';

const testDir = path.dirname(fileURLToPath(import.meta.url));
const cssSource = fs.readFileSync(path.resolve(testDir, '../../ui_components/layout/PrintLayout/PrintLayout.css'), 'utf8');
const TIME_STYLE = { dateStyle: 'medium', timeStyle: 'short' };
const LINK_ID = 'b4a-print-layout-styles';

const styleLinks = () => [...document.querySelectorAll(`#${LINK_ID}`)];
const printMarks = () => ({
    printing: document.documentElement.hasAttribute('data-b4a-printing'),
    targets: document.querySelectorAll('[data-b4a-print-target]').length,
    ancestors: document.querySelectorAll('[data-b4a-print-ancestor]').length
});

const originalPrint = window.print;

let host;
let created;
let printSpy;

function make(options = {}, target = host) {
    const layout = new PrintLayout({ title: '會議室使用月報', ...options });
    if (target) layout.mount(target);
    created.push(layout);
    return layout;
}

beforeEach(() => {
    Locale.setLang('zh-TW');
    host = document.createElement('div');
    host.className = 'page-main';
    document.body.appendChild(host);
    created = [];
    printSpy = vi.fn();
    window.print = printSpy;
});

afterEach(() => {
    created.forEach((layout) => layout.destroy());
    host.remove();
    window.print = originalPrint;
    vi.useRealTimers();
    vi.restoreAllMocks();
    Locale.setLang('zh-TW');
});

describe('PrintLayout — 結構', () => {
    it('index.js 匯出具名與預設', () => {
        expect(PrintLayoutNamed).toBe(PrintLayout);
        expect(PrintLayoutDefault).toBe(PrintLayout);
    });

    it('預設：A4 直向 15mm，標題為 h2，列印時間依 Locale 格式', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2026-09-27T06:30:00.000Z'));
        const layout = make({ subtitle: '2026 年 9 月' });
        const root = layout.element;
        expect(root.classList.contains('b4a-print-layout')).toBe(true);
        expect(root.dataset.pageSize).toBe('A4');
        expect(root.dataset.orientation).toBe('portrait');
        expect(root.dataset.margin).toBe('15mm');
        expect(root.dataset.preview).toBe('false');
        expect(root.getAttribute('data-b4a-page')).toBe('a4-portrait-15mm');
        expect(root.querySelector('h2.b4a-print-layout__title').textContent).toBe('會議室使用月報');
        expect(root.querySelector('.b4a-print-layout__subtitle').textContent).toBe('2026 年 9 月');
        const expected = new Intl.DateTimeFormat('zh-TW', TIME_STYLE).format(new Date('2026-09-27T06:30:00.000Z'));
        expect(root.querySelector('.b4a-print-layout__printed-at').textContent).toBe(`列印時間：${expected}`);
        expect(root.querySelector('.b4a-print-layout__footer').hidden).toBe(true);
        expect(root.querySelector('.b4a-print-layout__toolbar')).toBeNull();
    });

    it('header／footer／content 接受 Node、B4A 元件或文字（文字不解析 HTML）', () => {
        const headerNode = document.createElement('div');
        headerNode.className = 'custom-header';
        const component = { element: document.createElement('section') };
        component.element.className = 'custom-footer';
        const layout = make({ header: headerNode, footer: component, content: '<b>純文字</b>' });
        expect(layout.element.querySelector('.b4a-print-layout__header-extra .custom-header')).toBe(headerNode);
        const footer = layout.element.querySelector('.b4a-print-layout__footer');
        expect(footer.hidden).toBe(false);
        expect(footer.querySelector('.custom-footer')).toBe(component.element);
        expect(layout.getContentElement().textContent).toBe('<b>純文字</b>');
        expect(layout.getContentElement().querySelector('b')).toBeNull();
        const textual = make({ header: '機密等級：一般', footer: '第 1 版' });
        expect(textual.element.querySelector('.b4a-print-layout__header-extra').textContent).toBe('機密等級：一般');
        expect(textual.element.querySelector('.b4a-print-layout__footer').textContent).toBe('第 1 版');
    });

    it('getContentElement 可直接掛子元件；setContent 取代、null 清空', () => {
        const layout = make();
        const content = layout.getContentElement();
        const child = document.createElement('table');
        content.appendChild(child);
        expect(layout.element.contains(child)).toBe(true);
        const replacement = document.createElement('p');
        layout.setContent(replacement);
        expect(content.contains(child)).toBe(false);
        expect(content.firstChild).toBe(replacement);
        layout.setContent(null);
        expect(content.childNodes).toHaveLength(0);
    });

    it('setTitle 更新標題，空字串移除標題', () => {
        const layout = make();
        layout.setTitle('新標題');
        expect(layout.element.querySelector('.b4a-print-layout__title').textContent).toBe('新標題');
        layout.setTitle('');
        expect(layout.element.querySelector('.b4a-print-layout__title')).toBeNull();
    });

    it('沒有標題、頁首且不顯示列印時間時隱藏頁首', () => {
        const layout = make({ title: '', showPrintedAt: false });
        const header = layout.element.querySelector('.b4a-print-layout__header');
        expect(header.hidden).toBe(true);
        expect(header.style.display).toBe('none');
        expect(layout.element.querySelector('.b4a-print-layout__printed-at')).toBeNull();
    });

    it('紙張設定不分大小寫；不支援的值警告並退回預設', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const ok = make({ pageSize: 'letter', orientation: 'LANDSCAPE', margin: 0 });
        expect(ok.element.getAttribute('data-b4a-page')).toBe('letter-landscape-0');
        expect(warn).not.toHaveBeenCalled();
        const bad = make({ pageSize: 'B5', orientation: 'diagonal', margin: '12mm' });
        expect(bad.element.getAttribute('data-b4a-page')).toBe('a4-portrait-15mm');
        expect(warn).toHaveBeenCalledTimes(3);
        expect(PrintLayout.PAGE_SIZES).toEqual(['A4', 'A3', 'Letter', 'Legal']);
        expect(PrintLayout.MARGINS).toEqual(['0', '10mm', '15mm', '20mm', '25mm']);
    });
});

describe('PrintLayout — 預覽', () => {
    it('預設不預覽：不設定紙張寬度與陰影', () => {
        const layout = make();
        const sheet = layout.element.querySelector('.b4a-print-layout__sheet');
        expect(sheet.style.width).toBe('');
        expect(sheet.style.boxShadow).toBe('');
    });

    it('preview：紙張寬度、最小高度、邊界內距與陰影', () => {
        const layout = make({ preview: true });
        const sheet = layout.element.querySelector('.b4a-print-layout__sheet');
        expect(layout.element.dataset.preview).toBe('true');
        expect(sheet.style.width).toBe('210mm');
        expect(sheet.style.minHeight).toBe('297mm');
        expect(sheet.style.padding).toBe('15mm');
        expect(sheet.style.boxShadow).toBe('var(--cl-shadow-md)');
        expect(layout.element.style.background).toBe('var(--cl-bg-secondary)');
        expect(layout.element.style.overflowX).toBe('auto');
    });

    it('preview 依紙張與方向換算尺寸', () => {
        const a3 = make({ preview: true, pageSize: 'A3', orientation: 'landscape', margin: '0' });
        const a3Sheet = a3.element.querySelector('.b4a-print-layout__sheet');
        expect(a3Sheet.style.width).toBe('420mm');
        expect(a3Sheet.style.minHeight).toBe('297mm');
        expect(a3Sheet.style.padding).toMatch(/^0(px)?$/);
        const letter = make({ preview: true, pageSize: 'Letter', margin: '25mm' });
        const letterSheet = letter.element.querySelector('.b4a-print-layout__sheet');
        expect(letterSheet.style.width).toBe('215.9mm');
        expect(letterSheet.style.minHeight).toBe('279.4mm');
        expect(letterSheet.style.padding).toBe('25mm');
    });
});

describe('PrintLayout — 列印按鈕', () => {
    it('printButton：Locale 文字按鈕（Canvas 圖示 aria-hidden），點擊呼叫 print()', () => {
        const layout = make({ printButton: true });
        const button = layout.element.querySelector('.b4a-print-layout__print-button');
        expect(button.type).toBe('button');
        expect(button.textContent).toBe('列印');
        expect(button.querySelector('[aria-hidden="true"] canvas')).not.toBeNull();
        button.click();
        expect(printSpy).toHaveBeenCalledTimes(1);
        window.dispatchEvent(new Event('afterprint'));
    });
});

describe('PrintLayout — print()', () => {
    function buildPage() {
        const header = document.createElement('header');
        const wrapper = document.createElement('div');
        wrapper.className = 'scroll-area';
        const sibling = document.createElement('aside');
        host.append(header, wrapper, sibling);
        return { header, wrapper, sibling };
    }

    it('列印期間標記 html、目標與祖先鏈；afterprint 後全部移除並呼叫 afterPrint', () => {
        const { header, wrapper, sibling } = buildPage();
        const calls = [];
        const beforePrint = vi.fn(() => { calls.push('before'); });
        const afterPrint = vi.fn(() => { calls.push('after'); });
        let during = null;
        printSpy.mockImplementation(() => {
            calls.push('print');
            during = {
                html: document.documentElement.getAttribute('data-b4a-printing'),
                target: layout.element.hasAttribute('data-b4a-print-target'),
                wrapper: wrapper.hasAttribute('data-b4a-print-ancestor'),
                host: host.hasAttribute('data-b4a-print-ancestor'),
                body: document.body.hasAttribute('data-b4a-print-ancestor'),
                header: header.hasAttribute('data-b4a-print-ancestor'),
                sibling: sibling.hasAttribute('data-b4a-print-ancestor')
            };
        });
        const layout = make({ beforePrint, afterPrint }, wrapper);
        expect(layout.print()).toBe(layout);
        expect(during).toEqual({ html: expect.stringMatching(/^print-layout-/), target: true, wrapper: true, host: true, body: true, header: false, sibling: false });
        expect(document.documentElement.hasAttribute('data-b4a-print-ancestor')).toBe(false);
        expect(beforePrint).toHaveBeenCalledWith(layout);
        expect(afterPrint).not.toHaveBeenCalled();

        window.dispatchEvent(new Event('afterprint'));
        expect(printMarks()).toEqual({ printing: false, targets: 0, ancestors: 0 });
        expect(afterPrint).toHaveBeenCalledTimes(1);
        expect(afterPrint).toHaveBeenCalledWith(layout);
        expect(calls).toEqual(['before', 'print', 'after']);
    });

    it('afterprint 監聽只在列印期間掛載', () => {
        const winAdd = vi.spyOn(window, 'addEventListener');
        const winRemove = vi.spyOn(window, 'removeEventListener');
        const layout = make();
        expect(winAdd.mock.calls.filter(([type]) => type === 'afterprint')).toHaveLength(0);
        layout.print();
        const added = winAdd.mock.calls.filter(([type]) => type === 'afterprint');
        expect(added).toHaveLength(1);
        window.dispatchEvent(new Event('afterprint'));
        const removed = winRemove.mock.calls.filter(([type]) => type === 'afterprint');
        expect(removed).toHaveLength(1);
        expect(removed[0][1]).toBe(added[0][1]);
    });

    it('瀏覽器沒送 afterprint 時，備援計時器移除標記', () => {
        vi.useFakeTimers();
        const afterPrint = vi.fn();
        const layout = make({ afterPrint });
        layout.print();
        expect(printMarks().printing).toBe(true);
        vi.advanceTimersByTime(PrintLayout.CLEANUP_FALLBACK_MS - 1);
        expect(printMarks()).toEqual({ printing: true, targets: 1, ancestors: 2 });
        vi.advanceTimersByTime(1);
        expect(printMarks()).toEqual({ printing: false, targets: 0, ancestors: 0 });
        expect(afterPrint).toHaveBeenCalledTimes(1);
        // 之後補送的 afterprint 不會重複呼叫
        window.dispatchEvent(new Event('afterprint'));
        expect(afterPrint).toHaveBeenCalledTimes(1);
    });

    it('window.print() 內同步送出 afterprint 時立即收尾，不留備援計時器', () => {
        vi.useFakeTimers();
        printSpy.mockImplementation(() => window.dispatchEvent(new Event('afterprint')));
        const afterPrint = vi.fn();
        const layout = make({ afterPrint });
        layout.print();
        expect(printMarks().printing).toBe(false);
        expect(afterPrint).toHaveBeenCalledTimes(1);
        expect(vi.getTimerCount()).toBe(0);
    });

    it('print() 時更新列印時間', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2026-09-01T00:00:00.000Z'));
        const layout = make();
        vi.setSystemTime(new Date('2026-09-27T09:15:00.000Z'));
        layout.print();
        const expected = new Intl.DateTimeFormat('zh-TW', TIME_STYLE).format(new Date('2026-09-27T09:15:00.000Z'));
        expect(layout.element.querySelector('.b4a-print-layout__printed-at').textContent).toBe(`列印時間：${expected}`);
        window.dispatchEvent(new Event('afterprint'));
    });

    it('beforePrint 回傳 false 取消列印', () => {
        const layout = make({ beforePrint: () => false });
        layout.print();
        expect(printSpy).not.toHaveBeenCalled();
        expect(printMarks()).toEqual({ printing: false, targets: 0, ancestors: 0 });
    });

    it('beforePrint 內 destroy 版面時不列印、不留標記', () => {
        const layout = make({ beforePrint: (self) => self.destroy() });
        layout.print();
        expect(printSpy).not.toHaveBeenCalled();
        expect(printMarks()).toEqual({ printing: false, targets: 0, ancestors: 0 });
    });

    it('未掛載時不列印並警告', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const layout = make({}, null);
        layout.print();
        expect(printSpy).not.toHaveBeenCalled();
        expect(warn).toHaveBeenCalled();
        expect(printMarks().printing).toBe(false);
    });

    it('另一個實例開始列印時，先收掉前一個的標記', () => {
        const firstAfter = vi.fn();
        const first = make({ afterPrint: firstAfter });
        const second = make({ title: '第二份' });
        first.print();
        second.print();
        expect(firstAfter).toHaveBeenCalledTimes(1);
        expect(first.element.hasAttribute('data-b4a-print-target')).toBe(false);
        expect(second.element.hasAttribute('data-b4a-print-target')).toBe(true);
        expect(printMarks().targets).toBe(1);
        window.dispatchEvent(new Event('afterprint'));
        expect(printMarks()).toEqual({ printing: false, targets: 0, ancestors: 0 });
    });

    it('列印中 destroy：移除標記、監聽與計時器，不呼叫 afterPrint', () => {
        vi.useFakeTimers();
        const winRemove = vi.spyOn(window, 'removeEventListener');
        const afterPrint = vi.fn();
        const layout = make({ afterPrint });
        layout.print();
        layout.destroy();
        expect(printMarks()).toEqual({ printing: false, targets: 0, ancestors: 0 });
        expect(winRemove.mock.calls.filter(([type]) => type === 'afterprint')).toHaveLength(1);
        vi.advanceTimersByTime(PrintLayout.CLEANUP_FALLBACK_MS * 2);
        expect(afterPrint).not.toHaveBeenCalled();
        expect(vi.getTimerCount()).toBe(0);
    });

    it('window.print() 拋錯時收掉標記並往外丟', () => {
        printSpy.mockImplementation(() => { throw new Error('blocked'); });
        const layout = make();
        expect(() => layout.print()).toThrow('blocked');
        expect(printMarks()).toEqual({ printing: false, targets: 0, ancestors: 0 });
    });
});

describe('PrintLayout — 樣式表引用計數', () => {
    it('多個實例共用一個同源 link，最後一個 destroy 才移除', () => {
        expect(styleLinks()).toHaveLength(0);
        const first = make();
        const second = make();
        const links = styleLinks();
        expect(links).toHaveLength(1);
        expect(links[0].rel).toBe('stylesheet');
        expect(links[0].parentNode).toBe(document.head);
        expect(links[0].href).toMatch(/\/layout\/PrintLayout\/PrintLayout\.css$/);
        first.destroy();
        first.destroy();
        expect(styleLinks()).toHaveLength(1);
        second.destroy();
        expect(styleLinks()).toHaveLength(0);
    });

    it('宿主頁面自行放的同 id link 不會被移除', () => {
        const own = document.createElement('link');
        own.id = LINK_ID;
        own.rel = 'stylesheet';
        own.href = '/assets/PrintLayout.css';
        document.head.appendChild(own);
        const layout = make();
        expect(styleLinks()).toEqual([own]);
        layout.destroy();
        expect(styleLinks()).toEqual([own]);
        own.remove();
    });

    it('link 被宿主移除後，print() 會補回', () => {
        const layout = make();
        styleLinks()[0].remove();
        layout.print();
        expect(styleLinks()).toHaveLength(1);
        window.dispatchEvent(new Event('afterprint'));
        layout.destroy();
        expect(styleLinks()).toHaveLength(0);
    });
});

describe('PrintLayout — PrintLayout.css', () => {
    it('每種紙張 × 方向 × 邊界都有具名 @page 與對應選擇器', () => {
        const sizes = { A4: 'A4', A3: 'A3', Letter: 'letter', Legal: 'legal' };
        for (const [size, keyword] of Object.entries(sizes)) {
            for (const orientation of PrintLayout.ORIENTATIONS) {
                for (const margin of PrintLayout.MARGINS) {
                    const key = `${size.toLowerCase()}-${orientation}-${margin}`;
                    expect(cssSource).toContain(`@page b4a-print-${key} { size: ${keyword} ${orientation}; margin: ${margin}; }`);
                    expect(cssSource).toContain(`[data-b4a-print-target][data-b4a-page="${key}"] { page: b4a-print-${key}; }`);
                }
            }
        }
    });

    it('列印規則只在 print() 期間的標記下生效，且不含色碼', () => {
        expect(cssSource).toContain('@media print');
        expect(cssSource).toContain('html[data-b4a-printing] [data-b4a-print-ancestor] > :not([data-b4a-print-ancestor]):not([data-b4a-print-target])');
        expect(cssSource).toContain('.b4a-print-layout__toolbar');
        // 列印時間：非預覽的螢幕畫面隱藏，紙本照常顯示
        expect(cssSource).toMatch(/@media screen\s*\{\s*\.b4a-print-layout\[data-preview="false"\] \.b4a-print-layout__printed-at\s*\{\s*display: none;/);
        expect(cssSource).not.toMatch(/#[0-9a-f]{3,8}\b/i);
        expect(cssSource).not.toMatch(/rgba?\(/i);
        expect(cssSource).not.toMatch(/prefers-color-scheme/);
    });
});

describe('PrintLayout — 生命週期與 Locale', () => {
    it('destroy 移除 DOM、Icon 與 window 監聽，之後呼叫方法不拋錯', () => {
        const winAdd = vi.spyOn(window, 'addEventListener');
        const winRemove = vi.spyOn(window, 'removeEventListener');
        const docAdd = vi.spyOn(document, 'addEventListener');
        const iconDestroy = vi.spyOn(Icon.prototype, 'destroy');
        const layout = new PrintLayout({ printButton: true }).mount(host);
        layout.destroy();
        expect(host.children).toHaveLength(0);
        expect(iconDestroy).toHaveBeenCalled();
        const count = (spy, type) => spy.mock.calls.filter(([t]) => t === type).length;
        expect(count(winAdd, 'locale-changed')).toBe(1);
        expect(count(winRemove, 'locale-changed')).toBe(1);
        expect(docAdd).not.toHaveBeenCalled();
        expect(() => {
            layout.destroy();
            layout.print();
            layout.setContent(document.createElement('p'));
            layout.setTitle('x');
            layout.mount(host);
        }).not.toThrow();
        expect(printSpy).not.toHaveBeenCalled();
        expect(host.children).toHaveLength(0);
    });

    it('切換語言即時更新列印按鈕與列印時間', () => {
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2026-09-27T06:30:00.000Z'));
        const layout = make({ printButton: true });
        Locale.setLang('en');
        expect(layout.element.querySelector('.b4a-print-layout__print-label').textContent).toBe('Print');
        const expected = new Intl.DateTimeFormat('en', TIME_STYLE).format(new Date('2026-09-27T06:30:00.000Z'));
        expect(layout.element.querySelector('.b4a-print-layout__printed-at').textContent).toBe(`Printed: ${expected}`);
    });

    it('mount 找不到目標時警告並回傳 this', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const layout = make({}, null);
        expect(layout.mount('#missing')).toBe(layout);
        expect(warn).toHaveBeenCalled();
    });
});
