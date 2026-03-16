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

console.log(`\nSummary: ${pass} passed, ${fail} failed`);
if (fail > 0) {
    process.exitCode = 1;
}
