import { ListInput } from '../ListInput/ListInput.js';

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

                // 平台
                const platformSelect = document.createElement('select');
                ['LINE', 'Facebook', 'Instagram', 'Twitter (X)', 'WeChat', 'Telegram', '其他'].forEach(opt => {
                    const option = document.createElement('option');
                    option.value = opt;
                    option.textContent = opt;
                    if (opt === currentValues.platform) option.selected = true;
                    platformSelect.appendChild(option);
                });
                platformSelect.style.cssText = `
                    padding: 8px;
                    border: 1px solid var(--cl-border);
                    border-radius: 4px;
                    width: 120px;
                `;

                // 帳號
                const accountInput = document.createElement('input');
                accountInput.type = 'text';
                accountInput.placeholder = Locale.t('socialMediaList.placeholder');
                accountInput.value = currentValues.account || '';
                accountInput.style.cssText = `
                    padding: 8px;
                    border: 1px solid var(--cl-border);
                    border-radius: 4px;
                    flex: 1;
                `;

                // 事件
                const update = () => {
                    onChange({
                        platform: platformSelect.value,
                        account: accountInput.value
                    });
                };

                platformSelect.addEventListener('change', update);
                accountInput.addEventListener('input', update);

                wrapper.appendChild(platformSelect);
                wrapper.appendChild(accountInput);
                container.appendChild(wrapper);
            },
            ...options
        });
    }
}

export default SocialMediaList;
