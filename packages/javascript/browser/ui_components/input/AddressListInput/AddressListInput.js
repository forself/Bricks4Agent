import { ListInput } from '../ListInput/index.js';
import { AddressInput } from '../AddressInput/index.js';

import Locale from '../../i18n/index.js';
export class AddressListInput extends ListInput {
    constructor(options = {}) {
        super({
            title: Locale.t('addressListInput.title'),
            minItems: 1,
            maxItems: 3,
            addButtonText: Locale.t('addressListInput.addButton'),
            renderItem: (container, index, value, onChange) => {
                const addressInput = new AddressInput({
                    layout: 'horizontal',
                    onChange: (newValue) => {
                        onChange(newValue);
                    }
                });

                addressInput.mount(container);

                if (value && Object.keys(value).length > 0) {
                    // setValues 是 async，需要等待選項載入完成
                    addressInput.setValues(value).catch(err => {
                        console.warn('AddressListInput: 設定初始值失敗', err);
                    });
                }
            },
            ...options
        });
    }
}

export default AddressListInput;
