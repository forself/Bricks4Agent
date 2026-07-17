import assert from 'node:assert/strict';
import test from 'node:test';
import { installTestDom } from '../TreeList/test-dom.mjs';

const documentRef = installTestDom();
const { List } = await import('./List.js');

test('action list uses native buttons, tracks activeId, and preserves callback payload', () => {
    const selected = [];
    const list = new List({
        activeId: 'first',
        items: [
            { id: 'first', primary: 'First', secondary: 'One' },
            { id: 'second', label: 'Second' },
        ],
        onItemClick: (item, event) => selected.push({ item, type: event.type }),
    }).mount(documentRef.body);

    let buttons = list.element.querySelectorAll('button');
    assert.equal(buttons.length, 2);
    assert.equal(buttons[0].type, 'button');
    assert.equal(buttons[0].getAttribute('aria-pressed'), 'true');
    assert.equal(buttons[1].getAttribute('aria-pressed'), 'false');

    const activatedButton = buttons[1];
    activatedButton.click();
    assert.equal(list.activeId, 'second');
    assert.equal(selected.length, 1);
    assert.equal(selected[0].item.id, 'second');
    assert.equal(selected[0].type, 'click');
    buttons = list.element.querySelectorAll('button');
    assert.equal(buttons[1], activatedButton);
    assert.equal(buttons[1].getAttribute('aria-pressed'), 'true');

    list.setActive('first');
    assert.equal(list.activeId, 'first');
    assert.equal(list.element.querySelectorAll('button')[0].getAttribute('aria-pressed'), 'true');
    list.destroy();
    assert.equal(list.element, null);
});

test('setItems replaces children and removes listeners from discarded actions', () => {
    let clicks = 0;
    const list = new List({
        items: [{ id: 'old', primary: 'Old' }],
        onItemClick: () => { clicks += 1; },
    });
    const discardedButton = list.element.querySelector('button');

    list.setItems([{ id: 'new', primary: 'New' }]);
    discardedButton.click();
    assert.equal(clicks, 0);
    assert.equal(list.element.textContent, 'New');
    list.element.querySelector('button').click();
    assert.equal(clicks, 1);

    list.destroy();
    assert.equal(list._listeners.length, 0);
    assert.equal(list._children.length, 0);
});

test('display-only List keeps the legacy non-button structure', () => {
    const list = new List({ items: [{ primary: 'Primary', secondary: 'Secondary' }] });
    assert.equal(list.element.tagName, 'UL');
    assert.equal(list.element.querySelectorAll('button').length, 0);
    assert.equal(list.element.textContent, 'PrimarySecondary');
    list.destroy();
});
