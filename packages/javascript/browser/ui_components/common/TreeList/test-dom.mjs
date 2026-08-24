class FakeClassList {
    constructor(element) {
        this.element = element;
    }

    contains(name) {
        return this.element.className.split(/\s+/).includes(name);
    }
}

export class FakeElement {
    constructor(tagName, ownerDocument) {
        this.tagName = String(tagName).toUpperCase();
        this.ownerDocument = ownerDocument;
        this.parentNode = null;
        this.children = [];
        this.childNodes = this.children;
        this.style = { cssText: '' };
        this.dataset = {};
        this.attributes = new Map();
        this.className = '';
        this.classList = new FakeClassList(this);
        this._textContent = '';
        this._listeners = new Map();
    }

    appendChild(child) {
        child.parentNode?.removeChild?.(child);
        this.children.push(child);
        child.parentNode = this;
        return child;
    }

    append(...children) {
        children.forEach((child) => this.appendChild(child));
    }

    removeChild(child) {
        const index = this.children.indexOf(child);
        if (index >= 0) this.children.splice(index, 1);
        child.parentNode = null;
        return child;
    }

    replaceChildren(...children) {
        [...this.children].forEach((child) => this.removeChild(child));
        this._textContent = '';
        children.forEach((child) => this.appendChild(child));
    }

    remove() {
        this.parentNode?.removeChild?.(this);
    }

    contains(candidate) {
        for (let current = candidate; current; current = current.parentNode) {
            if (current === this) return true;
        }
        return false;
    }

    setAttribute(name, value) {
        this.attributes.set(name, String(value));
    }

    getAttribute(name) {
        return this.attributes.get(name) ?? null;
    }

    addEventListener(type, handler) {
        const handlers = this._listeners.get(type) ?? new Set();
        handlers.add(handler);
        this._listeners.set(type, handlers);
    }

    removeEventListener(type, handler) {
        this._listeners.get(type)?.delete(handler);
    }

    dispatchEvent(event) {
        event.target ??= this;
        event.currentTarget = this;
        event.preventDefault ??= () => { event.defaultPrevented = true; };
        event.stopPropagation ??= () => { event.propagationStopped = true; };
        for (const handler of this._listeners.get(event.type) ?? []) handler.call(this, event);
        this[`on${event.type}`]?.call(this, event);
        return !event.defaultPrevented;
    }

    click() {
        this.dispatchEvent({ type: 'click' });
    }

    querySelectorAll(selector) {
        const matches = [];
        const normalized = selector.toUpperCase();
        const visit = (element) => {
            for (const child of element.children) {
                if (selector.startsWith('.')) {
                    if (child.classList.contains(selector.slice(1))) matches.push(child);
                } else if (child.tagName === normalized) {
                    matches.push(child);
                }
                visit(child);
            }
        };
        visit(this);
        return matches;
    }

    querySelector(selector) {
        return this.querySelectorAll(selector)[0] ?? null;
    }

    set innerHTML(value) {
        if (value !== '') throw new Error('Fake DOM only supports clearing innerHTML.');
        this.replaceChildren();
    }

    get innerHTML() {
        return '';
    }

    set textContent(value) {
        this.replaceChildren();
        this._textContent = String(value);
    }

    get textContent() {
        return this._textContent + this.children.map((child) => child.textContent).join('');
    }

    get isConnected() {
        return Boolean(this.ownerDocument?.documentElement?.contains(this));
    }
}

export function installTestDom() {
    const documentRef = {
        createElement(tagName) {
            return new FakeElement(tagName, documentRef);
        },
        querySelector(selector) {
            return documentRef.documentElement.querySelector(selector);
        },
    };
    documentRef.documentElement = documentRef.createElement('html');
    documentRef.body = documentRef.createElement('body');
    documentRef.documentElement.appendChild(documentRef.body);
    globalThis.document = documentRef;
    globalThis.HTMLElement = FakeElement;
    return documentRef;
}
