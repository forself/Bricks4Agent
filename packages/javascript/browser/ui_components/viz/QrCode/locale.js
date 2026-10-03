/**
 * QrCode 字串（命名空間 qrCode）。
 * QrCode.js 以副作用方式匯入本檔，載入時自行註冊到 Locale。
 */
import Locale from '../../i18n/index.js';

Locale.register('zh-TW', 'qrCode', {
    ariaLabel: 'QR Code：{value}',
    ariaEmpty: 'QR Code（沒有內容）',
    errorTooLong: '內容太長，無法產生 QR Code',
    errorInvalidOption: 'QR Code 的設定不正確',
    errorUnknown: '無法產生 QR Code'
});

Locale.register('en', 'qrCode', {
    ariaLabel: 'QR code: {value}',
    ariaEmpty: 'QR code (empty)',
    errorTooLong: 'The content is too long for a QR code',
    errorInvalidOption: 'The QR code settings are invalid',
    errorUnknown: 'Unable to generate the QR code'
});
