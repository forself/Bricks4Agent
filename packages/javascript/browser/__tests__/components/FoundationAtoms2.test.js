import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { Textarea } from '../../ui_components/form/TextArea/index.js';   // 合併後單一 TextArea 雙名稱
import { Slider } from '../../ui_components/form/Slider/Slider.js';
import { Rating } from '../../ui_components/form/Rating/Rating.js';
import { Skeleton } from '../../ui_components/common/Skeleton/Skeleton.js';
import { MediaPlayer } from '../../ui_components/common/MediaPlayer/MediaPlayer.js';
import { CodeBlock } from '../../ui_components/common/CodeBlock/CodeBlock.js';

let container;
beforeEach(() => { container = document.createElement('div'); document.body.appendChild(container); });
afterEach(() => { container.remove(); });

describe('Textarea', () => {
    it('mount 後 textarea 存在,get/set value', () => {
        const t = new Textarea({ value: 'a' }).mount(container);
        expect(container.querySelector('.cl-textarea textarea.cl-textarea__field')).not.toBeNull();
        t.setValue('b');
        expect(t.getValue()).toBe('b');
    });
    it('setDisabled 反映到 DOM 與 state', () => {
        const t = new Textarea().mount(container);
        t.setDisabled(true);
        expect(t.textarea.disabled).toBe(true);
        expect(t.snapshot().availability).toBe('disabled');
    });
});

describe('Slider', () => {
    it('值被夾在 min/max', () => {
        const s = new Slider({ min: 0, max: 10, value: 99 }).mount(container);
        expect(s.getValue()).toBe(10);
        s.setValue(-5);
        expect(s.getValue()).toBe(0);
    });
});

describe('Rating', () => {
    it('依 max 產生星數,setValue 改填色數', () => {
        const r = new Rating({ max: 5, value: 2 }).mount(container);
        expect(container.querySelector('.cl-rating canvas')).not.toBeNull();
        expect(r._stars).toHaveLength(5);
        r.setValue(4);
        expect(r.getValue()).toBe(4);
    });
    it('值被夾在 0..max', () => {
        const r = new Rating({ max: 3, value: 10 }).mount(container);
        expect(r.getValue()).toBe(3);
    });
});

describe('Skeleton', () => {
    it('text variant 依 lines 產生骨架條', () => {
        new Skeleton({ variant: 'text', lines: 4 }).mount(container);
        expect(container.querySelectorAll('.cl-skeleton-item').length).toBe(4);
    });
});

describe('MediaPlayer', () => {
    it('安全 https src 照常設定', () => {
        const m = new MediaPlayer({ src: 'https://example.com/v.mp4' }).mount(container);
        expect(m.getSrc()).toBe('https://example.com/v.mp4');
    });
    it('不安全協定 = fail-closed:不設 src', () => {
        // eslint-disable-next-line no-script-url
        const m = new MediaPlayer({ src: 'javascript:alert(1)' }).mount(container);
        expect(m.getSrc()).toBeNull();
    });
});

describe('CodeBlock', () => {
    it('code 以 textContent 呈現(不解析 HTML)', () => {
        const c = new CodeBlock({ code: '<b>x</b>', language: 'js' }).mount(container);
        const code = container.querySelector('.cl-code code');
        expect(code.textContent).toBe('<b>x</b>');
        expect(code.querySelector('b')).toBeNull();
    });
});
