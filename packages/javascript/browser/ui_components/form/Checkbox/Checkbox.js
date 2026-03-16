import { createComponentState } from '../../utils/component-state.js';

export class Checkbox {
    constructor(options = {}) {
        this.options = {
            label: '',
            checked: false,
            value: true,
            disabled: false,
            size: 'medium',
            onChange: null,
            ...options
        };

        this.checked = !!this.options.checked;
        this.input = null;
        this.box = null;
        this.element = this._createElement();
        this._state = createComponentState(this._buildInitialState(), {
            MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
            DESTROY: (state) => ({ ...state, lifecycle: 'destroyed' }),
            SHOW: (state) => ({ ...state, visibility: 'visible' }),
            HIDE: (state) => ({ ...state, visibility: 'hidden' }),
            SET_CHECKED: (state, payload) => ({
                ...state,
                checked: !!payload?.checked
            }),
            TOGGLE: (state) => ({
                ...state,
                checked: !state.checked
            }),
            SET_DISABLED: (state, payload) => ({
                ...state,
                availability: payload?.disabled ? 'disabled' : 'enabled'
            })
        });
        this._applyState();
    }

    _buildInitialState() {
        return {
            lifecycle: 'created',
            visibility: 'visible',
            availability: this.options.disabled ? 'disabled' : 'enabled',
            checked: this.checked
        };
    }

    _getCheckmarkMarkup(size) {
        const iconSize = Math.max(10, size - 8);
        return `
            <svg viewBox="0 0 12 12" fill="none" style="width: ${iconSize}px; height: ${iconSize}px;">
                <path d="M2 6L5 9L10 3" stroke="var(--cl-text-inverse)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
        `;
    }

    _getSizeStyles() {
        const sizes = {
            small: { box: '14px', font: '12px', gap: '6px' },
            medium: { box: '18px', font: '14px', gap: '8px' },
            large: { box: '22px', font: '16px', gap: '10px' }
        };
        return sizes[this.options.size] || sizes.medium;
    }

    _createElement() {
        const { label, disabled } = this.options;
        const sizeStyles = this._getSizeStyles();

        const container = document.createElement('label');
        container.className = 'checkbox';
        container.style.cssText = `
            display: inline-flex;
            align-items: center;
            gap: ${sizeStyles.gap};
            cursor: ${disabled ? 'not-allowed' : 'pointer'};
            user-select: none;
            opacity: ${disabled ? '0.6' : '1'};
            transition: opacity var(--cl-transition-fast);
        `;

        const input = document.createElement('input');
        input.type = 'checkbox';
        input.checked = this.checked;
        input.disabled = disabled;
        input.style.cssText = `
            position: absolute;
            opacity: 0;
            width: 0;
            height: 0;
        `;
        input.addEventListener('change', () => {
            this.send('SET_CHECKED', { checked: input.checked });
            if (this.options.onChange) {
                this.options.onChange(this.checked, this.options.value);
            }
        });

        const box = document.createElement('span');
        box.className = 'checkbox__box';
        box.style.cssText = `
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: ${sizeStyles.box};
            height: ${sizeStyles.box};
            border: 2px solid ${this.checked ? 'var(--cl-primary)' : 'var(--cl-text-light)'};
            border-radius: var(--cl-radius-sm);
            background: ${this.checked ? 'var(--cl-primary)' : 'var(--cl-bg)'};
            transition: all var(--cl-transition);
            box-sizing: border-box;
        `;

        const labelSpan = document.createElement('span');
        labelSpan.className = 'checkbox__label';
        labelSpan.textContent = label;
        labelSpan.style.cssText = `
            font-size: ${sizeStyles.font};
            font-family: var(--cl-font-family);
            color: var(--cl-text);
        `;

        container.appendChild(input);
        container.appendChild(box);
        container.appendChild(labelSpan);

        container.addEventListener('mouseenter', () => {
            if (this.snapshot().availability === 'disabled' || this.checked) return;
            box.style.borderColor = 'var(--cl-primary)';
        });

        container.addEventListener('mouseleave', () => {
            if (this.checked) return;
            box.style.borderColor = 'var(--cl-text-light)';
        });

        this.input = input;
        this.box = box;

        return container;
    }

    _applyState() {
        const state = this.snapshot();
        const size = parseInt(this._getSizeStyles().box, 10);

        this.checked = state.checked;
        this.options.checked = state.checked;
        this.options.disabled = state.availability === 'disabled';

        if (this.element) {
            this.element.style.display = state.visibility === 'hidden' ? 'none' : '';
            this.element.style.cursor = state.availability === 'disabled' ? 'not-allowed' : 'pointer';
            this.element.style.opacity = state.availability === 'disabled' ? '0.6' : '1';
        }

        if (this.input) {
            this.input.checked = state.checked;
            this.input.disabled = state.availability === 'disabled';
        }

        if (this.box) {
            this.box.style.borderColor = state.checked ? 'var(--cl-primary)' : 'var(--cl-text-light)';
            this.box.style.background = state.checked ? 'var(--cl-primary)' : 'var(--cl-bg)';
            this.box.innerHTML = state.checked ? this._getCheckmarkMarkup(size) : '';
        }
    }

    snapshot() {
        return this._state.snapshot();
    }

    send(event, payload = null) {
        const nextState = this._state.send(event, payload);
        this._applyState();
        return nextState;
    }

    isChecked() {
        return this.checked;
    }

    setChecked(checked) {
        this.send('SET_CHECKED', { checked });
    }

    getValue() {
        return this.checked;
    }

    setValue(value) {
        this.setChecked(!!value);
    }

    setDisabled(disabled) {
        this.send('SET_DISABLED', { disabled });
    }

    clear() {
        this.send('SET_CHECKED', { checked: false });
    }

    toggle() {
        this.send('TOGGLE');
    }

    show() {
        this.send('SHOW');
    }

    hide() {
        this.send('HIDE');
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) {
            target.appendChild(this.element);
            this.send('MOUNT');
        }
        return this;
    }

    destroy() {
        this.send('DESTROY');
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }

    static createGroup(config = {}) {
        const {
            items = [],
            name = 'checkbox-group',
            direction = 'vertical',
            onChange = () => {},
            ...options
        } = config;

        const group = document.createElement('div');
        group.className = 'checkbox-group';
        group.style.cssText = `
            display: flex;
            flex-direction: ${direction === 'vertical' ? 'column' : 'row'};
            gap: ${direction === 'vertical' ? '8px' : '16px'};
            flex-wrap: wrap;
        `;

        const checkboxes = [];

        items.forEach((item) => {
            const checkbox = new Checkbox({
                label: item.label,
                value: item.value,
                checked: item.checked || false,
                disabled: item.disabled || false,
                ...options,
                onChange: (checked, value) => {
                    const selectedValues = checkboxes
                        .filter((entry) => entry.isChecked())
                        .map((entry) => entry.options.value);
                    onChange(selectedValues, { checked, value });
                }
            });
            checkboxes.push(checkbox);
            group.appendChild(checkbox.element);
        });

        group.getValues = () => checkboxes.filter((entry) => entry.isChecked()).map((entry) => entry.options.value);

        group.setValues = (values) => {
            checkboxes.forEach((entry) => {
                entry.setChecked(values.includes(entry.options.value));
            });
        };

        group.selectAll = () => {
            checkboxes.forEach((entry) => entry.setChecked(true));
            onChange(group.getValues());
        };

        group.deselectAll = () => {
            checkboxes.forEach((entry) => entry.setChecked(false));
            onChange(group.getValues());
        };

        group.toggleAll = () => {
            const allChecked = checkboxes.every((entry) => entry.isChecked());
            if (allChecked) {
                group.deselectAll();
            } else {
                group.selectAll();
            }
        };

        group.invertSelection = () => {
            checkboxes.forEach((entry) => entry.setChecked(!entry.isChecked()));
            onChange(group.getValues());
        };

        group.mount = (container) => {
            const target = typeof container === 'string' ? document.querySelector(container) : container;
            if (target) {
                target.appendChild(group);
            }
            return group;
        };

        return group;
    }
}

export default Checkbox;
