import { ChainedInput } from '../ChainedInput/index.js';

import Locale from '../../i18n/index.js';
export class StudentInput extends ChainedInput {
    constructor(options = {}) {
        const fields = [
            {
                name: 'isStudent',
                type: 'checkbox',
                checkboxLabel: Locale.t('studentInput.checkboxLabel'),
                label: Locale.t('studentInput.statusLabel'),
                flex: 0,
                minWidth: 'auto'
            },
            {
                name: 'schoolName',
                type: 'text',
                label: Locale.t('studentInput.schoolLabel'),
                placeholder: Locale.t('studentInput.schoolPlaceholder'),
                required: true,
                flex: 1,
                hideWhenDisabled: true  // 非學生時隱藏學校欄位
            }
        ];

        super({
            ...options,
            fields,
            layout: 'horizontal',
            gap: '20px'
        });
    }
}

export default StudentInput;
