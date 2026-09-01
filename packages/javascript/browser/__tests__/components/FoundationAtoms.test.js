import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { Text } from '../../ui_components/common/Text/Text.js';
import { Heading } from '../../ui_components/common/Heading/Heading.js';
import { Icon } from '../../ui_components/common/Icon/Icon.js';
import { Link } from '../../ui_components/common/Link/Link.js';

describe('Text', () => {
    let container;
    beforeEach(() => { container = document.createElement('div'); document.body.appendChild(container); });
    afterEach(() => { container.remove(); });

    it('mount 後元素存在且為文字內容', () => {
        new Text({ text: 'hello' }).mount(container);
        const el = container.querySelector('.cl-text');
        expect(el).not.toBeNull();
        expect(el.textContent).toBe('hello');
    });

    it('setText 更新內容', () => {
        const t = new Text({ text: 'a' }).mount(container);
        t.setText('b');
        expect(t.getText()).toBe('b');
        expect(container.querySelector('.cl-text').textContent).toBe('b');
    });

    it('tag 限白名單,非法退回 span', () => {
        new Text({ text: 'x', tag: 'script' }).mount(container);
        expect(container.querySelector('span.cl-text')).not.toBeNull();
    });
});

describe('Heading', () => {
    let container;
    beforeEach(() => { container = document.createElement('div'); document.body.appendChild(container); });
    afterEach(() => { container.remove(); });

    it('依 level 產生 h1–h6', () => {
        new Heading({ text: 'T', level: 3 }).mount(container);
        expect(container.querySelector('h3.cl-heading')).not.toBeNull();
    });

    it('level 超界被夾到 1–6', () => {
        new Heading({ text: 'T', level: 9 }).mount(container);
        expect(container.querySelector('h6')).not.toBeNull();
    });
});

describe('Icon', () => {
    let container;
    beforeEach(() => { container = document.createElement('div'); document.body.appendChild(container); });
    afterEach(() => { container.remove(); });

    it('已知 name 渲染 CSP-safe canvas icon', () => {
        new Icon({ name: 'search' }).mount(container);
        const canvas = container.querySelector('.cl-icon canvas.cl-icon__canvas');
        expect(canvas).not.toBeNull();
        expect(canvas.width).toBeGreaterThan(0);
    });

    it('未知 name = fail-closed:渲染空,不現捏', () => {
        new Icon({ name: 'totally-made-up' }).mount(container);
        const el = container.querySelector('.cl-icon');
        expect(el).not.toBeNull();
        expect(el.querySelector('svg')).toBeNull();
        expect(el.querySelector('canvas.cl-icon__canvas')).not.toBeNull();
    });

    it('names() 是閉集且 has() 一致', () => {
        expect(Icon.names()).toContain('close');
        expect(Icon.has('close')).toBe(true);
        expect(Icon.has('nope')).toBe(false);
    });
});

describe('Link', () => {
    let container;
    beforeEach(() => { container = document.createElement('div'); document.body.appendChild(container); });
    afterEach(() => { container.remove(); });

    it('internal 安全 href 照常設定', () => {
        const l = new Link({ text: 'go', href: '/about', scope: 'internal' }).mount(container);
        expect(l.getHref()).toBe('/about');
    });

    it('external 自動加 noopener + target=_blank', () => {
        new Link({ text: 'x', href: 'https://example.com', scope: 'external' }).mount(container);
        const a = container.querySelector('a.cl-link');
        expect(a.getAttribute('target')).toBe('_blank');
        expect(a.getAttribute('rel')).toBe('noopener noreferrer');
    });

    it('scope=none 無 href(純文字)', () => {
        const l = new Link({ text: 'plain', href: 'https://example.com', scope: 'none' }).mount(container);
        expect(l.getHref()).toBeNull();
    });

    it('不安全協定 = fail-closed:去掉 href', () => {
        // eslint-disable-next-line no-script-url
        const l = new Link({ text: 'evil', href: 'javascript:alert(1)', scope: 'internal' }).mount(container);
        expect(l.getHref()).toBeNull();
    });

    it('disabled 去 href 並擋 onClick', () => {
        let clicked = false;
        const l = new Link({ text: 'd', href: '/x', disabled: true, onClick: () => { clicked = true; } }).mount(container);
        expect(l.getHref()).toBeNull();
        container.querySelector('a').click();
        expect(clicked).toBe(false);
    });
});
