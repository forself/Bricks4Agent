import { createComponentState } from '../../utils/component-state.js';

const SAFE_PROTOCOLS = new Set(['http:', 'https:']);

/**
 * MediaPlayer — 影音原子(video / audio)。葉子;src 經協定白名單,不安全 = fail-closed(不設 src)。
 */
export class MediaPlayer {
    static KINDS = { VIDEO: 'video', AUDIO: 'audio' };

    constructor(options = {}) {
        this.options = {
            src: '',
            kind: MediaPlayer.KINDS.VIDEO,
            poster: '',
            controls: true,
            ...options
        };
        this.element = null;
        this._state = createComponentState(
            { lifecycle: 'created', visibility: 'visible' },
            {
                MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
                SHOW: (s) => ({ ...s, visibility: 'visible' }),
                HIDE: (s) => ({ ...s, visibility: 'hidden' }),
                DESTROY: (s) => ({ ...s, lifecycle: 'destroyed' })
            }
        );
        this._injectStyles();
        this._create();
        this._applyState();
    }

    static _sanitizeSrc(src) {
        const value = String(src ?? '').trim();
        if (!value) return '';
        if (value.startsWith('/')) return value;
        try {
            const url = new URL(value, 'https://local.invalid');
            return SAFE_PROTOCOLS.has(url.protocol) ? value : '';
        } catch { return ''; }
    }

    _injectStyles() {
        if (document.getElementById('mediaplayer-component-styles')) return;
        const style = document.createElement('style');
        style.id = 'mediaplayer-component-styles';
        style.textContent = `
            .cl-media { max-width: 100%; }
            .cl-media--video { width: 100%; border-radius: var(--cl-radius-md); background: var(--cl-surface-contrast); }
            .cl-media--audio { width: 100%; }
        `;
        document.head.appendChild(style);
    }

    _create() {
        const kind = this.options.kind === MediaPlayer.KINDS.AUDIO ? 'audio' : 'video';
        this.element = document.createElement(kind);
        this.element.className = `cl-media cl-media--${kind}`;
        if (this.options.controls) this.element.controls = true;
        if (kind === 'video' && this.options.poster) {
            const poster = MediaPlayer._sanitizeSrc(this.options.poster);
            if (poster) this.element.setAttribute('poster', poster);
        }
        const src = MediaPlayer._sanitizeSrc(this.options.src);
        if (src) this.element.setAttribute('src', src);
        else if (this.options.src) console.warn('[MediaPlayer] unsafe src dropped (fail-closed):', this.options.src);
    }

    _applyState() {
        if (!this.element) return;
        this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : '';
    }

    snapshot() { return this._state.snapshot(); }
    send(event, payload = null) { const next = this._state.send(event, payload); this._applyState(); return next; }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) { console.warn('[MediaPlayer] mount target not found:', container); return this; }
        target.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    getSrc() { return this.element?.getAttribute('src') ?? null; }
    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this.send('DESTROY'); this.element?.remove(); this.element = null; }
}

export default MediaPlayer;
