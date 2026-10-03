/**
 * Barcode 字串（命名空間 barcode）。
 * Barcode.js 以副作用方式匯入本檔，載入時自行註冊到 Locale。
 */
import Locale from '../../i18n/index.js';

Locale.register('zh-TW', 'barcode', {
    ariaLabel: '條碼（{format}）：{value}',
    ariaEmpty: '條碼（沒有內容）',
    errorInvalidChar: '條碼含有這個格式不支援的字元：{char}',
    errorInvalidLength: '條碼的長度不符合格式',
    errorBadCheckDigit: '條碼的檢查碼錯誤，應為 {expected}',
    errorInvalidOption: '條碼的設定不正確',
    errorUnknown: '無法產生條碼'
});

Locale.register('en', 'barcode', {
    ariaLabel: 'Barcode ({format}): {value}',
    ariaEmpty: 'Barcode (empty)',
    errorInvalidChar: 'The barcode contains a character this format does not support: {char}',
    errorInvalidLength: 'The barcode length does not match the format',
    errorBadCheckDigit: 'Wrong check digit; expected {expected}',
    errorInvalidOption: 'The barcode settings are invalid',
    errorUnknown: 'Unable to generate the barcode'
});
