import { Dropdown } from '../../form/Dropdown/index.js';
import { TextInput } from '../../form/TextInput/index.js';
import { ListInput } from '../ListInput/index.js';
import Locale from '../../i18n/index.js';

export class SocialMediaList extends ListInput {
    constructor(options = {}) {
        super({
            title: Locale.t('socialMediaList.title'),
            minItems: 0,
            maxItems: 5,
            addButtonText: Locale.t('socialMediaList.addButton'),
            renderItem: (container, index, value, onChange) => {
                const wrapper = document.createElement('div');
                wrapper.style.cssText = 'display: flex; gap: 8px; align-items: center; width: 100%;';

                const currentValues = value || { platform: 'LINE', account: '' };

                const platformSelect = new Dropdown({
                    items: ['LINE', 'Facebook', 'Instagram', 'Twitter (X)', 'WeChat', 'Telegram', 'Other'].map((opt) => ({
                        value: opt,
                        label: opt
                    })),
                    value: currentValues.platform,
                    width: '120px',
                    onChange: (selected) => {
                        currentValues.platform = selected;
                        onChange({ ...currentValues });
                    }
                });

                const accountInput = new TextInput({
                    type: 'text',
                    placeholder: Locale.t('socialMediaList.placeholder'),
                    value: currentValues.account || '',
                    width: '100%',
                    onChange: (inputValue) => {
                        currentValues.account = inputValue;
                        onChange({ ...currentValues });
                    }
                });

                const platformHost = document.createElement('div');
                platformHost.style.cssText = 'width: 120px;';
                platformSelect.mount(platformHost);

                const accountHost = document.createElement('div');
                accountHost.style.cssText = 'flex: 1;';
                accountInput.mount(accountHost);

                wrapper.appendChild(platformHost);
                wrapper.appendChild(accountHost);
                container.appendChild(wrapper);
            },
            ...options
        });
    }
}

export default SocialMediaList;
