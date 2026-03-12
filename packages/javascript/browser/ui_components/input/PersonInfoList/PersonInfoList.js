import { ListInput } from '../ListInput/ListInput.js';

import Locale from '../../i18n/index.js';
export class PersonInfoList extends ListInput {
    constructor(options = {}) {
        super({
            title: Locale.t('personInfoList.title'),
            minItems: 1,
            maxItems: 5,
            addButtonText: Locale.t('personInfoList.addButton'),
            renderItem: (container, index, value, onChange) => {
                const wrapper = document.createElement('div');
                wrapper.style.cssText = `
                    display: grid;
                    grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
                    gap: 12px;
                `;

                // 定義欄位
                const fields = [
                    { name: 'name', label: Locale.t('personInfoList.nameLabel'), type: 'text', placeholder: Locale.t('personInfoList.namePlaceholder') },
                    { name: 'gender', label: Locale.t('personInfoList.genderLabel'), type: 'select', options: Object.values(Locale.t('personInfoList.genderOptions')) },
                    { name: 'age', label: Locale.t('personInfoList.ageLabel'), type: 'number', min: 0, max: 150 },
                    { name: 'id', label: Locale.t('personInfoList.idLabel'), type: 'text', maxLength: 20, placeholder: Locale.t('personInfoList.idPlaceholder') }, // 用戶要求 20 碼自由填寫
                    { name: 'otherId', label: Locale.t('personInfoList.otherIdLabel'), type: 'text' }
                ];

                const currentValues = value || {};

                fields.forEach(field => {
                    const fieldDiv = document.createElement('div');
                    fieldDiv.style.cssText = 'display: flex; flex-direction: column; gap: 4px;';

                    const label = document.createElement('label');
                    label.textContent = field.label;
                    label.style.cssText = 'font-size: var(--cl-font-size-md); color: var(--cl-text-secondary);';
                    fieldDiv.appendChild(label);

                    let input;
                    if (field.type === 'select') {
                        input = document.createElement('select');
                        field.options.forEach(opt => {
                            const option = document.createElement('option');
                            option.value = opt;
                            option.textContent = opt;
                            input.appendChild(option);
                        });
                        input.value = currentValues[field.name] || field.options[0];
                    } else {
                        input = document.createElement('input');
                        input.type = field.type;
                        if (field.placeholder) input.placeholder = field.placeholder;
                        if (field.maxLength) input.maxLength = field.maxLength;
                        if (field.min) input.min = field.min;
                        if (field.max) input.max = field.max;
                        input.value = currentValues[field.name] || '';
                    }

                    input.style.cssText = `
                        padding: 6px 10px;
                        border: 1px solid var(--cl-border);
                        border-radius: var(--cl-radius-sm);
                        font-family: inherit;
                        font-size: var(--cl-font-size-lg);
                        width: 100%;
                    `;

                    // 綁定事件
                    const updateField = () => {
                        currentValues[field.name] = input.value;
                        onChange(currentValues);
                    };
                    input.addEventListener('input', updateField);
                    input.addEventListener('change', updateField);

                    fieldDiv.appendChild(input);
                    wrapper.appendChild(fieldDiv);
                });

                container.appendChild(wrapper);
            },
            ...options
        });
    }
}

export default PersonInfoList;
