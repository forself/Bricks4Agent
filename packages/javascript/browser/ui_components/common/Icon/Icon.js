/**
 * Icon Component
 * CSP-safe Canvas icon(取代 Font Awesome / Material-UI icons 的 CSS class 用法)
 *
 * - 內建常用 Material Design 24x24 path(Apache-2.0),以 Path2D 繪製
 * - 顏色走 currentColor / --cl-* token,不注入 <style>,無外部字型依賴
 * - 旋轉動畫用 Web Animations API(CSP-safe)
 *
 * @example
 * const icon = new Icon({ name: 'add-circle', size: 20, color: 'var(--cl-primary)' });
 * icon.mount(container);
 *
 * // 可點擊(自帶 role=button 與鍵盤支援)
 * new Icon({ name: 'delete', onClick: () => remove(), title: '刪除' }).mount(cell);
 *
 * // 擴充自訂圖示
 * Icon.register('custom-badge', 'M12 2L2 7v10l10 5 10-5V7z');
 */

import { FALLBACK_PAINT, onThemeChange, resolveTokens } from '../../utils/theme-bus.js';

const PATHS = {
    'add': 'M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z',
    'add-circle': 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm5 11h-4v4h-2v-4H7v-2h4V7h2v4h4v2z',
    'remove-circle': 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm5 11H7v-2h10v2z',
    'edit': 'M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04c.39-.39.39-1.02 0-1.41l-2.34-2.34c-.39-.39-1.02-.39-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z',
    'delete': 'M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z',
    'search': 'M15.5 14h-.79l-.28-.27C15.41 12.59 16 11.11 16 9.5 16 5.91 13.09 3 9.5 3S3 5.91 3 9.5 5.91 16 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z',
    'save': 'M17 3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V7l-4-4zm-5 16c-1.66 0-3-1.34-3-3s1.34-3 3-3 3 1.34 3 3-1.34 3-3 3zm3-10H5V5h10v4z',
    'close': 'M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z',
    'check': 'M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z',
    'done-all': 'M18 7l-1.41-1.41-6.34 6.34 1.41 1.41L18 7zm4.24-1.41L11.66 16.17 7.48 12l-1.41 1.41L11.66 19l12-12-1.42-1.41zM.41 13.41L6 19l1.41-1.41L1.83 12 .41 13.41z',
    'warning': 'M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z',
    'error': 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z',
    'info': 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z',
    'download': 'M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z',
    'cloud-download': 'M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96zM17 13l-5 5-5-5h3V9h4v4h3z',
    'upload': 'M9 16h6v-6h4l-7-7-7 7h4zm-4 2h14v2H5z',
    'cloud-upload': 'M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96zM14 13v4h-4v-4H7l5-5 5 5h-3z',
    'print': 'M19 8H5c-1.66 0-3 1.34-3 3v6h4v4h12v-4h4v-6c0-1.66-1.34-3-3-3zm-3 11H8v-5h8v5zm3-7c-.55 0-1-.45-1-1s.45-1 1-1 1 .45 1 1-.45 1-1 1zm-1-9H6v4h12V3z',
    'refresh': 'M17.65 6.35C16.2 4.9 14.21 4 12 4c-4.42 0-7.99 3.58-7.99 8s3.57 8 7.99 8c3.73 0 6.84-2.55 7.73-6h-2.08c-.82 2.33-3.04 4-5.65 4-3.31 0-6-2.69-6-6s2.69-6 6-6c1.66 0 3.14.69 4.22 1.78L13 11h7V4l-2.35 2.35z',
    'arrow-back': 'M20 11H7.83l5.59-5.59L12 4l-8 8 8 8 1.41-1.41L7.83 13H20v-2z',
    'arrow-forward': 'M12 4l-1.41 1.41L16.17 11H4v2h12.17l-5.58 5.59L12 20l8-8z',
    'chevron-left': 'M15.41 7.41L14 6l-6 6 6 6 1.41-1.41L10.83 12z',
    'chevron-right': 'M10 6L8.59 7.41 13.17 12l-4.58 4.59L10 18l6-6z',
    'chevron-up': 'M12 8l-6 6 1.41 1.41L12 10.83l4.59 4.58L18 14z',
    'chevron-down': 'M16.59 8.59L12 13.17 7.41 8.59 6 10l6 6 6-6z',
    'calendar': 'M17 12h-5v5h5v-5zM16 1v2H8V1H6v2H5c-1.11 0-1.99.9-1.99 2L3 19c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2h-1V1h-2zm3 18H5V8h14v11z',
    'clock': 'M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20c-4.42 0-8-3.58-8-8s3.58-8 8-8 8 3.58 8 8-3.58 8-8 8zm.5-13H11v6l5.25 3.15.75-1.23-4.5-2.67z',
    'person': 'M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z',
    'account-circle': 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 3c1.66 0 3 1.34 3 3s-1.34 3-3 3-3-1.34-3-3 1.34-3 3-3zm0 14.2c-2.5 0-4.71-1.28-6-3.22.03-1.99 4-3.08 6-3.08 1.99 0 5.97 1.09 6 3.08-1.29 1.94-3.5 3.22-6 3.22z',
    'people': 'M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5c-1.66 0-3 1.34-3 3s1.34 3 3 3zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5C6.34 5 5 6.34 5 8s1.34 3 3 3zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5c0-2.33-4.67-3.5-7-3.5zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5z',
    'file': 'M6 2c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6H6zm7 7V3.5L18.5 9H13z',
    'folder': 'M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z',
    'attachment': 'M16.5 6v11.5c0 2.21-1.79 4-4 4s-4-1.79-4-4V5c0-1.38 1.12-2.5 2.5-2.5s2.5 1.12 2.5 2.5v10.5c0 .55-.45 1-1 1s-1-.45-1-1V6H10v9.5c0 1.38 1.12 2.5 2.5 2.5s2.5-1.12 2.5-2.5V5c0-2.21-1.79-4-4-4S7 2.79 7 5v12.5c0 3.04 2.46 5.5 5.5 5.5s5.5-2.46 5.5-5.5V6h-1.5z',
    'add-comment': 'M22 4c0-1.1-.9-2-2-2H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h14l4 4V4zm-5 7h-4v4h-2v-4H7V9h4V5h2v4h4v2z',
    'notes': 'M3 18h12v-2H3v2zM3 6v2h18V6H3zm0 7h18v-2H3v2z',
    'queue': 'M4 6H2v14c0 1.1.9 2 2 2h14v-2H4V6zm16-4H8c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-1 9h-4v4h-2v-4H9V9h4V5h2v4h4v2z',
    'visibility': 'M12 4.5C7 4.5 2.73 7.61 1 12c1.73 4.39 6 7.5 11 7.5s9.27-3.11 11-7.5c-1.73-4.39-6-7.5-11-7.5zM12 17c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5zm0-8c-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3-1.34-3-3-3z',
    'lock': 'M18 8h-1V6c0-2.76-2.24-5-5-5S7 3.24 7 6v2H6c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V10c0-1.1-.9-2-2-2zm-6 9c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2zm3.1-9H8.9V6c0-1.71 1.39-3.1 3.1-3.1 1.71 0 3.1 1.39 3.1 3.1v2z',
    'home': 'M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8z',
    'menu': 'M3 18h18v-2H3v2zm0-5h18v-2H3v2zm0-7v2h18V6H3z',
    'more-vert': 'M12 8c1.1 0 2-.9 2-2s-.9-2-2-2-2 .9-2 2 .9 2 2 2zm0 2c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm0 6c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z',
    'filter': 'M10 18h4v-2h-4v2zM3 6v2h18V6H3zm3 7h12v-2H6v2z',
    'sort': 'M3 18h6v-2H3v2zM3 6v2h18V6H3zm0 7h12v-2H3v2z',
    'star': 'M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z',
    'phone': 'M6.62 10.79c1.44 2.83 3.76 5.14 6.59 6.59l2.2-2.2c.27-.27.67-.36 1.02-.24 1.12.37 2.33.57 3.57.57.55 0 1 .45 1 1V20c0 .55-.45 1-1 1-9.39 0-17-7.61-17-17 0-.55.45-1 1-1h3.5c.55 0 1 .45 1 1 0 1.25.2 2.45.57 3.57.11.35.03.74-.25 1.02l-2.2 2.2z',
    'chat': 'M20 2H4c-1.1 0-2 .9-2 2v18l4-4h14c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zM6 9h12v2H6V9zm8 5H6v-2h8v2zm4-6H6V6h12v2z',
    'account-balance': 'M4 10v7h3v-7H4zm6 0v7h3v-7h-3zM2 22h19v-3H2v3zm14-12v7h3v-7h-3zm-4.5-9L2 6v2h19V6L11.5 1z',
    'home-work': 'M12 7V3H2v18h20V7H12zM6 19H4v-2h2v2zm0-4H4v-2h2v2zm0-4H4V9h2v2zm0-4H4V5h2v2zm14 12h-8V9h8v10zm-2-8h-4v2h4v-2zm0 4h-4v2h4v-2z',
    'directions-car': 'M18.92 6.01A1.5 1.5 0 0017.5 5h-11c-.66 0-1.21.42-1.42 1.01L3 12v8c0 .55.45 1 1 1h1c.55 0 1-.45 1-1v-1h12v1c0 .55.45 1 1 1h1c.55 0 1-.45 1-1v-8l-2.08-5.99zM6.5 16A1.5 1.5 0 116.5 13a1.5 1.5 0 010 3zm11 0a1.5 1.5 0 111.5-1.5 1.5 1.5 0 01-1.5 1.5zM5 11l1.5-4.5h11L19 11H5z',
    'mail': 'M20 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm0 4l-8 5-8-5V6l8 5 8-5v2z',
    'place': 'M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7zm0 9.5c-1.38 0-2.5-1.12-2.5-2.5s1.12-2.5 2.5-2.5 2.5 1.12 2.5 2.5-1.12 2.5-2.5 2.5z',
    'bar-chart': 'M5 9.2h3V19H5zM10.6 5h2.8v14h-2.8zm5.6 8H19v6h-2.8z',
    'settings': 'M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z',
    'logout': 'M17 7l-1.41 1.41L18.17 11H8v2h10.17l-2.58 2.58L17 17l5-5zM4 5h8V3H4c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h8v-2H4V5z',
    'help': 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 17h-2v-2h2v2zm2.07-7.75l-.9.92C13.45 12.9 13 13.5 13 15h-2v-.5c0-1.1.45-2.1 1.17-2.83l1.24-1.26c.37-.36.59-.86.59-1.41 0-1.1-.9-2-2-2s-2 .9-2 2H8c0-2.21 1.79-4 4-4s4 1.79 4 4c0 .88-.36 1.68-.93 2.25z',
    'flag': 'M14.4 6L14 4H5v17h2v-7h5.6l.4 2h7V6z',
    'domain': 'M12 7V3H2v18h20V7H12zM6 19H4v-2h2v2zm0-4H4v-2h2v2zm0-4H4V9h2v2zm0-4H4V5h2v2zm4 12H8v-2h2v2zm0-4H8v-2h2v2zm0-4H8V9h2v2zm0-4H8V5h2v2zm10 12h-8v-2h2v-2h-2v-2h2v-2h-2V9h8v10zm-2-8h-2v2h2v-2zm0 4h-2v2h2v-2z',
    'image': 'M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z',
    'camera': 'M12 15.2c1.77 0 3.2-1.43 3.2-3.2s-1.43-3.2-3.2-3.2-3.2 1.43-3.2 3.2 1.43 3.2 3.2 3.2zM9 2L7.17 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2h-3.17L15 2H9zm3 15c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5z',
    'zoom-in': 'M15.5 14h-.79l-.28-.27A6.471 6.471 0 0016 9.5 6.5 6.5 0 109.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0A4.5 4.5 0 119.5 5a4.5 4.5 0 010 9zM10 7H9v2H7v1h2v2h1v-2h2V9h-2V7z',
    'zoom-out': 'M15.5 14h-.79l-.28-.27A6.471 6.471 0 0016 9.5 6.5 6.5 0 109.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0A4.5 4.5 0 119.5 5a4.5 4.5 0 010 9zM7 9h5v1H7z',
    'fullscreen': 'M7 14H5v5h5v-2H7v-3zm-2-4h2V7h3V5H5v5zm12 7h-3v2h5v-5h-2v3zM14 5v2h3v3h2V5h-5z',
    'check-box-outline-blank': 'M19 5v14H5V5h14m0-2H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2z',
    'straighten': 'M3 17v4h4v-2H5v-2h2v-2H5v-2h2v-2H5V9h2V7H3v10zm18-10h-8v4h2V9h2v2h2V9h2V7z',
    'format-bold': 'M15.6 10.79c.97-.67 1.65-1.77 1.65-2.79 0-2.26-1.75-4-4-4H7v14h7.04c2.09 0 3.71-1.7 3.71-3.79 0-1.52-.86-2.82-2.15-3.42zM10 6.5h3c.83 0 1.5.67 1.5 1.5S13.83 9.5 13 9.5h-3v-3zm3.5 9H10v-3h3.5c.83 0 1.5.67 1.5 1.5s-.67 1.5-1.5 1.5z',
    'format-italic': 'M10 4v3h2.21l-3.42 8H6v3h8v-3h-2.21l3.42-8H18V4z',
    'format-underlined': 'M12 17c3.31 0 6-2.69 6-6V3h-2.5v8c0 1.93-1.57 3.5-3.5 3.5S8.5 12.93 8.5 11V3H6v8c0 3.31 2.69 6 6 6zM5 19v2h14v-2H5z',
    'strikethrough-s': 'M10 19h4v-3h-4v3zM5 4v3h5v3h4V7h5V4H5zm-2 8v2h18v-2H3z',
    'subscript': 'M5 4h3.5l3.5 5 3.5-5H19l-5.25 7.5L19 19h-3.5L12 14l-3.5 5H5l5.25-7.5L5 4zm15 13h4v1.5h-2.5V20H24v1.5h-4V17z',
    'superscript': 'M5 5h3.5l3.5 5 3.5-5H19l-5.25 7.5L19 20h-3.5L12 15l-3.5 5H5l5.25-7.5L5 5zm15-3h4v1.5h-2.5V5H24v1.5h-4V2z',
    'format-size': 'M9 4v3h5v12h3V7h5V4H9zM3 9v3h3v7h3v-7h3V9H3z',
    'format-paragraph': 'M9 4v16h2v-6h2v6h2V6h2V4H9zm2 2h2v6h-2V6z',
    'format-quote': 'M6 17h3l2-4V7H5v6h3l-2 4zm8 0h3l2-4V7h-6v6h3l-2 4z',
    'code': 'M9.4 16.6L4.8 12l4.6-4.6L8 6l-6 6 6 6 1.4-1.4zm5.2 0l4.6-4.6-4.6-4.6L16 6l6 6-6 6-1.4-1.4z',
    'format-align-left': 'M3 18h14v-2H3v2zm0-4h18v-2H3v2zm0-4h14V8H3v2zm0-6v2h18V4H3z',
    'format-align-center': 'M7 18h10v-2H7v2zm-4-4h18v-2H3v2zm4-4h10V8H7v2zM3 4v2h18V4H3z',
    'format-align-right': 'M7 18h14v-2H7v2zm-4-4h18v-2H3v2zm4-4h14V8H7v2zM3 4v2h18V4H3z',
    'format-align-justify': 'M3 18h18v-2H3v2zm0-4h18v-2H3v2zm0-4h18V8H3v2zm0-6v2h18V4H3z',
    'format-list-bulleted': 'M4 10.5c.83 0 1.5-.67 1.5-1.5S4.83 7.5 4 7.5 2.5 8.17 2.5 9s.67 1.5 1.5 1.5zm0-6C4.83 4.5 5.5 3.83 5.5 3S4.83 1.5 4 1.5 2.5 2.17 2.5 3 3.17 4.5 4 4.5zm0 12c-.83 0-1.5.67-1.5 1.5s.67 1.5 1.5 1.5 1.5-.67 1.5-1.5-.67-1.5-1.5-1.5zM7 19h15v-2H7v2zm0-9h15V8H7v2zm0-8v2h15V2H7z',
    'format-list-numbered': 'M2 17h2v.5H3v1h1v.5H2v1.5h3.5V16H2v1zm1.5-12H5V1H2v1.5h1.5V5zM2 8.5h2.1L2 11v1h3.5v-1.5H3.4L5.5 8V7H2v1.5zM7 19h15v-2H7v2zm0-9h15V8H7v2zm0-8v2h15V2H7z',
    'table-chart': 'M10 10H5v4h5v-4zm0 6H5v4h5v-4zm0-12H5v4h5V4zm2 0v4h7V4h-7zm0 10h7v-4h-7v4zm0 6h7v-4h-7v4zM3 2h18v20H3V2z',
    'horizontal-rule': 'M4 11h16v2H4z',
    'insert-page-break': 'M4 18h16v2H4v-2zm0-5h5v2H4v-2zm7 0h2v2h-2v-2zm4 0h5v2h-5v-2zM4 4h16v7H4V4z',
    'title': 'M5 4v3h5.5v12h3V7H19V4z',
    'touch-app': 'M9 11.24V7.5a2.5 2.5 0 115 0v3.74a4.5 4.5 0 10-5 0zm9.84 1.63l-4.54-2.26c-.26-.13-.55-.2-.85-.2H13V7.5a1.5 1.5 0 00-3 0v8.55l-2.47-.52a1.25 1.25 0 00-1.17.38l-.66.67 4.57 4.57c.56.55 1.31.85 2.09.85h4.91c1.22 0 2.3-.74 2.75-1.88l1.37-3.43a3 3 0 00-1.55-3.82z',
    'content-copy': 'M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h11c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm0 16H8V7h11v14z',
    'content-paste': 'M19 4h-4.18C14.4 2.84 13.3 2 12 2s-2.4.84-2.82 2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm-7 0c.55 0 1 .45 1 1s-.45 1-1 1-1-.45-1-1 .45-1 1-1zm7 16H5V6h2v3h10V6h2v14z',
    'content-cut': 'M9.64 7.64c.23-.5.36-1.06.36-1.64a4 4 0 10-4 4c.58 0 1.14-.13 1.64-.36L10 12l-2.36 2.36A4 4 0 106 22a4 4 0 004-4c0-.58-.13-1.14-.36-1.64L12 14l7 7h3v-1L9.64 7.64zM6 8a2 2 0 110-4 2 2 0 010 4zm0 12a2 2 0 110-4 2 2 0 010 4zm6-7a1 1 0 110-2 1 1 0 010 2zm7-10L13 9l2 2 7-7V3h-3z',
    'toc': 'M3 9h2V7H3v2zm0 4h2v-2H3v2zm0 4h2v-2H3v2zm4 0h14v-2H7v2zm0-4h14v-2H7v2zm0-6v2h14V7H7z',
    'layers': 'M12 2L1 9l11 7 9-5.73V17h2V9L12 2zm0 11.65L5.74 9.67 12 5.69l6.26 3.98L12 13.65zM3 15l9 5.73L18.74 16.44l-1.9-1.21L12 18.31l-7.1-4.52L3 15z',
    'vertical-align-top': 'M8 11h3v10h2V11h3l-4-4-4 4zM5 3v2h14V3H5z',
    'vertical-align-bottom': 'M16 13h-3V3h-2v10H8l4 4 4-4zM5 19v2h14v-2H5z',
    'format-clear': 'M3.27 5L2 6.27l6.97 6.97L6.5 19h3l1.58-3.68L16.73 21 18 19.73 3.27 5zM6 4v2h4.85l1.55 1.55L15.11 6H18V4H6z',
    'select-all': 'M3 5h2V3H3v2zm0 8h2v-2H3v2zm4 8h2v-2H7v2zM3 9h2V7H3v2zm10-6h-2v2h2V3zm6 0v2h2V3h-2zM5 21v-2H3v2h2zm-2-4h2v-2H3v2zM9 3H7v2h2V3zm2 18h2v-2h-2v2zm8-8h2v-2h-2v2zm0 8h2v-2h-2v2zm0-12h2V7h-2v2zm0 8h2v-2h-2v2zm-4 4h2v-2h-2v2zm0-16h2V3h-2v2zM7 17h10V7H7v10zm2-8h6v6H9V9z',
    'deselect-all': 'M4.27 3L3 4.27 5.73 7H3v2h2V7.27l2 2V17h7.73l2 2H15v2h2v-1.27L19.27 22 20.54 20.73 4.27 3zM9 15v-3.73L12.73 15H9zM19 17h2v-2h-2v2zm0-4h2v-2h-2v2zm0-4h2V7h-2v2zm0-4v2h2V5h-2zM7 3H5v2h2V3zm4 0H9v2h2V3zm4 0h-2v2h2V3zm-4 18h2v-2h-2v2zm-4 0h2v-2H7v2zm-4 0h2v-2H3v2zm0-4h2v-2H3v2zm0-4h2v-2H3v2z',
    'add-row': 'M3 3h18v14H3V3zm2 2v4h14V5H5zm0 6v4h14v-4H5zm8 8h3v-3h2v3h3v2h-3v3h-2v-3h-3v-2z',
    'sort-none': 'M3 18h6v-2H3v2zM3 6v2h18V6H3zm0 7h12v-2H3v2z',
    'sort-desc': 'M3 18h18v-2H3v2zm0-5h12v-2H3v2zm0-5h6V6H3v2z',
    'sort-asc': 'M3 18h6v-2H3v2zm0-5h12v-2H3v2zm0-5h18V6H3v2z',
    'triangle-right': 'M8 5v14l11-7z',
    'caret-right': 'M9 18l6-6-6-6v12z',
    'send': 'M2.01 21L23 12 2.01 3 2 10l15 2-15 2z',
    'play-arrow': 'M8 5v14l11-7z',
    'music-note': 'M12 3v10.55A4 4 0 1014 17V7h4V3h-6z'
};

const SIZE_ALIAS = {
    sm: 16,
    md: 20,
    lg: 24,
    small: 16,
    medium: 20,
    large: 24
};

function normalizeSize(size) {
    if (typeof size === 'number' && Number.isFinite(size) && size > 0) return size;
    if (typeof size === 'string') {
        if (SIZE_ALIAS[size]) return SIZE_ALIAS[size];
        const parsed = Number.parseFloat(size);
        if (Number.isFinite(parsed) && parsed > 0) return parsed;
    }
    return 20;
}

const CONNECTION_PROBE_TAG = 'b4a-icon-connection';

function createConnectionProbe(icon) {
    const registry = globalThis.customElements;
    const ElementBase = globalThis.HTMLElement;
    if (!registry || !ElementBase) return null;
    try {
        if (!registry.get(CONNECTION_PROBE_TAG)) {
            registry.define(CONNECTION_PROBE_TAG, class extends ElementBase {
                connectedCallback() {
                    this._iconInstance?._handleConnected();
                }
            });
        }
        const probe = document.createElement(CONNECTION_PROBE_TAG);
        probe.style.display = 'none';
        probe.setAttribute('aria-hidden', 'true');
        probe._iconInstance = icon;
        return probe;
    } catch {
        return null;
    }
}

export class Icon {
    /**
     * @param {Object} options
     * @param {string} options.name - 圖示名稱(見 Icon.names())
     * @param {number} options.size - 邊長 px(預設 20)
     * @param {string} options.color - CSS 顏色(預設 currentColor,可用 var(--cl-*))
     * @param {string} options.title - 無障礙標題/tooltip
     * @param {boolean} options.spin - 是否旋轉(loading/refresh 用)
     * @param {Function} options.onClick - 點擊回呼(有給則自帶 button 語意)
     */
    constructor(options = {}) {
        this.options = {
            name: 'info',
            size: 20,
            color: 'currentColor',
            title: '',
            spin: false,
            onClick: null,
            pathData: null,
            glyph: null,
            ...options
        };
        // 相容新 sm/md/lg 與 0626 線的 small/medium/large。
        this.options.size = normalizeSize(this.options.size);
        if (this.options.label && !this.options.title) this.options.title = this.options.label;
        this._animation = null;
        this._offTheme = null;
        this._connectionObserver = null;
        this._clickHandler = null;
        this._keyHandler = null;
        this.element = this._create();
        this._applySpin();
    }

    static register(name, pathData) {
        PATHS[name] = pathData;
    }

    static names() {
        return Object.keys(PATHS);
    }

    static has(name) {
        return Object.prototype.hasOwnProperty.call(PATHS, name);
    }

    _create() {
        const { name, size, color, title, onClick } = this.options;

        const wrapper = document.createElement('span');
        wrapper.className = `cl-icon cl-icon--${name}`;
        wrapper.style.cssText = `
            display: inline-flex;
            align-items: center;
            justify-content: center;
            line-height: 1;
            color: ${color};
            vertical-align: middle;
        `;

        if (title) wrapper.title = title;

        if (typeof onClick === 'function') {
            wrapper.style.cursor = 'pointer';
            wrapper.setAttribute('role', 'button');
            wrapper.setAttribute('tabindex', '0');
            if (title) wrapper.setAttribute('aria-label', title);
            this._clickHandler = (e) => onClick(e);
            this._keyHandler = (e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    onClick(e);
                }
            };
            wrapper.addEventListener('click', this._clickHandler);
            wrapper.addEventListener('keydown', this._keyHandler);
        } else if (!title) {
            wrapper.setAttribute('aria-hidden', 'true');
        }

        const canvas = document.createElement('canvas');
        canvas.className = 'cl-icon__canvas';
        canvas.setAttribute('aria-hidden', 'true');
        canvas.style.cssText = 'display:block;flex:none;';
        wrapper.appendChild(canvas);

        this._connectionProbe = createConnectionProbe(this);
        if (this._connectionProbe) wrapper.appendChild(this._connectionProbe);

        this._canvas = canvas;
        this._warnUnknown(name);
        this._resizeCanvas(size);
        return wrapper;
    }

    _warnUnknown(name) {
        if (!this.options.pathData && !PATHS[name]) console.warn(`[Icon] Unknown icon name "${name}", fallback to "help".`);
    }

    _resizeCanvas(size) {
        if (!this._canvas) return;
        const dpr = Math.max(1, globalThis.devicePixelRatio || 1);
        this._canvas.width = Math.max(1, Math.round(size * dpr));
        this._canvas.height = Math.max(1, Math.round(size * dpr));
        this._canvas.style.width = `${size}px`;
        this._canvas.style.height = `${size}px`;
    }

    _resolveColor() {
        const requested = String(this.options.color || 'currentColor').trim();
        const tokenMatch = requested.match(/^var\((--[\w-]+)/);
        if (tokenMatch && typeof getComputedStyle === 'function') {
            const resolved = resolveTokens([tokenMatch[1]], this.element)[tokenMatch[1]];
            if (resolved) return resolved;
        }
        if (typeof getComputedStyle === 'function') {
            return getComputedStyle(this.element).color || FALLBACK_PAINT;
        }
        return FALLBACK_PAINT;
    }

    _draw() {
        const canvas = this._canvas;
        if (!canvas || typeof canvas.getContext !== 'function' || typeof Path2D === 'undefined') return;
        const ctx = canvas.getContext('2d');
        if (!ctx) return;
        const dpr = Math.max(1, globalThis.devicePixelRatio || 1);
        const size = this.options.size;
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        ctx.setTransform(dpr * size / 24, 0, 0, dpr * size / 24, 0, 0);
        ctx.fillStyle = this._resolveColor();
        if (this.options.glyph) {
            const glyph = String(this.options.glyph).slice(0, 4);
            const glyphScale = glyph.length > 2 ? 0.42 : 0.58;
            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
            ctx.font = `700 ${Math.max(8, Math.round(size * glyphScale))}px system-ui, sans-serif`;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillText(glyph, size / 2, size / 2);
            return;
        }
        ctx.fill(new Path2D(this.options.pathData || PATHS[this.options.name] || PATHS.help));
    }

    _applySpin() {
        if (this.options.spin && this._canvas && typeof this._canvas.animate === 'function') {
            this._animation = this._canvas.animate(
                [{ transform: 'rotate(0deg)' }, { transform: 'rotate(360deg)' }],
                { duration: 1000, iterations: Infinity }
            );
        } else if (this._animation) {
            this._animation.cancel();
            this._animation = null;
        }
    }

    setName(name) {
        this.options.name = name;
        this.element.className = `cl-icon cl-icon--${name}`;
        this._warnUnknown(name);
        this._draw();
    }

    setColor(color) {
        this.options.color = color;
        this.element.style.color = color;
        this._draw();
    }

    setSize(size) {
        this.options.size = normalizeSize(size);
        this._resizeCanvas(this.options.size);
        this._draw();
    }

    setSpin(spin) {
        this.options.spin = !!spin;
        this._applySpin();
    }

    redraw() {
        this._draw();
        return this;
    }

    _redrawWhenConnected() {
        if (this.element?.isConnected) {
            this._handleConnected();
            return;
        }
        // Native custom-element connectedCallback fires synchronously when an
        // outer component enters the document. MutationObserver is the fallback.
        if (this._connectionProbe) return;
        if (this._connectionObserver || typeof MutationObserver === 'undefined') return;
        const root = this.element?.ownerDocument?.documentElement;
        if (!root) return;
        this._connectionObserver = new MutationObserver(() => {
            if (!this.element?.isConnected) return;
            this._handleConnected();
        });
        this._connectionObserver.observe(root, { childList: true, subtree: true });
    }

    _handleConnected() {
        this._connectionObserver?.disconnect();
        this._connectionObserver = null;
        this._draw();
    }

    show() {
        this.element.style.display = 'inline-flex';
    }

    hide() {
        this.element.style.display = 'none';
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) {
            target.appendChild(this.element);
            this._resizeCanvas(this.options.size);
            this._redrawWhenConnected();
            if (!this._offTheme) this._offTheme = onThemeChange(() => this._draw());
        }
        return this;
    }

    destroy() {
        if (this._animation) this._animation.cancel();
        this._connectionObserver?.disconnect();
        if (this._connectionProbe) this._connectionProbe._iconInstance = null;
        this._offTheme?.();
        if (this._clickHandler) this.element?.removeEventListener('click', this._clickHandler);
        if (this._keyHandler) this.element?.removeEventListener('keydown', this._keyHandler);
        if (this.element?.parentNode) this.element.remove();
        this._animation = null;
        this._connectionObserver = null;
        this._connectionProbe = null;
        this._offTheme = null;
    }
}

export default Icon;
