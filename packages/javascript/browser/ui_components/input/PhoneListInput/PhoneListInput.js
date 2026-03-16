import { Dropdown } from '../../form/Dropdown/index.js';
import { TextInput } from '../../form/TextInput/index.js';
import { ListInput } from '../ListInput/index.js';
import Locale from '../../i18n/index.js';

export class PhoneListInput extends ListInput {
    constructor(options = {}) {
        super({
            title: Locale.t('phoneListInput.title'),
            minItems: 1,
            maxItems: 5,
            addButtonText: Locale.t('phoneListInput.addButton'),
            renderItem: (container, index, value, onChange) => {
                const wrapper = document.createElement('div');
                wrapper.style.cssText = 'display: flex; gap: 8px; align-items: center; width: 100%;';

                const currentValues = value || { type: Locale.t('phoneListInput.types').mobile, number: '' };

                const typeSelect = new Dropdown({
                    items: Object.values(Locale.t('phoneListInput.types')).map((opt) => ({
                        value: opt,
                        label: opt
                    })),
                    value: currentValues.type,
                    width: '96px',
                    onChange: (selected) => {
                        currentValues.type = selected;
                        onChange({ ...currentValues });
                    }
                });

                const numberInput = new TextInput({
                    type: 'tel',
                    placeholder: Locale.t('phoneListInput.placeholder'),
                    value: currentValues.number || '',
                    width: '100%',
                    onChange: (inputValue) => {
                        currentValues.number = inputValue;
                        onChange({ ...currentValues });
                    }
                });

                const typeHost = document.createElement('div');
                typeHost.style.cssText = 'width: 96px;';
                typeSelect.mount(typeHost);

                const numberHost = document.createElement('div');
                numberHost.style.cssText = 'flex: 1;';
                numberInput.mount(numberHost);

                wrapper.appendChild(typeHost);
                wrapper.appendChild(numberHost);
                container.appendChild(wrapper);
            },
            ...options
        });
    }
}

export default PhoneListInput;
