class FakeStyle {
    constructor() {
        this.cssText = '';
    }
}

class FakeClassList {
    constructor(element) {
        this.element = element;
    }

    _read() {
        return new Set(String(this.element.className || '').split(/\s+/).filter(Boolean));
    }

    _write(set) {
        this.element.className = Array.from(set).join(' ');
    }

    add(...tokens) {
        const set = this._read();
        for (const token of tokens) set.add(token);
        this._write(set);
    }

    remove(...tokens) {
        const set = this._read();
        for (const token of tokens) set.delete(token);
        this._write(set);
    }

    contains(token) {
        return this._read().has(token);
    }
}

class FakeElement {
    constructor(tagName, ownerDocument) {
        this.tagName = tagName.toUpperCase();
        this.ownerDocument = ownerDocument;
        this.children = [];
        this.parentNode = null;
        this.style = new FakeStyle();
        this.dataset = {};
        this.attributes = new Map();
        this.eventListeners = new Map();
        this.className = '';
        this.textContent = '';
        this._innerHTML = '';
        this.id = '';
        this.type = '';
        this.value = '';
        this.placeholder = '';
        this.disabled = false;
        this.readOnly = false;
        this.maxLength = undefined;
        this.classList = new FakeClassList(this);
    }

    get innerHTML() {
        return this._innerHTML;
    }

    set innerHTML(value) {
        this._innerHTML = String(value);
        if (value === '') {
            this.children = [];
        }
    }

    appendChild(child) {
        if (child.parentNode) {
            child.parentNode.removeChild(child);
        }
        child.parentNode = this;
        this.children.push(child);
        return child;
    }

    removeChild(child) {
        this.children = this.children.filter((entry) => entry !== child);
        child.parentNode = null;
        return child;
    }

    remove() {
        if (this.parentNode) {
            this.parentNode.removeChild(this);
        }
    }

    setAttribute(name, value) {
        this.attributes.set(name, String(value));
        if (name === 'id') this.id = String(value);
    }

    getAttribute(name) {
        return this.attributes.get(name);
    }

    addEventListener(type, handler) {
        if (!this.eventListeners.has(type)) {
            this.eventListeners.set(type, []);
        }
        this.eventListeners.get(type).push(handler);
    }

    removeEventListener(type, handler) {
        const handlers = this.eventListeners.get(type) || [];
        this.eventListeners.set(type, handlers.filter((entry) => entry !== handler));
    }

    dispatchEvent(event) {
        const handlers = this.eventListeners.get(event.type) || [];
        for (const handler of handlers) {
            handler({ target: this, currentTarget: this, ...event });
        }
    }

    querySelector(selector) {
        if (selector.startsWith('#')) {
            return this._find((node) => node.id === selector.slice(1));
        }
        if (selector.startsWith('.')) {
            return this._find((node) => node.classList.contains(selector.slice(1)));
        }
        return null;
    }

    querySelectorAll(selector) {
        const matches = [];
        if (selector.startsWith('.')) {
            this._collect((node) => node.classList.contains(selector.slice(1)), matches);
        }
        return matches;
    }

    _find(predicate) {
        for (const child of this.children) {
            if (predicate(child)) return child;
            const nested = child._find(predicate);
            if (nested) return nested;
        }
        return null;
    }

    _collect(predicate, matches) {
        for (const child of this.children) {
            if (predicate(child)) matches.push(child);
            child._collect(predicate, matches);
        }
    }

    contains(node) {
        if (!node) return false;
        if (node === this) return true;
        return this.children.some((child) => child.contains(node));
    }

    focus() {
        this.dispatchEvent({ type: 'focus' });
    }

    scrollIntoView() {}
}

class FakeDocument {
    constructor() {
        this.head = new FakeElement('head', this);
        this.body = new FakeElement('body', this);
        this.eventListeners = new Map();
    }

    createElement(tagName) {
        return new FakeElement(tagName, this);
    }

    getElementById(id) {
        return this.head.querySelector(`#${id}`) || this.body.querySelector(`#${id}`);
    }

    querySelector(selector) {
        return this.body.querySelector(selector) || this.head.querySelector(selector);
    }

    querySelectorAll(selector) {
        return [...this.body.querySelectorAll(selector), ...this.head.querySelectorAll(selector)];
    }

    addEventListener(type, handler) {
        if (!this.eventListeners.has(type)) {
            this.eventListeners.set(type, []);
        }
        this.eventListeners.get(type).push(handler);
    }

    removeEventListener(type, handler) {
        const handlers = this.eventListeners.get(type) || [];
        this.eventListeners.set(type, handlers.filter((entry) => entry !== handler));
    }

    dispatchEvent(event) {
        const handlers = this.eventListeners.get(event.type) || [];
        for (const handler of handlers) {
            handler(event);
        }
    }
}

export function installFakeDom() {
    const previous = {
        document: globalThis.document,
        window: globalThis.window,
        HTMLElement: globalThis.HTMLElement,
        getComputedStyle: globalThis.getComputedStyle
    };

    const document = new FakeDocument();
    const window = { document };

    globalThis.document = document;
    globalThis.window = window;
    globalThis.HTMLElement = FakeElement;
    globalThis.getComputedStyle = (element) => element.style;

    return {
        document,
        window,
        cleanup() {
            globalThis.document = previous.document;
            globalThis.window = previous.window;
            globalThis.HTMLElement = previous.HTMLElement;
            globalThis.getComputedStyle = previous.getComputedStyle;
        }
    };
}

export default installFakeDom;
