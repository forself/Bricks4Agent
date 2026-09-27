import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import {
    setFieldError,
    clearFieldError,
    getFieldError,
    hasFieldError,
    FIELD_ERROR_CONTRACT,
} from '../../ui_components/utils/field-error.js';
import { TextInput } from '../../ui_components/form/TextInput/TextInput.js';
import { Checkbox } from '../../ui_components/form/Checkbox/Checkbox.js';
import { Radio } from '../../ui_components/form/Radio/Radio.js';
import { ToggleSwitch } from '../../ui_components/form/ToggleSwitch/ToggleSwitch.js';
import { ColorPicker } from '../../ui_components/common/ColorPicker/ColorPicker.js';
import { Rating } from '../../ui_components/form/Rating/Rating.js';
import { Slider } from '../../ui_components/form/Slider/Slider.js';
import { TextArea } from '../../ui_components/form/TextArea/TextArea.js';
import { NumberInput } from '../../ui_components/form/NumberInput/NumberInput.js';
import { Dropdown } from '../../ui_components/form/Dropdown/Dropdown.js';
import { MultiSelectDropdown } from '../../ui_components/form/MultiSelectDropdown/MultiSelectDropdown.js';
import { DatePicker } from '../../ui_components/form/DatePicker/DatePicker.js';
import { TimePicker } from '../../ui_components/form/TimePicker/TimePicker.js';
import { DateTimeInput } from '../../ui_components/input/DateTimeInput/DateTimeInput.js';
import { TagInput } from '../../ui_components/form/TagInput/TagInput.js';
import { BatchUploader } from '../../ui_components/form/BatchUploader/BatchUploader.js';
import { CommandComposer } from '../../ui_components/form/CommandComposer/CommandComposer.js';
import { ChainedInput } from '../../ui_components/input/ChainedInput/ChainedInput.js';
import { ListInput } from '../../ui_components/input/ListInput/ListInput.js';
import { FormField } from '../../ui_components/form/FormField/FormField.js';
import { SearchForm } from '../../ui_components/form/SearchForm/SearchForm.js';

const ITEMS = [{ label: 'A', value: 'a' }, { label: 'B', value: 'b' }];

let host;
beforeEach(() => {
    host = document.createElement('div');
    document.body.appendChild(host);
});
afterEach(() => {
    host.remove();
    vi.restoreAllMocks();
});

const messagesIn = (el) => [...el.querySelectorAll('.b4a-field-error')];

describe('field-error helper', () => {
    it('marks the target, shows an alert message and links it with aria-describedby', () => {
        const owner = {};
        const input = document.createElement('input');
        input.setAttribute('aria-describedby', 'hint-1');
        host.appendChild(input);

        setFieldError(owner, '必填', { target: input });
        const message = host.querySelector('.b4a-field-error');
        expect(message.getAttribute('role')).toBe('alert');
        expect(message.textContent).toBe('必填');
        expect(input.getAttribute('aria-invalid')).toBe('true');
        expect(input.getAttribute('aria-describedby')).toBe(`hint-1 ${message.id}`);
        expect(input.style.outline).toContain('var(--cl-danger)');
        expect(getFieldError(owner)).toBe('必填');
        expect(hasFieldError(owner)).toBe(true);

        setFieldError(owner, '格式不正確', { target: input });
        expect(messagesIn(host)).toHaveLength(1);
        expect(getFieldError(owner)).toBe('格式不正確');

        clearFieldError(owner);
        expect(messagesIn(host)).toHaveLength(0);
        expect(input.hasAttribute('aria-invalid')).toBe(false);
        expect(input.getAttribute('aria-describedby')).toBe('hint-1');
        expect(input.style.outline).toBe('');
        expect(hasFieldError(owner)).toBe(false);
    });

    it('restores attributes and inline styles the element had before', () => {
        const owner = {};
        const input = document.createElement('input');
        input.setAttribute('aria-invalid', 'false');
        input.style.outline = '2px dotted blue';
        host.appendChild(input);

        setFieldError(owner, 'x', { target: input });
        setFieldError(owner, 'y', { target: input });
        clearFieldError(owner);
        expect(input.getAttribute('aria-invalid')).toBe('false');
        expect(input.style.outline).toBe('2px dotted blue');
    });

    it('display:false only marks the state, and an empty message clears', () => {
        const owner = {};
        const input = document.createElement('input');
        host.appendChild(input);

        setFieldError(owner, '必填', { target: input });
        setFieldError(owner, '必填', { target: input, display: false });
        expect(messagesIn(host)).toHaveLength(0);
        expect(input.getAttribute('aria-invalid')).toBe('true');
        expect(input.hasAttribute('aria-describedby')).toBe(false);
        expect(getFieldError(owner)).toBe('');
        expect(hasFieldError(owner)).toBe(true);

        setFieldError(owner, '', { target: input });
        expect(input.hasAttribute('aria-invalid')).toBe(false);
        expect(hasFieldError(owner)).toBe(false);
    });

    it('places the message after an element and keeps it there', () => {
        const owner = {};
        const anchor = document.createElement('label');
        const next = document.createElement('p');
        host.append(anchor, next);

        setFieldError(owner, 'x', { target: anchor, after: anchor });
        expect(anchor.nextSibling.className).toBe('b4a-field-error');
        setFieldError(owner, 'y', { target: anchor, after: anchor });
        expect(host.children).toHaveLength(3);
        expect(anchor.nextSibling.textContent).toBe('y');
    });

    it('ignores calls without a target', () => {
        const owner = {};
        expect(() => setFieldError(owner, 'x', { target: null })).not.toThrow();
        expect(hasFieldError(owner)).toBe(false);
        expect(clearFieldError({})).toEqual({});
    });
});

// 每個輸入元件的 setError 契約：[名稱, 建立, 標示目標, 訊息是否在元件外]
const CASES = [
    ['Checkbox', () => new Checkbox({ label: '同意' }), (c) => c.input, true],
    ['Checkbox group', () => Checkbox.createGroup({ items: ITEMS, direction: 'horizontal' }), (g) => g.querySelector('input'), false],
    ['Radio group', () => Radio.createGroup({ items: ITEMS }), (g) => g.querySelector('input'), false],
    ['ToggleSwitch', () => new ToggleSwitch({ label: '啟用' }), (c) => c.element, true],
    ['ColorPicker', () => new ColorPicker({ value: '#ff0000' }), (c) => c.colorInput, true],
    ['Rating', () => new Rating({ value: 2 }), (c) => c._canvas, true],
    ['Slider', () => new Slider({ min: 0, max: 10 }), (c) => c.input, false],
    ['TextArea', () => new TextArea({}), (c) => c.textarea, false],
    ['NumberInput', () => new NumberInput({}), (c) => c.input, false],
    ['Dropdown', () => new Dropdown({ items: ITEMS }), (c) => c.selector, false],
    ['Dropdown searchable', () => new Dropdown({ items: ITEMS, variant: 'searchable' }), (c) => c.input, false],
    ['MultiSelectDropdown', () => new MultiSelectDropdown({ items: ITEMS }), (c) => c._input, false],
    ['DatePicker', () => new DatePicker({}), (c) => c.inputWrapper, false],
    ['TimePicker', () => new TimePicker({}), (c) => c.inputWrapper, false],
    ['DateTimeInput', () => new DateTimeInput({}), (c) => c.datePicker.inputWrapper, false],
    ['TagInput', () => new TagInput({}), (c) => c.input.input, false],
    ['BatchUploader', () => new BatchUploader({}), (c) => c.dropzone, false],
    ['CommandComposer', () => new CommandComposer({}), (c) => c.textarea.textarea, false],
    ['ChainedInput', () => new ChainedInput({ fields: [{ name: 'city', type: 'select', options: ITEMS }] }), (c) => c.element, false],
    ['ListInput', () => new ListInput({}), (c) => c.element, false],
];

const rootOf = (component) => component.element || component;
const mountInto = (component, container) => {
    component.mount(container);
    return component;
};
const destroyOf = (component) => component.destroy?.();

describe.each(CASES)('%s setError contract', (_name, create, targetOf, outside) => {
    it('shows and clears an error message', () => {
        const component = mountInto(create(), host);
        const target = targetOf(component);

        expect(component[FIELD_ERROR_CONTRACT]).toBe(true);
        expect(component.setError('必填')).toBe(component);
        const messages = messagesIn(host);
        expect(messages).toHaveLength(1);
        expect(messages[0].textContent).toBe('必填');
        expect(rootOf(component).contains(messages[0])).toBe(!outside);
        expect(target.getAttribute('aria-invalid')).toBe('true');
        expect(target.getAttribute('aria-describedby')).toContain(messages[0].id);

        expect(component.clearError()).toBe(component);
        expect(messagesIn(host)).toHaveLength(0);
        expect(target.hasAttribute('aria-invalid')).toBe(false);
        destroyOf(component);
    });

    it('display:false marks the state without text', () => {
        const component = mountInto(create(), host);
        const target = targetOf(component);

        component.setError('必填', { display: false });
        expect(messagesIn(host)).toHaveLength(0);
        expect(target.getAttribute('aria-invalid')).toBe('true');

        component.setError('');
        expect(target.hasAttribute('aria-invalid')).toBe(false);
        destroyOf(component);
    });

    it('leaves nothing behind after destroy', () => {
        const component = mountInto(create(), host);
        component.setError('必填');
        destroyOf(component);
        expect(messagesIn(host)).toHaveLength(0);
    });
});

describe('TextInput setError display option', () => {
    it('keeps the default behaviour and adds aria-invalid', () => {
        const input = new TextInput({}).mount(host);
        input.setError('必填');
        expect(host.querySelector('.text-input__error').textContent).toBe('必填');
        expect(input.input.getAttribute('aria-invalid')).toBe('true');
        expect(input.input.style.borderColor).toBe('var(--cl-danger)');

        input.clearError();
        expect(input.input.hasAttribute('aria-invalid')).toBe(false);
        input.destroy();
    });

    it('display:false keeps the red border but renders no text', () => {
        const input = new TextInput({}).mount(host);
        input.setError('必填', { display: false });
        expect(host.querySelector('.text-input__error')).toBeNull();
        expect(input.input.getAttribute('aria-invalid')).toBe('true');
        expect(input.input.style.borderColor).toBe('var(--cl-danger)');
        input.destroy();
    });
});

describe('FormField markControl', () => {
    it('does not touch the inner component by default', () => {
        const dropdown = new Dropdown({ items: ITEMS });
        const field = new FormField({ label: '類別', component: dropdown });
        host.appendChild(field.element);
        field.setError('必填');
        expect(dropdown.selector.hasAttribute('aria-invalid')).toBe(false);
        expect(messagesIn(host)).toHaveLength(0);
    });

    it('marks the inner component without duplicating the text when enabled', () => {
        const dropdown = new Dropdown({ items: ITEMS });
        const field = new FormField({ label: '類別', component: dropdown, markControl: true });
        host.appendChild(field.element);
        field.setError('必填');
        expect(dropdown.selector.getAttribute('aria-invalid')).toBe('true');
        expect(messagesIn(host)).toHaveLength(0);
        expect(field.element.textContent).toContain('必填');

        field.clearError();
        expect(dropdown.selector.hasAttribute('aria-invalid')).toBe(false);
    });
});

describe('SearchForm field errors', () => {
    const fields = [
        { key: 'category', label: '類別', type: 'select', options: ITEMS, required: true },
        { key: 'keyword', label: '關鍵字', type: 'text', required: true },
    ];

    it('keeps the previous output by default', () => {
        const form = new SearchForm({ fields, requiredMessage: '必填' });
        form.mount(host);
        const dropdown = form._fieldComponents.get('category');

        form._handleSearch();
        // SearchForm 的下拉是可搜尋型，aria-invalid 標在輸入框、紅框畫在選擇器
        expect(dropdown.input.hasAttribute('aria-invalid')).toBe(false);
        expect(dropdown.selector.style.outline).toBe('');
        expect(messagesIn(host)).toHaveLength(0);
        const errorEl = host.querySelector('.search-form-field-error');
        expect(errorEl.textContent).toBe('必填');

        // 既有 setError 的元件（TextInput）照舊收到錯誤並顯示自己的文字
        form._setFieldError('keyword', '必填');
        expect(host.querySelector('.text-input__error').textContent).toBe('必填');
        form.destroy();
    });

    it('marks every field component and shows the text once when markInvalidFields is on', () => {
        const form = new SearchForm({ fields, requiredMessage: '必填', markInvalidFields: true });
        form.mount(host);
        const dropdown = form._fieldComponents.get('category');
        const textInput = form._fieldComponents.get('keyword');

        form._handleSearch();
        expect(dropdown.input.getAttribute('aria-invalid')).toBe('true');
        expect(dropdown.selector.style.outline).toContain('var(--cl-danger)');
        expect(messagesIn(host)).toHaveLength(0);

        form._setFieldError('keyword', '必填');
        expect(textInput.input.getAttribute('aria-invalid')).toBe('true');
        expect(host.querySelector('.text-input__error')).toBeNull();

        form._handleChange('category', 'a');
        expect(dropdown.input.hasAttribute('aria-invalid')).toBe(false);
        expect(dropdown.selector.style.outline).toBe('');
        form.destroy();
    });
});
