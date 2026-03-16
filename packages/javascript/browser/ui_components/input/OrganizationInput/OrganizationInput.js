import { ChainedInput } from '../ChainedInput/index.js';
import { mockApi } from '../utils/mockApi.js';

import Locale from '../../i18n/index.js';
export class OrganizationInput extends ChainedInput {
    constructor(options = {}) {
        const fields = [
            {
                name: 'level1',
                type: 'select',
                label: Locale.t('organizationInput.level1Label'),
                placeholder: Locale.t('organizationInput.placeholder'),
                required: true,
                flex: 1,
                loadOptions: async () => {
                    const units = await mockApi.getUnitList('');
                    return units.map(u => ({ value: u.id, label: u.name }));
                }
            },
            {
                name: 'level2',
                type: 'select',
                label: Locale.t('organizationInput.level2Label'),
                placeholder: Locale.t('organizationInput.placeholder'),
                flex: 1,
                hideWhenEmpty: true,
                loadOptions: async (parentId) => {
                    if (!parentId) return [];
                    const units = await mockApi.getUnitList(parentId);
                    return units.map(u => ({ value: u.id, label: u.name }));
                }
            },
            {
                name: 'level3',
                type: 'select',
                label: Locale.t('organizationInput.level3Label'),
                placeholder: Locale.t('organizationInput.placeholder'),
                flex: 1,
                hideWhenEmpty: true,
                loadOptions: async (parentId) => {
                    if (!parentId) return [];
                    const units = await mockApi.getUnitList(parentId);
                    return units.map(u => ({ value: u.id, label: u.name }));
                }
            },
            {
                name: 'level4',
                type: 'select',
                label: Locale.t('organizationInput.level4Label'),
                placeholder: Locale.t('organizationInput.placeholder'),
                flex: 1,
                hideWhenEmpty: true,
                loadOptions: async (parentId) => {
                    if (!parentId) return [];
                    const units = await mockApi.getUnitList(parentId);
                    return units.map(u => ({ value: u.id, label: u.name }));
                }
            }
        ];

        super({
            ...options,
            fields,
            gap: '8px'
        });
    }

    /**
     * 取得選定的最底層單位
     */
    getSelectedUnit() {
        const values = this.getValues();
        //由後往前找第一個有值的
        const levels = ['level4', 'level3', 'level2', 'level1'];
        for (const level of levels) {
            if (values[level]) return { level, id: values[level] };
        }
        return null;
    }
}

export default OrganizationInput;
