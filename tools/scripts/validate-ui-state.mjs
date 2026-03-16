#!/usr/bin/env node

import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

import { installFakeDom } from './lib/fake-dom.mjs';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..', '..');

let pass = 0;
let fail = 0;

function assert(condition, message) {
    if (!condition) throw new Error(message);
}

async function test(name, fn) {
    try {
        await fn();
        pass += 1;
        console.log(`  [OK] ${name}`);
    } catch (error) {
        fail += 1;
        console.log(`  [NG] ${name}: ${error.message || error}`);
    }
}

async function importModule(relativePath) {
    const fullPath = path.join(repoRoot, relativePath);
    return import(pathToFileURL(fullPath).href);
}

console.log('=== UI State Contract Validation ===\n');

await test('component-state core: snapshot and transitions', async () => {
    const { createComponentState } = await importModule('packages/javascript/browser/ui_components/utils/component-state.js');
    const machine = createComponentState(
        { lifecycle: 'created', count: 0 },
        {
            INC: (state) => ({ ...state, count: state.count + 1 }),
            MOUNT: (state) => ({ ...state, lifecycle: 'mounted' })
        }
    );

    const seen = [];
    machine.subscribe((nextState, prevState, event) => {
        seen.push({ nextState, prevState, event });
    });

    const firstSnapshot = machine.snapshot();
    machine.send('INC');
    machine.send('MOUNT');

    assert(firstSnapshot.count === 0, 'snapshot should be immutable copy');
    assert(machine.snapshot().count === 1, 'count increments through transition');
    assert(machine.snapshot().lifecycle === 'mounted', 'lifecycle transition should apply');
    assert(seen.length === 2, 'listeners should observe transitions');
});

await test('Badge pilot: state and legacy methods stay aligned', async () => {
    const dom = installFakeDom();
    try {
        const { Badge } = await importModule('packages/javascript/browser/ui_components/common/Badge/Badge.js');
        const host = dom.document.createElement('div');
        host.style.position = '';
        dom.document.body.appendChild(host);

        const badge = new Badge({ text: '128', type: 'count', maxCount: 99, variant: 'primary', attached: true });
        assert(typeof badge.snapshot === 'function', 'Badge exposes snapshot');
        assert(typeof badge.send === 'function', 'Badge exposes send');
        assert(badge.snapshot().content.text === '99+', 'Badge formats count through state');

        badge.render(host);
        assert(badge.snapshot().lifecycle === 'mounted', 'render should mount');

        badge.hide();
        assert(badge.snapshot().visibility === 'hidden', 'hide should update state');
        assert(badge.element.style.display === 'none', 'hide should update DOM');

        badge.show();
        badge.setText('12');
        badge.setVariant('danger');

        assert(badge.snapshot().content.text === '12', 'setText should update state');
        assert(badge.snapshot().content.variant === 'danger', 'setVariant should update state');
        assert(badge.element.textContent === '12', 'setText should update DOM');
    } finally {
        dom.cleanup();
    }
});

await test('TextInput pilot: state and legacy methods stay aligned', async () => {
    const dom = installFakeDom();
    try {
        const { TextInput } = await importModule('packages/javascript/browser/ui_components/form/TextInput/TextInput.js');
        const host = dom.document.createElement('div');
        dom.document.body.appendChild(host);

        const input = new TextInput({ value: 'hello', hint: 'hint text' });

        assert(typeof input.snapshot === 'function', 'TextInput exposes snapshot');
        assert(typeof input.send === 'function', 'TextInput exposes send');
        assert(typeof input.setValue === 'function', 'legacy setValue remains');
        assert(typeof input.setDisabled === 'function', 'legacy setDisabled remains');
        assert(typeof input.clear === 'function', 'legacy clear remains');
        assert(typeof input.mount === 'function', 'legacy mount remains');

        input.mount(host);
        assert(input.snapshot().lifecycle === 'mounted', 'mount should update lifecycle');

        input.setValue('world');
        assert(input.getValue() === 'world', 'setValue should keep getValue compatible');
        assert(input.snapshot().value === 'world', 'setValue should update state');

        input.setDisabled(true);
        assert(input.snapshot().availability === 'disabled', 'setDisabled should update state');
        assert(input.input.disabled === true, 'setDisabled should update DOM');

        input.setError('bad');
        assert(input.snapshot().validation.status === 'error', 'setError should update validation state');
        assert(input.message.textContent === 'bad', 'setError should update message');

        input.clearError();
        assert(input.snapshot().validation.status === 'hint', 'clearError should restore hint state');
        assert(input.message.textContent === 'hint text', 'clearError should restore hint text');

        input.clear();
        assert(input.getValue() === '', 'clear should keep legacy behavior');
        assert(input.snapshot().value === '', 'clear should update state');
    } finally {
        dom.cleanup();
    }
});

await test('NumberInput phase 1: state and legacy methods stay aligned', async () => {
    const dom = installFakeDom();
    try {
        const { NumberInput } = await importModule('packages/javascript/browser/ui_components/form/NumberInput/NumberInput.js');
        const host = dom.document.createElement('div');
        dom.document.body.appendChild(host);

        const input = new NumberInput({ value: 5, min: 0, max: 10, step: 2 });

        assert(typeof input.snapshot === 'function', 'NumberInput exposes snapshot');
        assert(typeof input.send === 'function', 'NumberInput exposes send');
        assert(typeof input.setValue === 'function', 'legacy setValue remains');
        assert(typeof input.setDisabled === 'function', 'legacy setDisabled remains');
        assert(typeof input.clear === 'function', 'legacy clear remains');
        assert(typeof input.mount === 'function', 'legacy mount remains');

        input.mount(host);
        assert(input.snapshot().lifecycle === 'mounted', 'mount should update lifecycle');

        input.setValue(8);
        assert(input.getValue() === 8, 'setValue should keep getValue compatible');
        assert(input.snapshot().value === 8, 'setValue should update state');

        input.send('INCREASE');
        assert(input.getValue() === 10, 'increase should clamp at max');

        input.send('DECREASE');
        assert(input.getValue() === 8, 'decrease should update value');

        input.setDisabled(true);
        assert(input.snapshot().availability === 'disabled', 'setDisabled should update state');
        assert(input.input.disabled === true, 'setDisabled should update DOM');

        input.clear();
        assert(input.getValue() === null, 'clear should reset value to null');
        assert(input.snapshot().value === null, 'clear should update state');
    } finally {
        dom.cleanup();
    }
});

await test('Checkbox phase 1: state and legacy methods stay aligned', async () => {
    const dom = installFakeDom();
    try {
        const { Checkbox } = await importModule('packages/javascript/browser/ui_components/form/Checkbox/Checkbox.js');
        const host = dom.document.createElement('div');
        dom.document.body.appendChild(host);

        const checkbox = new Checkbox({ checked: false, value: 'yes' });

        assert(typeof checkbox.snapshot === 'function', 'Checkbox exposes snapshot');
        assert(typeof checkbox.send === 'function', 'Checkbox exposes send');
        assert(typeof checkbox.setValue === 'function', 'legacy setValue remains');
        assert(typeof checkbox.setDisabled === 'function', 'legacy setDisabled remains');
        assert(typeof checkbox.clear === 'function', 'legacy clear remains');
        assert(typeof checkbox.mount === 'function', 'legacy mount remains');

        checkbox.mount(host);
        assert(checkbox.snapshot().lifecycle === 'mounted', 'mount should update lifecycle');

        checkbox.setChecked(true);
        assert(checkbox.isChecked() === true, 'setChecked should keep legacy behavior');
        assert(checkbox.snapshot().checked === true, 'setChecked should update state');

        checkbox.toggle();
        assert(checkbox.isChecked() === false, 'toggle should invert state');

        checkbox.setDisabled(true);
        assert(checkbox.snapshot().availability === 'disabled', 'setDisabled should update state');
        assert(checkbox.input.disabled === true, 'setDisabled should update DOM');

        checkbox.setValue(true);
        assert(checkbox.isChecked() === true, 'setValue should still drive checked state');

        checkbox.clear();
        assert(checkbox.isChecked() === false, 'clear should uncheck');
    } finally {
        dom.cleanup();
    }
});

await test('Dropdown phase 2: state and legacy methods stay aligned', async () => {
    const dom = installFakeDom();
    try {
        const { Dropdown } = await importModule('packages/javascript/browser/ui_components/form/Dropdown/Dropdown.js');
        const host = dom.document.createElement('div');
        dom.document.body.appendChild(host);

        const dropdown = new Dropdown({
            items: [
                { value: 'a', label: 'Alpha' },
                { value: 'b', label: 'Beta' }
            ],
            value: 'a'
        });

        assert(typeof dropdown.snapshot === 'function', 'Dropdown exposes snapshot');
        assert(typeof dropdown.send === 'function', 'Dropdown exposes send');
        assert(typeof dropdown.setValue === 'function', 'legacy setValue remains');
        assert(typeof dropdown.setItems === 'function', 'legacy setItems remains');
        assert(typeof dropdown.setDisabled === 'function', 'legacy setDisabled remains');
        assert(typeof dropdown.clear === 'function', 'legacy clear remains');
        assert(typeof dropdown.mount === 'function', 'legacy mount remains');

        dropdown.mount(host);
        assert(dropdown.snapshot().lifecycle === 'mounted', 'mount should update lifecycle');
        assert(dropdown.getValue() === 'a', 'initial value should be preserved');

        dropdown.open();
        assert(dropdown.snapshot().open === true, 'open should update state');
        assert(dropdown.menu.style.display === 'block', 'open should update DOM');

        dropdown.setValue('b');
        assert(dropdown.getValue() === 'b', 'setValue should keep legacy behavior');
        assert(dropdown.snapshot().selectedValue === 'b', 'setValue should update state');

        dropdown.setDisabled(true);
        assert(dropdown.snapshot().availability === 'disabled', 'setDisabled should update state');
        assert(dropdown.snapshot().open === false, 'disabled dropdown should close');

        dropdown.setDisabled(false);
        dropdown.send('FILTER', { query: 'alp' });
        assert(dropdown.snapshot().filteredItems.length === 1, 'filter should update filtered items');
        assert(dropdown.snapshot().filteredItems[0].value === 'a', 'filter should preserve matching item');

        dropdown.clear();
        assert(dropdown.getValue() === null, 'clear should reset selection');
        assert(dropdown.snapshot().selectedValue === null, 'clear should update state');
    } finally {
        dom.cleanup();
    }
});

await test('DatePicker phase 2: state and legacy methods stay aligned', async () => {
    const dom = installFakeDom();
    try {
        const { DatePicker } = await importModule('packages/javascript/browser/ui_components/form/DatePicker/DatePicker.js');
        const host = dom.document.createElement('div');
        dom.document.body.appendChild(host);

        const picker = new DatePicker({
            value: '2024-03-15',
            min: '2024-03-01',
            max: '2024-03-31'
        });

        assert(typeof picker.snapshot === 'function', 'DatePicker exposes snapshot');
        assert(typeof picker.send === 'function', 'DatePicker exposes send');
        assert(typeof picker.setValue === 'function', 'legacy setValue remains');
        assert(typeof picker.setDisabled === 'function', 'legacy setDisabled remains');
        assert(typeof picker.clear === 'function', 'legacy clear remains');
        assert(typeof picker.mount === 'function', 'legacy mount remains');

        picker.mount(host);
        assert(picker.snapshot().lifecycle === 'mounted', 'mount should update lifecycle');
        assert(picker.getFormattedValue() === '2024/03/15', 'initial formatted value should be preserved');

        picker.open();
        assert(picker.snapshot().open === true, 'open should update state');
        assert(picker.calendar.style.display === 'block', 'open should update DOM');

        picker.send('SELECT_DAY', { day: 20 });
        assert(picker.getFormattedValue() === '2024/03/20', 'select day should update formatted value');
        assert(picker.snapshot().open === false, 'select day should close calendar');

        picker.setDisabled(true);
        assert(picker.snapshot().availability === 'disabled', 'setDisabled should update state');
        assert(picker.snapshot().open === false, 'disabled datepicker should close');

        picker.clear();
        assert(picker.getValue() === null, 'clear should reset selected date');
        assert(picker.snapshot().selectedValue === null, 'clear should update state');
    } finally {
        dom.cleanup();
    }
});

await test('TimePicker phase 2: state and legacy methods stay aligned', async () => {
    const dom = installFakeDom();
    try {
        const { TimePicker } = await importModule('packages/javascript/browser/ui_components/form/TimePicker/TimePicker.js');
        const host = dom.document.createElement('div');
        dom.document.body.appendChild(host);

        const picker = new TimePicker({ value: '08:15', step: 15 });

        assert(typeof picker.snapshot === 'function', 'TimePicker exposes snapshot');
        assert(typeof picker.send === 'function', 'TimePicker exposes send');
        assert(typeof picker.setValue === 'function', 'legacy setValue remains');
        assert(typeof picker.setDisabled === 'function', 'legacy setDisabled remains');
        assert(typeof picker.clear === 'function', 'legacy clear remains');
        assert(typeof picker.mount === 'function', 'legacy mount remains');

        picker.mount(host);
        assert(picker.snapshot().lifecycle === 'mounted', 'mount should update lifecycle');
        assert(picker.getValue() === '08:15', 'initial value should be preserved');

        picker.open();
        assert(picker.snapshot().open === true, 'open should update state');
        assert(picker.panel.style.display === 'block', 'open should update DOM');

        picker.send('SELECT_HOUR', { value: 9 });
        picker.send('SELECT_MINUTE', { value: 30 });
        picker.send('CONFIRM');

        assert(picker.getValue() === '09:30', 'confirm should commit selected time');
        assert(picker.snapshot().hour === 9, 'confirm should update committed hour');
        assert(picker.snapshot().minute === 30, 'confirm should update committed minute');

        picker.setDisabled(true);
        assert(picker.snapshot().availability === 'disabled', 'setDisabled should update state');
        assert(picker.snapshot().open === false, 'disabled timepicker should close');

        picker.clear();
        assert(picker.getValue() === '', 'clear should reset time');
        assert(picker.snapshot().hour === null, 'clear should reset committed hour');
        assert(picker.snapshot().minute === null, 'clear should reset committed minute');
    } finally {
        dom.cleanup();
    }
});

await test('MultiSelectDropdown phase 2: state and legacy methods stay aligned', async () => {
    const dom = installFakeDom();
    try {
        const { MultiSelectDropdown } = await importModule('packages/javascript/browser/ui_components/form/MultiSelectDropdown/MultiSelectDropdown.js');
        const host = dom.document.createElement('div');
        dom.document.body.appendChild(host);

        const dropdown = new MultiSelectDropdown({
            items: [
                { value: 'a', label: 'Alpha' },
                { value: 'b', label: 'Beta' },
                { value: 'c', label: 'Gamma' }
            ],
            values: ['a'],
            maxCount: 2
        });

        assert(typeof dropdown.snapshot === 'function', 'MultiSelectDropdown exposes snapshot');
        assert(typeof dropdown.send === 'function', 'MultiSelectDropdown exposes send');
        assert(typeof dropdown.setValues === 'function', 'legacy setValues remains');
        assert(typeof dropdown.setItems === 'function', 'legacy setItems remains');
        assert(typeof dropdown.clear === 'function', 'legacy clear remains');
        assert(typeof dropdown.mount === 'function', 'legacy mount remains');

        dropdown.mount(host);
        assert(dropdown.snapshot().lifecycle === 'mounted', 'mount should update lifecycle');
        assert(dropdown.getValues().length === 1 && dropdown.getValues()[0] === 'a', 'initial selected values should be preserved');

        dropdown.open();
        assert(dropdown.snapshot().open === true, 'open should update state');
        assert(dropdown._menu.style.display === 'block', 'open should update DOM');

        dropdown.send('TOGGLE_VALUE', { value: 'b' });
        assert(dropdown.getValues().includes('b'), 'toggle should add value');
        assert(dropdown.snapshot().selectedValues.length === 2, 'toggle should update selected value state');

        dropdown.send('FILTER', { query: 'ga' });
        assert(dropdown.snapshot().filteredItems.length === 1, 'filter should update filtered items');
        assert(dropdown.snapshot().filteredItems[0].value === 'c', 'filter should preserve matching item');

        dropdown.setDisabled(true);
        assert(dropdown.snapshot().availability === 'disabled', 'setDisabled should update state');
        assert(dropdown.snapshot().open === false, 'disabled multiselect should close');

        dropdown.clear();
        assert(dropdown.getValues().length === 0, 'clear should reset selection');
        assert(dropdown.snapshot().selectedValues.length === 0, 'clear should update state');
    } finally {
        dom.cleanup();
    }
});

await test('ChainedInput phase 3: parent state stays aligned with child flow', async () => {
    const dom = installFakeDom();
    try {
        const { ChainedInput } = await importModule('packages/javascript/browser/ui_components/input/ChainedInput/ChainedInput.js');
        const host = dom.document.createElement('div');
        dom.document.body.appendChild(host);

        const input = new ChainedInput({
            fields: [
                {
                    name: 'country',
                    label: 'Country',
                    type: 'select',
                    options: [{ value: 'tw', label: 'Taiwan' }],
                    required: true
                },
                {
                    name: 'city',
                    label: 'City',
                    type: 'text',
                    hideWhenDisabled: true
                }
            ]
        });

        assert(typeof input.snapshot === 'function', 'ChainedInput exposes snapshot');
        assert(typeof input.send === 'function', 'ChainedInput exposes send');
        assert(typeof input.setValues === 'function', 'legacy setValues remains');
        assert(typeof input.reset === 'function', 'legacy reset remains');
        assert(typeof input.validate === 'function', 'legacy validate remains');
        assert(typeof input.mount === 'function', 'legacy mount remains');

        input.mount(host);
        assert(input.snapshot().lifecycle === 'mounted', 'mount should update lifecycle');
        assert(input.snapshot().fields.country.availability === 'enabled', 'first field should start enabled');
        assert(input.snapshot().fields.city.availability === 'disabled', 'dependent field should start disabled');
        assert(input.snapshot().fields.city.visibility === 'hidden', 'dependent hidden field should start hidden');

        await input.setValues({ country: 'tw' });
        assert(input.getValues().country === 'tw', 'setValues should update parent values');
        assert(input.snapshot().fields.city.availability === 'enabled', 'setting parent should enable dependent field');
        assert(input.snapshot().fields.city.visibility === 'visible', 'setting parent should show dependent field');

        await input.setValues({ city: 'Taipei' });
        assert(input.getValues().city === 'Taipei', 'child value should propagate to parent state');

        input.reset();
        assert(input.getValues().country === '', 'reset should clear first field');
        assert(input.getValues().city === '', 'reset should clear dependent field');
        assert(input.snapshot().fields.city.availability === 'disabled', 'reset should disable dependent field');
        assert(input.snapshot().fields.city.visibility === 'hidden', 'reset should hide dependent field again');

        const errors = input.validate();
        assert(errors.length === 1 && errors[0].field === 'country', 'validate should reflect required field state');
    } finally {
        dom.cleanup();
    }
});

await test('DateTimeInput phase 3: composite state stays aligned with child inputs', async () => {
    const dom = installFakeDom();
    try {
        const { DateTimeInput } = await importModule('packages/javascript/browser/ui_components/input/DateTimeInput/DateTimeInput.js');
        const host = dom.document.createElement('div');
        dom.document.body.appendChild(host);

        const input = new DateTimeInput({
            dateValue: '2024-03-10',
            timeValue: '08:15'
        });

        assert(typeof input.snapshot === 'function', 'DateTimeInput exposes snapshot');
        assert(typeof input.send === 'function', 'DateTimeInput exposes send');
        assert(typeof input.setValue === 'function', 'legacy setValue remains');
        assert(typeof input.mount === 'function', 'legacy mount remains');

        input.mount(host);
        assert(input.snapshot().lifecycle === 'mounted', 'mount should update lifecycle');
        assert(input.getValue().date === '2024-03-10', 'initial date should be preserved');
        assert(input.getValue().time === '08:15', 'initial time should be preserved');

        input.setValue('2024-03-12', '09:30');
        assert(input.snapshot().dateValue === '2024-03-12', 'setValue should update date state');
        assert(input.snapshot().timeValue === '09:30', 'setValue should update time state');

        input.setDisabled(true);
        assert(input.snapshot().availability === 'disabled', 'setDisabled should update availability');

        input.clear();
        assert(input.getValue().date === '', 'clear should reset date');
        assert(input.getValue().time === '', 'clear should reset time');
    } finally {
        dom.cleanup();
    }
});

await test('ListInput phase 3: list state stays aligned with item lifecycle', async () => {
    const dom = installFakeDom();
    try {
        const { ListInput } = await importModule('packages/javascript/browser/ui_components/input/ListInput/ListInput.js');
        const host = dom.document.createElement('div');
        dom.document.body.appendChild(host);

        const input = new ListInput({
            minItems: 0,
            maxItems: 3,
            renderItem: (container, index, value) => {
                const marker = dom.document.createElement('span');
                marker.textContent = `${index}:${value?.label || ''}`;
                container.appendChild(marker);
            }
        });

        assert(typeof input.snapshot === 'function', 'ListInput exposes snapshot');
        assert(typeof input.send === 'function', 'ListInput exposes send');
        assert(typeof input.setValues === 'function', 'legacy setValues remains');
        assert(typeof input.mount === 'function', 'legacy mount remains');

        input.mount(host);
        assert(input.snapshot().lifecycle === 'mounted', 'mount should update lifecycle');
        assert(input.snapshot().items.length === 0, 'initial items should match minItems');

        input._addItem({ label: 'first' });
        input._addItem({ label: 'second' });
        assert(input.snapshot().items.length === 2, 'addItem should update state');

        input._moveItem(1, -1);
        assert(input.snapshot().items[0].label === 'second', 'moveItem should reorder state');

        input._removeItem(1);
        assert(input.snapshot().items.length === 1, 'removeItem should update state');

        input.setValues([{ label: 'one' }, { label: 'two' }]);
        assert(input.snapshot().items.length === 2, 'setValues should update state');
        assert(input.getValues()[1].label === 'two', 'setValues should keep legacy behavior');
    } finally {
        dom.cleanup();
    }
});

await test('PhoneListInput phase 3: inherited list contract stays available', async () => {
    const dom = installFakeDom();
    try {
        const { PhoneListInput } = await importModule('packages/javascript/browser/ui_components/input/PhoneListInput/PhoneListInput.js');
        const host = dom.document.createElement('div');
        dom.document.body.appendChild(host);

        const input = new PhoneListInput();

        assert(typeof input.snapshot === 'function', 'PhoneListInput inherits snapshot');
        assert(typeof input.send === 'function', 'PhoneListInput inherits send');
        assert(typeof input.getValues === 'function', 'legacy getValues remains');

        input.mount(host);
        assert(input.snapshot().lifecycle === 'mounted', 'mount should update lifecycle');
        assert(input.snapshot().items.length === 1, 'minItems should create one initial row');
    } finally {
        dom.cleanup();
    }
});

console.log(`\nSummary: ${pass} passed, ${fail} failed`);
if (fail > 0) {
    process.exitCode = 1;
}
