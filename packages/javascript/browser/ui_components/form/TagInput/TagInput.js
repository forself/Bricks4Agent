import { createComponentState } from '../../utils/component-state.js';
import { TextInput } from '../TextInput/TextInput.js';
import { Tag } from '../../common/Tag/Tag.js';
import { Icon } from '../../common/Icon/Icon.js';

/**
 * TagInput — 多標籤輸入(複合)。TextInput + 每標籤一個 Tag(可移除)。確定性展開。
 */
export class TagInput {
    constructor(options = {}) {
        this.options = { tags: [], placeholder: '輸入後按 Enter', onChange: null, ...options };
        this.element = null;
        this.input = null;
        this._tagsWrap = null;
        this._tagChildren = [];
        this._state = createComponentState(
            { lifecycle: 'created', visibility: 'visible', tags: [...(this.options.tags || [])].map(String) },
            {
                MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
                ADD_TAG: (s, p) => s.tags.includes(p.tag) ? s : ({ ...s, tags: [...s.tags, p.tag] }),
                REMOVE_TAG: (s, p) => ({ ...s, tags: s.tags.filter((t) => t !== p.tag) }),
                DESTROY: (s) => ({ ...s, lifecycle: 'destroyed' })
            }
        );
        this._injectStyles();
        this._create();
        this._renderTags();
    }

    _injectStyles() {
        if (document.getElementById('taginput-component-styles')) return;
        const s = document.createElement('style');
        s.id = 'taginput-component-styles';
        s.textContent = `
            .cl-taginput { border: 1px solid var(--cl-border); border-radius: var(--cl-radius-md); padding: 6px; display: flex; flex-wrap: wrap; gap: 6px; align-items: center; }
            .cl-taginput .text-input, .cl-taginput input { border: none; outline: none; flex: 1; min-width: 80px; background: transparent; }
            .cl-taginput-tag { display: inline-flex; align-items: center; gap: 4px; }
            .cl-taginput-remove { cursor: pointer; display: inline-flex; }
        `;
        document.head.appendChild(s);
    }

    _create() {
        this.element = document.createElement('div');
        this.element.className = 'cl-taginput';
        this._tagsWrap = document.createElement('span');
        this._tagsWrap.style.display = 'contents';
        this.element.appendChild(this._tagsWrap);
        this.input = new TextInput({ placeholder: this.options.placeholder });
        this.input.mount(this.element);
        this.input.input?.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                const v = this.input.getValue().trim();
                if (v) { this.addTag(v); this.input.clear(); }
            }
        });
    }

    _renderTags() {
        this._tagChildren.forEach((t) => t.destroy?.());
        this._tagChildren = [];
        this._tagsWrap.replaceChildren();
        this.snapshot().tags.forEach((label) => {
            const holder = document.createElement('span');
            holder.className = 'cl-taginput-tag';
            const tag = new Tag({ text: label });
            (tag.mount ? tag.mount(holder) : tag.render?.(holder));
            const remove = document.createElement('span');
            remove.className = 'cl-taginput-remove';
            const x = new Icon({ name: 'close', size: 'small', label: 'remove' });
            x.mount(remove);
            remove.addEventListener('click', () => this.removeTag(label));
            holder.appendChild(remove);
            this._tagsWrap.appendChild(holder);
            this._tagChildren.push(tag, x);
        });
    }

    snapshot() { return this._state.snapshot(); }
    send(e, p = null) { const n = this._state.send(e, p); this._renderTags(); if (typeof this.options.onChange === 'function') this.options.onChange(this.getTags()); return n; }

    mount(container) {
        const t = typeof container === 'string' ? document.querySelector(container) : container;
        if (!t) { console.warn('[TagInput] mount target not found:', container); return this; }
        t.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    getTags() { return [...this.snapshot().tags]; }
    addTag(tag) { this.send('ADD_TAG', { tag: String(tag) }); return this; }
    removeTag(tag) { this.send('REMOVE_TAG', { tag: String(tag) }); return this; }
    destroy() {
        this._tagChildren.forEach((t) => t.destroy?.());
        this.input?.destroy?.();
        this._tagChildren = [];
        this.send('DESTROY'); this.element?.remove(); this.element = null; this.input = null;
    }
}

export default TagInput;
