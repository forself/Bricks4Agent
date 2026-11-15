// The workbench, validator, and generated page share this bounded event contract.
const EVENTS = Object.freeze({
    BasicButton: ['click'],
    TextInput: ['change', 'blur'],
    NumberInput: ['change', 'blur'],
    TextArea: ['change', 'blur'],
    DatePicker: ['change'],
    ToggleSwitch: ['change'],
    Dropdown: ['change']
});
const CALLBACK_CHANGE = new Set(['NumberInput', 'DatePicker', 'ToggleSwitch', 'Dropdown']);

export function componentEvents(name) { return EVENTS[name] || []; }

export function bindComponentEvent(instance, name, event, handler) {
    if (!componentEvents(name).includes(event)) throw new TypeError('Unsupported component event');
    if (event === 'change' && CALLBACK_CHANGE.has(name)) {
        if (!instance?.options || typeof instance.options !== 'object')
            throw new TypeError('Component does not expose an onChange callback');
        const previous = instance.options.onChange;
        instance.options.onChange = (...args) => {
            if (typeof previous === 'function') previous(...args);
            handler();
        };
        return;
    }
    const element = name === 'BasicButton'
        ? instance?.element?.querySelector?.('button') || instance?.element
        : instance?.element?.querySelector?.('input,textarea') || instance?.element;
    if (!element?.addEventListener) throw new TypeError('Component event element unavailable');
    element.addEventListener(event, handler);
}
