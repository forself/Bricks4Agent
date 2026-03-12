import { ListInput } from '../ListInput/ListInput.js';

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

                // 類型
                const typeSelect = document.createElement('select');
                Object.values(Locale.t('phoneListInput.types')).forEach(opt => {
                    const option = document.createElement('option');
                    option.value = opt;
                    option.textContent = opt;
                    if (opt === currentValues.type) option.selected = true;
                    typeSelect.appendChild(option);
                });
                typeSelect.style.cssText = `
                    padding: 8px;
                    border: 1px solid var(--cl-border);
                    border-radius: var(--cl-radius-sm);
                    width: 80px;
                `;

                // 號碼
                const numberInput = document.createElement('input');
                numberInput.type = 'tel';
                numberInput.placeholder = Locale.t('phoneListInput.placeholder');
                numberInput.value = currentValues.number || '';
                numberInput.style.cssText = `
                    padding: 8px;
                    border: 1px solid var(--cl-border);
                    border-radius: var(--cl-radius-sm);
                    flex: 1;
                `;

                // 事件
                const update = () => {
                    onChange({
                        type: typeSelect.value,
                        number: numberInput.value
                    });
                };

                typeSelect.addEventListener('change', update);
                numberInput.addEventListener('input', update);

                wrapper.appendChild(typeSelect);
                wrapper.appendChild(numberInput);
                container.appendChild(wrapper);
            },
            ...options
        });
    }
}

export default PhoneListInput;
