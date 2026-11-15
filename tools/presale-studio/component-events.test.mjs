import test from 'node:test';
import assert from 'node:assert/strict';
import { bindComponentEvent, componentEvents } from './component-events.mjs';

test('component event adapter uses declared events and preserves B4A change callbacks', () => {
    assert.deepEqual(componentEvents('Dropdown'), ['change']);
    assert.deepEqual(componentEvents('ToggleSwitch'), ['change']);
    assert.deepEqual(componentEvents('TextInput'), ['change', 'blur']);
    let previous = 0, invoked = 0;
    const dropdown = { options: { onChange: () => { previous++; } } };
    bindComponentEvent(dropdown, 'Dropdown', 'change', () => { invoked++; });
    dropdown.options.onChange('active');
    assert.equal(previous, 1);
    assert.equal(invoked, 1);
    assert.throws(() => bindComponentEvent(dropdown, 'Dropdown', 'blur', () => {}), /Unsupported/);
    const button = { element: new EventTarget() };
    bindComponentEvent(button, 'BasicButton', 'click', () => { invoked++; });
    button.element.dispatchEvent(new Event('click'));
    assert.equal(invoked, 2);
});
