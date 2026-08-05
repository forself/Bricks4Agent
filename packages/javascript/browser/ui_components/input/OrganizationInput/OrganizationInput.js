import { ChainedInput } from '../ChainedInput/index.js';

import Locale from '../../i18n/index.js';
export class OrganizationInput extends ChainedInput {
    constructor(options = {}) {
        const loadUnits = options.loadUnits;
        const getUnits = async (parentId) => {
            if (typeof loadUnits !== 'function') {
                throw new Error('OrganizationInput requires a real loadUnits data loader.');
            }
            const units = await loadUnits(parentId);
            return units.map(u => ({ value: u?.value ?? u?.id, label: u?.label ?? u?.name }));
        };
        const fields = [
            {
                name: 'level1',
                type: 'select',
                label: Locale.t('organizationInput.level1Label'),
                placeholder: Locale.t('organizationInput.placeholder'),
                required: true,
                flex: 1,
                loadOptions: async () => getUnits('')
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
                    return getUnits(parentId);
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
                    return getUnits(parentId);
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
                    return getUnits(parentId);
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
