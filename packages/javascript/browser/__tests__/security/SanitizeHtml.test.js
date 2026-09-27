import { describe, it, expect } from 'vitest';
import { sanitizeHTML } from '../../ui_components/utils/security.js';
import { sanitizeHTML as sanitizeTemplateHTML } from '../../../../../templates/spa/frontend/components/utils/security.js';

const parse = (html) => {
    const template = document.createElement('template');
    template.innerHTML = html;
    return template.content;
};

const hasActiveContent = (html) => {
    const root = parse(html);
    for (const el of root.querySelectorAll('*')) {
        for (const attr of el.attributes) {
            if (/^on/i.test(attr.name)) return `event handler ${attr.name}`;
            if (/^(href|src)$/i.test(attr.name) && /^\s*(javascript|vbscript|data:text)/i.test(attr.value)) return `${attr.name}=${attr.value}`;
            if (attr.name === 'srcdoc' || attr.name === 'style') return attr.name;
        }
        if (['script', 'iframe', 'object', 'embed', 'svg', 'math', 'style', 'form', 'input', 'button'].includes(el.localName)) {
            return `<${el.localName}>`;
        }
    }
    return null;
};

describe('sanitizeHTML', () => {
    const attacks = [
        '<section><img src=y onerror="fetch(1)"></section>',
        '<main><a href="javascript:alert(1)">x</a></main>',
        '<details open ontoggle="alert(1)"><img src=w onerror=alert(5)></details>',
        '<header><embed src=x></header>',
        '<p>ok</p><section><img src=x onerror=alert(1)></section><p>ok2</p>',
        '<main><section><span onmouseover=1>deep</span></section><aside><img src=x onerror=2></aside></main>',
        '<section><script>bad()</script>keep</section>',
        '<article><iframe srcdoc="<script>alert(1)</script>"></iframe></article>',
        '<figure><svg><script>alert(1)</script></svg></figure>',
        '<label><math><mtext><img src=x onerror=alert(1)></mtext></math></label>',
        '<x-card><noscript><img src=x onerror=alert(1)></noscript>text</x-card>',
        '<video><source onerror=alert(1)></video><audio><track src="javascript:alert(1)"></audio>',
        '<section><section><section><a href="JaVaScRiPt:alert(1)">deep</a></section></section></section>',
    ];

    it.each(attacks)('removes active content hidden inside non-allowlisted wrappers: %s', (input) => {
        const output = sanitizeHTML(input);
        expect(hasActiveContent(output)).toBeNull();
    });

    it('keeps the text of unwrapped wrappers', () => {
        expect(sanitizeHTML('<section><p>hello</p></section>')).toBe('<p>hello</p>');
        expect(sanitizeHTML('<section><script>bad()</script>keep</section>')).toBe('keep');
        expect(sanitizeHTML('<main><section><span onmouseover=1>deep</span></section></main>')).toBe('<span>deep</span>');
    });

    it('leaves allowlisted markup unchanged', () => {
        const benign = [
            '<p><b>bold</b> <i>it</i> <a href="https://example.com/a?b=1">link</a></p>',
            '<table><thead><tr><th colspan="2">h</th></tr></thead><tbody><tr><td>1</td><td>2</td></tr></tbody></table>',
            '<ul><li class="rt-color-blue-500">a</li><li>b</li></ul>',
            '<h2 id="section_1">Title</h2><blockquote>q</blockquote><pre><code>x = 1</code></pre>',
            '<img src="data:image/png;base64,iVBORw0KGgo=" alt="dot">',
        ];
        for (const html of benign) {
            const template = document.createElement('template');
            template.innerHTML = html;
            expect(sanitizeHTML(html)).toBe(template.innerHTML);
        }
    });

    it('is idempotent', () => {
        for (const input of attacks) {
            const once = sanitizeHTML(input);
            expect(sanitizeHTML(once)).toBe(once);
        }
    });

    it('still strips unsafe ids, protocol-relative links and svg data images', () => {
        expect(sanitizeHTML('<p id="a&quot;onmouseover=1">x</p>')).toBe('<p>x</p>');
        expect(sanitizeHTML('<a href="//evil.example">x</a>')).toBe('<a>x</a>');
        expect(sanitizeHTML('<a href="/\\evil.example">x</a>')).toBe('<a>x</a>');
        expect(sanitizeHTML('<img src="data:image/svg+xml;base64,PHN2Zz4=">')).toBe('<img>');
    });
});

// SPA 範本前端有自己的 sanitizeHTML（允許清單較寬），拆殼繞過必須同樣被擋下
describe('SPA template sanitizeHTML', () => {
    const attacks = [
        '<section><img src=y onerror="fetch(1)"></section>',
        '<main><a href="javascript:alert(1)">x</a></main>',
        '<details open ontoggle="alert(1)"><img src=w onerror=alert(5)></details>',
        '<p>ok</p><section><img src=x onerror=alert(1)></section><p>ok2</p>',
        '<main><section><span onmouseover=1>deep</span></section><aside><img src=x onerror=2></aside></main>',
        '<figure><svg><script>alert(1)</script></svg></figure>',
        '<x-card><noscript><img src=x onerror=alert(1)></noscript>text</x-card>',
        '<section><section><section><a href="JaVaScRiPt:alert(1)">deep</a></section></section></section>',
    ];

    it.each(attacks)('removes active content hidden inside unwrapped elements: %s', (input) => {
        const output = sanitizeTemplateHTML(input);
        expect(hasActiveContent(output)).toBeNull();
        expect(sanitizeTemplateHTML(output)).toBe(output);
    });

    it('keeps the text of unwrapped wrappers', () => {
        expect(sanitizeTemplateHTML('<section><p>hello</p></section>')).toBe('<p>hello</p>');
        expect(sanitizeTemplateHTML('<main><section><span onmouseover=1>deep</span></section></main>')).toBe('<span>deep</span>');
    });
});
