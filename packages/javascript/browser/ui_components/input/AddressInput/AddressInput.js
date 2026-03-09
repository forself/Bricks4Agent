import { ChainedInput } from '../ChainedInput/ChainedInput.js';
import { mockApi } from '../utils/mockApi.js';

import Locale from '../../i18n/index.js';
export class AddressInput extends ChainedInput {
    constructor(options = {}) {
        const fields = [
            {
                name: 'city',
                type: 'select',
                label: Locale.t('addressInput.cityLabel'),
                placeholder: Locale.t('addressInput.cityPlaceholder'),
                required: true,
                flex: 1,
                loadOptions: async () => {
                    const cities = await mockApi.getCities();
                    return cities;
                }
            },
            {
                name: 'district',
                type: 'select',
                label: Locale.t('addressInput.districtLabel'),
                placeholder: Locale.t('addressInput.districtPlaceholder'),
                required: true,
                flex: 1,
                loadOptions: async (cityValue) => {
                    if (!cityValue) return [];
                    const districts = await mockApi.getDistricts(cityValue);
                    return districts.map(d => ({ value: d, label: d }));
                }
            },
            {
                name: 'address',
                type: 'text',
                label: Locale.t('addressInput.detailLabel'),
                placeholder: Locale.t('addressInput.detailPlaceholder'),
                required: true,
                flex: 2,
                minWidth: '200px'
            }
        ];

        super({
            ...options,
            fields,
            gap: '8px'
        });
    }

    // 取得完整地址字串
    getFullAddress() {
        const { city, district, address } = this.getValues();
        // 從 select 元素取得選中項的顯示文字（label）
        const cityElement = this.fieldElements.get('city');
        let cityLabel = city || '';
        if (cityElement && cityElement.input) {
            const selectedOption = cityElement.input.options?.[cityElement.input.selectedIndex];
            if (selectedOption && selectedOption.text) {
                cityLabel = selectedOption.text;
            }
        }
        return [cityLabel, district, address].filter(Boolean).join('').trim();
    }
}

export default AddressInput;
