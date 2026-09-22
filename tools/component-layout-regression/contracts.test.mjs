import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const source = file => readFileSync(new URL('../../packages/javascript/browser/ui_components/' + file, import.meta.url), 'utf8');

test('search input can shrink and icon occupies its own layout space', () => {
    const js = source('form/Dropdown/Dropdown.js');
    const css = source('form/Dropdown/Dropdown.css');
    assert.match(js, /input\.style\.cssText = `[\s\S]*?width: 0;\s*min-width: 0;/);
    assert.match(js, /icons\.style\.cssText = `\s*position: static;\s*transform: none;\s*flex: 0 0 auto;/);
    assert.match(css, /\.dropdown__icons\s*\{\s*position: static;\s*flex: 0 0 auto;/);
    assert.match(js, /min-height: \$\{sizeStyles\.height\}/);
});
test('date text and calendar icon cannot overlap', () => {
    const js = source('form/DatePicker/DatePicker.js');
    assert.match(js, /max-width: 100%;\s*min-width: 0;/);
    assert.match(js, /display\.style\.cssText = `\s*flex: 1;\s*min-width: 0;\s*overflow-wrap: anywhere;/);
    assert.match(js, /icon\.style\.cssText = `\s*flex: 0 0 auto;/);
    assert.match(js, /min-height: \$\{sizeStyles\.height\}/);
});
test('grid and field wrappers allow long labels to wrap inside assigned columns', () => {
    assert.match(source('layout/FormRow/FormRow.js'), /repeat\(12, minmax\(0, 1fr\)\)/);
    const field = source('form/FormField/FormField.js');
    assert.match(field, /container\.style\.minWidth = '0'/);
    assert.match(field, /slot\.style\.minWidth = '0'/);
    assert.match(field, /white-space:normal;overflow-wrap:anywhere/);
});
