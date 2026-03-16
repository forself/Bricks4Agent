import { Dropdown } from '../../form/Dropdown/index.js';
import { NumberInput } from '../../form/NumberInput/index.js';
import { TextInput } from '../../form/TextInput/index.js';
import { ListInput } from '../ListInput/index.js';
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

                const fields = [
                    { name: 'name', label: Locale.t('personInfoList.nameLabel'), type: 'text', placeholder: Locale.t('personInfoList.namePlaceholder') },
                    { name: 'gender', label: Locale.t('personInfoList.genderLabel'), type: 'select', options: Object.values(Locale.t('personInfoList.genderOptions')) },
                    { name: 'age', label: Locale.t('personInfoList.ageLabel'), type: 'number', min: 0, max: 150 },
                    { name: 'id', label: Locale.t('personInfoList.idLabel'), type: 'text', maxLength: 20, placeholder: Locale.t('personInfoList.idPlaceholder') },
                    { name: 'otherId', label: Locale.t('personInfoList.otherIdLabel'), type: 'text' }
                ];

                const currentValues = value || {};

                fields.forEach((field) => {
                    const fieldDiv = document.createElement('div');
                    fieldDiv.style.cssText = 'display: flex; flex-direction: column; gap: 4px;';

                    const label = document.createElement('label');
                    label.textContent = field.label;
                    label.style.cssText = 'font-size: var(--cl-font-size-md); color: var(--cl-text-secondary);';
                    fieldDiv.appendChild(label);

                    let component;
                    if (field.type === 'select') {
                        component = new Dropdown({
                            items: field.options.map((opt) => ({ value: opt, label: opt })),
                            value: currentValues[field.name] || field.options[0],
                            width: '100%',
                            onChange: (selected) => {
                                currentValues[field.name] = selected;
                                onChange({ ...currentValues });
                            }
                        });
                    } else if (field.type === 'number') {
                        component = new NumberInput({
                            value: currentValues[field.name] ?? null,
                            min: field.min ?? Number.NEGATIVE_INFINITY,
                            max: field.max ?? Number.POSITIVE_INFINITY,
                            showButtons: false,
                            width: '100%',
                            onChange: (selected) => {
                                currentValues[field.name] = selected;
                                onChange({ ...currentValues });
                            }
                        });
                    } else {
                        component = new TextInput({
                            type: field.type,
                            value: currentValues[field.name] || '',
                            placeholder: field.placeholder || '',
                            maxLength: field.maxLength || null,
                            width: '100%',
                            onChange: (selected) => {
                                currentValues[field.name] = selected;
                                onChange({ ...currentValues });
                            }
                        });
                    }

                    const host = document.createElement('div');
                    host.style.cssText = 'width: 100%;';
                    component.mount(host);
                    fieldDiv.appendChild(host);
                    wrapper.appendChild(fieldDiv);
                });

                container.appendChild(wrapper);
            },
            ...options
        });
    }
}

export default PersonInfoList;
