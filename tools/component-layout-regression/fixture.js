import { Dropdown, DatePicker, FormField, FormRow } from '../../packages/javascript/browser/ui_components/index.js';

for (const width of [720, 480, 320]) {
    const sample = document.createElement('section');
    sample.className = 'sample';
    sample.style.width = `min(100%, ${width}px)`;
    const heading = document.createElement('h2');
    heading.textContent = `容器 ${width}px`;
    sample.append(heading);
    const fields = [
        new FormField({ fieldName: 'city', label: '縣市與行政區測試長標籤', col: 4, required: true,
            component: new Dropdown({ variant: 'searchable', items: [{value:'test', label:'合成測試行政區名稱'}], placeholder: '請選擇縣市行政區' }) }),
        new FormField({ fieldName: 'date', label: '出生年月日', col: 4, component: new DatePicker({ value:'2000-01-01', format:'taiwan' }) }),
        new FormField({ fieldName: 'district', label: '其他欄位', col: 4, component: new Dropdown({ items:[{value:'test',label:'合成測試長名稱'}], placeholder:'合成測試長名稱' }) }),
    ];
    new FormRow({ fields }).mount(sample);
    document.querySelector('#fixture').append(sample);
}
function measure() {
    requestAnimationFrame(() => {
        const failures = [];
        for (const row of document.querySelectorAll('.form-row')) {
            const fields = [...row.children];
            for (let i=0;i<fields.length-1;i++) {
                if (fields[i].getBoundingClientRect().right > fields[i+1].getBoundingClientRect().left) failures.push('欄位交疊');
            }
        }
        for (const wrapper of document.querySelectorAll('.dropdown__selector,.datepicker__input-wrapper')) {
            const text = wrapper.querySelector('.dropdown__input,.dropdown__display,.datepicker__display');
            const icon = wrapper.querySelector('.dropdown__icons,.datepicker__icon');
            if (text && icon && text.getBoundingClientRect().right > icon.getBoundingClientRect().left + 1) failures.push('文字與圖示交疊');
            if (wrapper.scrollWidth > wrapper.clientWidth + 1) failures.push('元件溢出');
        }
        document.querySelector('#result').textContent = JSON.stringify({font:document.body.className||'normal', failures},null,2);
    });
}
document.querySelector('#large').onclick = () => { document.body.classList.add('large'); measure(); };
document.querySelector('#normal').onclick = () => { document.body.classList.remove('large'); measure(); };
measure();
