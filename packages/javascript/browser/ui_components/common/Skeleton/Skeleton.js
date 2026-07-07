import { createComponentState } from '../../utils/component-state.js';

/**
 * Skeleton — 載入骨架原子。葉子;shimmer 走 Web Animations API(CSP 合規,無 random/Date),確定性。
 */
export class Skeleton {
    static VARIANTS = { TEXT: 'text', RECT: 'rect', CIRCLE: 'circle' };

    constructor(options = {}) {
        this.options = {
            variant: Skeleton.VARIANTS.TEXT,
            width: '',
            height: '',
            lines: 3,
            ...options
        };
        this.element = null;
        this._animations = [];
        this._state = createComponentState(
            { lifecycle: 'created', visibility: 'visible' },
            {
                MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
                SHOW: (s) => ({ ...s, visibility: 'visible' }),
                HIDE: (s) => ({ ...s, visibility: 'hidden' }),
                DESTROY: (s) => ({ ...s, lifecycle: 'destroyed' })
            }
        );
        this._create();
        this._applyState();
    }

    // CSP 合規:樣式全走元素層 CSSOM(style.cssText),shimmer 用 Web Animations API,不注入 <style>。
    _composeItemCssText(isLast) {
        const decl = [
            'background: linear-gradient(90deg, var(--cl-bg-secondary) 25%, var(--cl-border) 37%, var(--cl-bg-secondary) 63%)',
            'background-size: 400% 100%',
            'border-radius: var(--cl-radius-sm)'
        ];
        if (this.options.variant === Skeleton.VARIANTS.TEXT) {
            decl.push('height: 12px');
            if (isLast) decl.push('width: 60%', 'margin-bottom: 0');
            else decl.push('margin-bottom: 8px');
        } else if (this.options.variant === Skeleton.VARIANTS.RECT) {
            decl.push('width: 100%', 'height: 120px');
        } else if (this.options.variant === Skeleton.VARIANTS.CIRCLE) {
            decl.push('width: 40px', 'height: 40px', 'border-radius: var(--cl-radius-round)');
        }
        return decl.join('; ') + ';';
    }

    _create() {
        this.element = document.createElement('div');
        this.element.className = `cl-skeleton cl-skeleton--${this.options.variant}`;
        this.element.style.cssText = 'display: block;';
        const count = this.options.variant === Skeleton.VARIANTS.TEXT
            ? Math.max(1, parseInt(this.options.lines, 10) || 1)
            : 1;
        for (let i = 0; i < count; i++) {
            const item = document.createElement('div');
            item.className = 'cl-skeleton-item';
            item.style.cssText = this._composeItemCssText(i === count - 1);
            if (this.options.width) item.style.width = this.options.width;
            if (this.options.height) item.style.height = this.options.height;
            if (typeof item.animate === 'function') {
                this._animations.push(item.animate(
                    [{ backgroundPosition: '100% 50%' }, { backgroundPosition: '0% 50%' }],
                    { duration: 1400, easing: 'ease', iterations: Infinity }
                ));
            }
            this.element.appendChild(item);
        }
    }

    _applyState() {
        if (!this.element) return;
        this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : 'block';
    }

    snapshot() { return this._state.snapshot(); }
    send(event, payload = null) { const next = this._state.send(event, payload); this._applyState(); return next; }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) { console.warn('[Skeleton] mount target not found:', container); return this; }
        target.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() {
        this._animations.forEach((a) => a.cancel());
        this._animations = [];
        this.send('DESTROY');
        this.element?.remove();
        this.element = null;
    }
}

export default Skeleton;
