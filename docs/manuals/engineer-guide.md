# Bricks4Agent 工程師使用手冊

> 本手冊面向初階工程師，涵蓋 Bricks4Agent 的安裝、使用、主題系統、頁面生成器與後端整合等完整指引。

---

## 目錄

1. [快速開始](#1-快速開始)
2. [元件總覽](#2-元件總覽)
3. [主題與樣式系統](#3-主題與樣式系統)
4. [表單元件](#4-表單元件)
5. [通用元件](#5-通用元件)
6. [佈局元件](#6-佈局元件)
7. [進階輸入元件](#7-進階輸入元件)
8. [社群元件](#8-社群元件)
9. [視覺化元件](#9-視覺化元件)
10. [行為模組與通用函式](#10-行為模組與通用函式)
11. [頁面生成器](#11-頁面生成器)
12. [SPA 生成器](#12-spa-生成器)
13. [後端套件 C#](#13-後端套件-c)
14. [安全性指引](#14-安全性指引)

---

## 1. 快速開始

### 1.1 環境需求

- Node.js 18+
- .NET 8 SDK
- Git

### 1.2 安裝與啟動

```bash
# 1. 複製專案
git clone <repo-url> Bricks4Agent
cd Bricks4Agent

# 2. 安裝開發工具依賴（Playwright/Puppeteer，用於截圖與 UI 測試）
npm install

# 3. 啟動 SPA 生成器 (Web UI)
npm run serve
# 瀏覽器開啟 http://localhost:3080
```

### 1.3 專案結構

```
Bricks4Agent/
├── packages/                          # 可重用套件
│   ├── javascript/
│   │   └── browser/
│   │       ├── ui_components/         # Bricks4Agent UI 元件庫（核心）
│   │       │   ├── form/              # 表單元件 (12 個)
│   │       │   ├── common/            # 通用元件 (23 個)
│   │       │   ├── layout/            # 佈局元件 (10 個)
│   │       │   ├── input/             # 進階輸入元件 (10 個)
│   │       │   ├── viz/               # 視覺化元件 (18 個可直接使用元件 + BaseChart)
│   │       │   ├── social/            # 社群元件 (5 個)
│   │       │   ├── editor/            # 編輯器元件 (1 個)
│   │       │   ├── data/              # 資料展示元件 (1 個)
│   │       │   ├── binding/           # 綁定模組 (2 個)
│   │       │   └── utils/             # 工具與服務 (4 個)
│   │       └── page-generator/        # 頁面生成器
│   └── csharp/
│       ├── api/                       # API 與控制器模組
│       ├── database/                  # 資料庫/ORM 模組
│       ├── security/                  # 安全與認證模組
│       ├── logging/                   # 記錄模組
│       └── utils/                     # 後端工具模組
├── templates/
│   └── spa/                           # SPA 專案範本
│       ├── frontend/                  # 前端範本 (含 core/)
│       ├── backend/                   # .NET 8 後端範本
│       └── scripts/                   # 範本 CLI（spa-cli.js）
└── tools/
    ├── spa-generator/                 # SPA 生成器 Web 介面
    └── page-gen.js                    # PageDefinition CLI
```

### 1.4 建立第一個專案

SPA 相關工具分成兩種：

- `tools/spa-generator/`：Web UI
- `templates/spa/scripts/`：範本 CLI

```bash
# 啟動 Web UI
npm run serve

# 使用範本 CLI 建立新專案（互動式）
node templates/spa/scripts/spa-cli.js new

# 使用範本 CLI 建立新專案（非互動）
node templates/spa/scripts/spa-cli.js new --name my-app --output ./projects

# 生成完整功能（頁面 + API）
node templates/spa/scripts/spa-cli.js feature User --fields "Name:string,Email:string"
```


---

## 2. 元件總覽

### 2.1 元件分類表

| 分類 | 目錄 | 數量 | 說明 |
|------|------|------|------|
| 表單元件 | `form/` | 12 | 文字、數字、日期、下拉選單等表單輸入 |
| 通用元件 | `common/` | 23 | 按鈕、徽章、標籤、提示框、進度條、分隔線、對話框、通知、分頁等通用 UI |
| 佈局元件 | `layout/` | 10 | 面板、表格、側選單、頁籤等版面配置 |
| 進階輸入 | `input/` | 10 | 地址、電話、組織等複合輸入元件 |
| 視覺化 | `viz/` | 18 | 圖表、地圖、畫板等資料視覺化（不含 `BaseChart` 基底類別） |
| 社群 | `social/` | 5 | 頭像、動態卡片、連線卡片、統計卡片、時間軸 |
| 編輯器 | `editor/` | 1 | 富文字編輯器 |
| 資料元件 | `data/` | 1 | 區域地圖與地理視覺化 |
| 綁定模組 | `binding/` | 2 | 元件工廠與綁定器 |
| 工具/服務 | `utils/` | 4 | 安全性、壓縮、地理定位、天氣 |

> 匯入路徑說明：以下程式碼片段為了閱讀性使用簡化路徑。若直接在此 repo 根目錄驗證，請將 `./ui_components/...` 視為 `./packages/javascript/browser/ui_components/...`，將 `./page-generator/...` 視為 `./packages/javascript/browser/page-generator/...`。

### 2.2 統一 API 慣例

所有元件遵循一致的 API 模式，降低學習成本：

```javascript
import { TextInput } from './ui_components/form/TextInput/TextInput.js';

// 1. 建構元件
const input = new TextInput({
  label: '姓名',
  placeholder: '請輸入姓名',
  required: true
});

// 2. 掛載到 DOM
input.mount(document.getElementById('container'));

// 3. 取值 / 設值
const value = input.getValue();
input.setValue('王小明');

// 4. 清空值（表單元件）
input.clear();

// 5. 銷毀清理
input.destroy();
```

**API 方法速查表：**

| 方法 | 說明 | 適用範圍 |
|------|------|----------|
| `new Component(options)` | 建構元件實例 | 所有元件 |
| `.mount(container)` | 掛載到指定 DOM 容器 | 所有元件 |
| `.getValue()` | 取得目前值 | 表單/輸入元件 |
| `.setValue(value)` | 設定值 | 表單/輸入元件 |
| `.clear()` | 清空值 | 表單/輸入元件 |
| `.destroy()` | 銷毀元件、移除事件監聽 | 所有元件 |

---

## 3. 主題與樣式系統

### 3.1 theme.css 概述

Bricks4Agent 使用 CSS 變數（Custom Properties）實作主題系統，所有變數使用 `--cl-` 前綴。

### 3.2 變數分類

```css
:root {
  /* 品牌色 */
  --cl-primary: #2196F3;
  --cl-primary-dark: #1976D2;
  --cl-primary-light: #e3f2fd;
  --cl-primary-rgb: 33, 150, 243;      /* 用於 rgba() */

  /* 語意色 */
  --cl-success: #4CAF50;
  --cl-success-light: #e8f5e9;
  --cl-success-rgb: 76, 175, 80;
  --cl-warning: #FF9800;
  --cl-warning-light: #fff3e0;
  --cl-warning-rgb: 255, 152, 0;
  --cl-danger: #F44336;
  --cl-danger-light: #fdecea;
  --cl-danger-rgb: 244, 67, 54;
  --cl-info: #2196F3;
  --cl-info-light: #e3f2fd;

  /* 文字 */
  --cl-text: #333333;
  --cl-text-secondary: #666666;
  --cl-text-muted: #888888;
  --cl-text-placeholder: #999999;
  --cl-text-light: #aaaaaa;
  --cl-text-inverse: #ffffff;
  --cl-text-dark: #000000;

  /* 背景 */
  --cl-bg: #ffffff;
  --cl-bg-secondary: #f5f5f5;
  --cl-bg-tertiary: #f8f9fa;
  --cl-bg-hover: #f0f2f5;
  --cl-bg-active: #e3f2fd;
  --cl-bg-disabled: #f9f9f9;
  --cl-bg-overlay: rgba(0, 0, 0, 0.5);
  --cl-bg-dark: #2b2b2b;

  /* 邊框 */
  --cl-border: #dddddd;
  --cl-border-light: #eeeeee;
  --cl-border-dark: #cccccc;
  --cl-border-focus: var(--cl-primary);

  /* 陰影 */
  --cl-shadow-sm: 0 1px 3px rgba(0,0,0,0.1);
  --cl-shadow-md: 0 4px 12px rgba(0,0,0,0.15);
  --cl-shadow-lg: 0 8px 24px rgba(0,0,0,0.2);
  --cl-shadow-xl: 0 12px 48px rgba(0,0,0,0.25);

  /* 圓角 */
  --cl-radius-sm: 4px;
  --cl-radius-md: 6px;
  --cl-radius-lg: 8px;
  --cl-radius-xl: 12px;
  --cl-radius-round: 50%;

  /* 字體 */
  --cl-font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
  --cl-font-size-xs: 11px;
  --cl-font-size-sm: 12px;
  --cl-font-size-md: 13px;
  --cl-font-size-lg: 14px;
  --cl-font-size-xl: 16px;
  --cl-font-size-2xl: 18px;
  --cl-font-size-3xl: 24px;

  /* Material 色板 — 按鈕 variant / 圖示色 */
  --cl-purple: #9C27B0;
  --cl-teal: #009688;
  --cl-pink: #E91E63;
  --cl-light-green: #8BC34A;
  --cl-amber: #FFC107;
  /* ...等 15+ 種擴充色（見 theme.css 完整定義） */

  /* 過渡動畫 */
  --cl-transition-fast: 0.15s ease;
  --cl-transition: 0.2s ease;
  --cl-transition-slow: 0.3s ease;
}
```

> 完整變數定義請參考 `packages/javascript/browser/ui_components/theme.css`，包含 140+ 個 CSS 變數。

### 3.3 自訂主題

覆寫 `:root` 變數即可自訂品牌主題：

```css
/* my-theme.css — 覆寫品牌色即可自訂主題 */
:root {
  --cl-primary: #E74C3C;
  --cl-primary-dark: #C0392B;
  --cl-radius-md: 8px;
  --cl-font-family: 'Noto Sans TC', sans-serif;
}
```

### 3.4 深色主題

theme.css 已內建 `[data-theme="dark"]` 選擇器，覆寫所有 `--cl-` 變數：

```css
[data-theme="dark"] {
  --cl-primary: #64B5F6;
  --cl-primary-dark: #42A5F5;
  --cl-primary-light: #1a2a4a;
  --cl-primary-rgb: 100, 181, 246;

  --cl-text: #e4e4e7;
  --cl-text-secondary: #a1a1aa;
  --cl-text-muted: #71717a;
  --cl-text-inverse: #1a1a2e;

  --cl-bg: #1a1a2e;
  --cl-bg-secondary: #252542;
  --cl-bg-tertiary: #2d2d4a;
  --cl-bg-hover: #2d2d4a;

  --cl-border: #3f3f5a;
  --cl-border-light: #2d2d4a;
  --cl-border-dark: #52527a;

  --cl-shadow-sm: 0 1px 3px rgba(0,0,0,0.3);
  --cl-shadow-md: 0 4px 12px rgba(0,0,0,0.4);
  /* ...等（見 theme.css 完整定義） */
}
```

切換主題的 JavaScript：

```javascript
// 切換深色主題
document.documentElement.setAttribute('data-theme', 'dark');

// 切換回淺色主題
document.documentElement.removeAttribute('data-theme');
```

### 3.5 Demo 主題切換工具

Bricks4Agent 提供 `demo-utils.js` 工具，在 Demo 頁面中快速加入主題切換按鈕：

```javascript
import { createThemeToggle } from '../../demo-utils.js';

// 在 Demo 頁面中加入深色/淺色切換按鈕
createThemeToggle();
```

### 3.6 樣式遷移工具

元件庫提供兩個自動化腳本，用於將硬編碼色碼批次替換為 CSS 變數：

```bash
# 預覽 hex 色碼替換（不寫入）
node tools/migrate-styles.js

# 執行替換
node tools/migrate-styles.js --apply

# 預覽命名色 / rgba() 替換
node tools/fix-named-colors.js

# 執行替換
node tools/fix-named-colors.js --apply
```

---

## 4. 表單元件

表單元件位於 `packages/javascript/browser/ui_components/form/` 目錄下，共 12 個元件。

### 4.1 TextInput — 文字輸入

![TextInput 元件](screenshots/after/form-TextInput.png)

```javascript
import { TextInput } from './ui_components/form/TextInput/TextInput.js';

const nameInput = new TextInput({
  label: '姓名',
  placeholder: '請輸入姓名',
  required: true,
  maxLength: 50,
  onChange: (value) => console.log('值變更:', value)
});

nameInput.mount(document.getElementById('name-field'));

// 取值
const name = nameInput.getValue();

// 設值
nameInput.setValue('王小明');
```

### 4.2 NumberInput — 數字輸入

![NumberInput 元件](screenshots/after/form-NumberInput.png)

```javascript
import { NumberInput } from './ui_components/form/NumberInput/NumberInput.js';

const ageInput = new NumberInput({
  label: '年齡',
  min: 0,
  max: 150,
  step: 1,
  required: true
});

ageInput.mount(document.getElementById('age-field'));
```

### 4.3 DatePicker — 日期選擇器

![DatePicker 元件](screenshots/after/form-DatePicker.png)

```javascript
import { DatePicker } from './ui_components/form/DatePicker/DatePicker.js';

const datePicker = new DatePicker({
  label: '生日',
  format: 'western',      // 'western' (西元) 或 'taiwan' (民國)
  min: '1900-01-01',
  max: '2026-12-31',
  onChange: (date) => console.log('選擇日期:', date)
});

datePicker.mount(document.getElementById('date-field'));
```

### 4.4 TimePicker — 時間選擇器

![TimePicker 元件](screenshots/after/form-TimePicker.png)

```javascript
import { TimePicker } from './ui_components/form/TimePicker/TimePicker.js';

const timePicker = new TimePicker({
  label: '會議時間',
  minuteStep: 15           // 分鐘間隔（1, 5, 10, 15, 30）
});

timePicker.mount(document.getElementById('time-field'));
```

### 4.5 Dropdown — 下拉選單

![Dropdown 元件](screenshots/after/form-Dropdown.png)

```javascript
import { Dropdown } from './ui_components/form/Dropdown/Dropdown.js';

const cityDropdown = new Dropdown({
  variant: 'searchable',   // 'basic' 或 'searchable'（可搜尋）
  placeholder: '請選擇縣市',
  items: [
    { value: 'TPE', label: '台北市' },
    { value: 'NTP', label: '新北市' },
    { value: 'TXG', label: '台中市' }
  ],
  clearable: true,
  onChange: (value) => console.log('選擇:', value)
});

cityDropdown.mount(document.getElementById('city-field'));
```

### 4.6 MultiSelectDropdown — 多選下拉

![MultiSelectDropdown 元件](screenshots/after/form-MultiSelectDropdown.png)

```javascript
import { MultiSelectDropdown } from './ui_components/form/MultiSelectDropdown/MultiSelectDropdown.js';

const tagSelect = new MultiSelectDropdown({
  placeholder: '請選擇標籤',
  items: [
    { value: 'js', label: 'JavaScript' },
    { value: 'css', label: 'CSS' },
    { value: 'html', label: 'HTML' }
  ],
  maxCount: 5,             // 最多可選數量
  modalTitle: '選擇標籤'
});

tagSelect.mount(document.getElementById('tag-field'));

// 取得已選值（陣列）
const selected = tagSelect.getValues(); // ['js', 'css']
```

### 4.7 Checkbox — 核取方塊

![Checkbox 元件](screenshots/after/form-Checkbox.png)

```javascript
import { Checkbox } from './ui_components/form/Checkbox/Checkbox.js';

const agreeCheckbox = new Checkbox({
  label: '我同意使用條款',
  checked: false,
  onChange: (checked) => console.log('勾選狀態:', checked)
});

agreeCheckbox.mount(document.getElementById('agree-field'));
```

### 4.8 Radio — 單選按鈕

![Radio 元件](screenshots/after/form-Radio.png)

```javascript
import { Radio } from './ui_components/form/Radio/Radio.js';

// 單個 Radio 按鈕
const radio = new Radio({
  name: 'gender',
  label: '男',
  value: 'male',
  onChange: (checked) => console.log('勾選:', checked)
});

radio.mount(document.getElementById('radio-field'));

// 建立 Radio 群組（推薦用法）
const genderGroup = Radio.createGroup({
  name: 'gender',
  items: [
    { value: 'male', label: '男' },
    { value: 'female', label: '女' },
    { value: 'other', label: '其他' }
  ],
  value: 'male',            // 預設選取值
  direction: 'horizontal',  // 'vertical' 或 'horizontal'
  onChange: (value) => console.log('選擇:', value)
});

// createGroup 回傳帶有 getValue/setValue/mount 方法的群組物件
genderGroup.mount(document.getElementById('gender-field'));

// 取得目前選取值
const selectedGender = genderGroup.getValue();

// 程式設定選取值
genderGroup.setValue('female');
```

### 4.9 ToggleSwitch — 開關切換

```javascript
import { ToggleSwitch } from './ui_components/form/ToggleSwitch/ToggleSwitch.js';

const toggle = new ToggleSwitch({
  label: '啟用通知',
  checked: true,
  onChange: (enabled) => console.log('通知:', enabled ? '開啟' : '關閉')
});

toggle.mount(document.getElementById('toggle-field'));
```

### 4.10 FormField — 表單欄位包裝器

![FormField 元件](screenshots/after/form-FormField.png)

```javascript
import { FormField } from './ui_components/form/FormField/FormField.js';

const field = new FormField({
  label: '電子郵件',
  required: true,
  hint: '我們不會分享您的電子郵件',
  component: new TextInput({ placeholder: 'user@example.com' })
});

field.mount(document.getElementById('email-field'));
```

### 4.11 SearchForm — 搜尋表單

![SearchForm 元件](screenshots/after/form-SearchForm.png)

```javascript
import { SearchForm } from './ui_components/form/SearchForm/SearchForm.js';

const searchForm = new SearchForm({
  fields: [
    { name: 'keyword', type: 'text', label: '關鍵字' },
    { name: 'category', type: 'select', label: '分類', options: [
      { value: 'all', label: '全部' },
      { value: 'news', label: '新聞' }
    ]},
    { name: 'dateRange', type: 'date', label: '日期範圍' }
  ],
  onSearch: (criteria) => console.log('搜尋條件:', criteria),
  onReset: () => console.log('已重設')
});

searchForm.mount(document.getElementById('search-area'));
```

### 4.12 BatchUploader — 批次上傳

![BatchUploader 元件](screenshots/after/form-BatchUploader.png)

```javascript
import { BatchUploader } from './ui_components/form/BatchUploader/BatchUploader.js';

const uploader = new BatchUploader({
  container: document.getElementById('upload-area'),
  apiEndpoint: '/api/files/upload',
  allowedExtensions: ['.pdf', '.jpg', '.png'],
  maxFileSize: 10 * 1024 * 1024, // 10MB
  maxFiles: 5,
  autoUpload: false,
  uploadMode: 'sequential',  // 'sequential' 或 'parallel'
  onComplete: (files) => console.log('上傳完成:', files),
  onError: (err) => console.error('上傳失敗:', err)
});
```

---

## 5. 通用元件

通用元件位於 `packages/javascript/browser/ui_components/common/` 目錄下，共 23 個元件。

### 5.1 按鈕系列

#### BasicButton — 基本按鈕

![BasicButton](screenshots/after/common-BasicButton.png)

```javascript
import { BasicButton } from './ui_components/common/BasicButton/BasicButton.js';

const btn = new BasicButton({
  text: '送出',
  variant: 'primary', // primary | secondary | outline | danger
  onClick: () => console.log('按鈕點擊')
});

btn.mount(document.getElementById('btn-container'));
```

#### ActionButton — 操作按鈕

![ActionButton](screenshots/after/common-ActionButton.png)

```javascript
import { ActionButton } from './ui_components/common/ActionButton/ActionButton.js';

const actionBtn = new ActionButton({
  text: '編輯',
  icon: 'edit',
  onClick: () => openEditor()
});

actionBtn.mount(document.getElementById('action-area'));
```

#### AuthButton — 權限按鈕

![AuthButton](screenshots/after/common-AuthButton.png)

```javascript
import { AuthButton } from './ui_components/common/AuthButton/AuthButton.js';

// 根據使用者權限自動顯示/隱藏
const deleteBtn = new AuthButton({
  text: '刪除',
  permission: 'admin.delete',
  variant: 'danger',
  onClick: () => deleteItem()
});

deleteBtn.mount(document.getElementById('auth-area'));
```

#### DownloadButton / UploadButton

![DownloadButton](screenshots/after/common-DownloadButton.png)

![UploadButton](screenshots/after/common-UploadButton.png)

```javascript
import { DownloadButton } from './ui_components/common/DownloadButton/DownloadButton.js';
import { UploadButton } from './ui_components/common/UploadButton/UploadButton.js';

const downloadBtn = new DownloadButton({
  text: '下載報表',
  url: '/api/reports/export',
  filename: 'report.xlsx'
});

const uploadBtn = new UploadButton({
  text: '上傳檔案',
  accept: '.csv,.xlsx',
  onUpload: (file) => processFile(file)
});
```

#### ButtonGroup — 按鈕群組

![ButtonGroup](screenshots/after/common-ButtonGroup.png)

```javascript
import { ButtonGroup } from './ui_components/common/ButtonGroup/ButtonGroup.js';

const group = new ButtonGroup({
  buttons: [saveBtn, cancelBtn, deleteBtn],  // BasicButton 實例陣列
  direction: 'horizontal',  // 'horizontal' | 'vertical'
  gap: '8px',
  showSeparator: false,
  theme: 'light'            // 'light' | 'dark' | 'gradient'
});

group.mount(document.getElementById('button-area'));
```

### 5.2 ColorPicker — 色彩選擇器

![ColorPicker](screenshots/after/common-ColorPicker.png)

```javascript
import { ColorPicker } from './ui_components/common/ColorPicker/ColorPicker.js';

const colorPicker = new ColorPicker({
  label: '主題色',
  value: '#4A90D9',
  onChange: (color) => applyColor(color)
});

colorPicker.mount(document.getElementById('color-field'));
```

### 5.3 Dialog / SimpleDialog — 對話框

![Dialog 元件](screenshots/after/common-Dialog.png)

```javascript
import { SimpleDialog } from './ui_components/common/Dialog/SimpleDialog.js';

// 確認對話框（回傳 Promise<boolean>）
const confirmed = await SimpleDialog.confirm('確定要刪除此項目嗎？此操作無法復原。');
if (confirmed) {
  deleteItem();
}

// 訊息對話框（回傳 Promise<true>）
await SimpleDialog.alert('資料已儲存完成。');

// 輸入對話框（回傳 Promise<string|null>）
const name = await SimpleDialog.prompt('請輸入名稱', '預設值');
```

### 5.4 Notification — 通知

![Notification 元件](screenshots/after/common-Notification.png)

```javascript
import { Notification } from './ui_components/common/Notification/Notification.js';

// 成功通知
Notification.success('儲存成功！');

// 錯誤通知
Notification.error('發生錯誤，請稍後再試。');

// 警告通知
Notification.warning('請填寫必填欄位。');

// 資訊通知
Notification.info('新版本已發布。');

// 自訂選項
Notification.show({
  type: 'success',
  message: '操作完成',
  duration: 5000,    // 顯示 5 秒
  position: 'top-right'
});
```

### 5.5 LoadingSpinner — 載入動畫

![LoadingSpinner 元件](screenshots/after/common-LoadingSpinner.png)

```javascript
import { LoadingSpinner } from './ui_components/common/LoadingSpinner/LoadingSpinner.js';

const spinner = new LoadingSpinner({
  text: '資料載入中...',
  size: 'medium' // small | medium | large
});

// 顯示載入
spinner.mount(document.getElementById('content-area'));

// 載入完成後移除
spinner.destroy();
```

### 5.6 Pagination — 分頁

![Pagination 元件](screenshots/after/common-Pagination.png)

```javascript
import { Pagination } from './ui_components/common/Pagination/Pagination.js';

const pagination = new Pagination({
  total: 200,
  pageSize: 20,
  page: 1,
  onChange: (page, pageSize) => loadData(page)
});

pagination.mount(document.getElementById('pagination-area'));
```

### 5.7 Breadcrumb — 麵包屑導航

![Breadcrumb 元件](screenshots/after/common-Breadcrumb.png)

```javascript
import { Breadcrumb } from './ui_components/common/Breadcrumb/Breadcrumb.js';

const breadcrumb = new Breadcrumb({
  items: [
    { text: '首頁', href: '#/' },
    { text: '使用者管理', href: '#/users' },
    { text: '編輯使用者' }  // 最後一項不需要 href
  ]
});

breadcrumb.mount(document.getElementById('breadcrumb-area'));
```

### 5.8 TreeList — 樹狀清單

![TreeList 元件](screenshots/after/common-TreeList.png)

```javascript
import { TreeList } from './ui_components/common/TreeList/TreeList.js';

const tree = new TreeList({
  data: [
    {
      id: 1, label: '總公司', children: [
        { id: 2, label: '技術部', children: [
          { id: 3, label: '前端組' },
          { id: 4, label: '後端組' }
        ]},
        { id: 5, label: '業務部' }
      ]
    }
  ],
  onSelect: (node) => console.log('選擇:', node)
});

tree.mount(document.getElementById('tree-area'));
```

### 5.9 PhotoCard / FeatureCard — 卡片元件

![PhotoCard 元件](screenshots/after/common-PhotoCard.png)

![FeatureCard 元件](screenshots/after/common-FeatureCard.png)

```javascript
import { PhotoCard } from './ui_components/common/PhotoCard/PhotoCard.js';
import { FeatureCard } from './ui_components/common/FeatureCard/FeatureCard.js';

const photoCard = new PhotoCard({
  imageUrl: '/images/landscape.jpg',
  title: '陽明山國家公園',
  description: '台北近郊的自然風景區',
  onClick: () => openDetail()
});

const featureCard = new FeatureCard({
  icon: 'chart',
  title: '數據分析',
  description: '提供即時的數據統計與視覺化報表'
});
```

### 5.10 ImageViewer — 圖片檢視器

![ImageViewer 元件](screenshots/after/common-ImageViewer.png)

```javascript
import { ImageViewer } from './ui_components/common/ImageViewer/ImageViewer.js';

const viewer = new ImageViewer({
  images: [
    { src: '/photos/1.jpg', caption: '照片一' },
    { src: '/photos/2.jpg', caption: '照片二' },
    { src: '/photos/3.jpg', caption: '照片三' }
  ],
  enableZoom: true,
  enableFullscreen: true
});

viewer.mount(document.getElementById('viewer-area'));
```

### 5.11 SortButton — 排序按鈕

用於表格欄位排序，循環切換 none → desc → asc 三種狀態。

```javascript
import { SortButton } from './ui_components/common/SortButton/SortButton.js';

const sortBtn = new SortButton({
  field: 'name',
  state: 'none',        // 'none'|'desc'|'asc'
  size: 'small',         // 'small'|'medium'
  onSort: (field, state) => {
    console.log(`排序 ${field}: ${state}`);
    loadData({ sortBy: field, order: state });
  }
});

sortBtn.mount(headerCell);
sortBtn.setState('asc');  // 程式控制
sortBtn.reset();          // 重置為 none
```

### 5.12 EditorButton — 編輯器工具列按鈕

提供 50+ 種預定義按鈕類型（bold、italic、link、image 等），用於富文字編輯器工具列。

```javascript
import { EditorButton } from './ui_components/common/EditorButton/EditorButton.js';

const boldBtn = new EditorButton({
  type: 'bold',           // 50+ 預定義類型
  onClick: () => document.execCommand('bold'),
  size: 'medium',         // 'small'|'medium'|'large'
  variant: 'default',     // 'default'|'primary'|'ghost'|'outline'
  tooltip: '粗體 (Ctrl+B)'
});

boldBtn.mount(toolbar);
boldBtn.active = true;    // 設定啟用狀態
boldBtn.setDisabled(true); // 禁用
```

### 5.6 Badge — 徽章

用於狀態指示、數字計數、圓點標記。

```javascript
import { Badge } from './ui_components/common/Badge/Badge.js';

// 文字徽章
const badge = new Badge({ text: 'NEW', variant: 'primary', size: 'small' });
badge.render(container);

// 數字徽章（超過 maxCount 顯示 99+）
const count = new Badge({ text: '128', type: 'count', variant: 'danger', maxCount: 99 });
count.render(container);

// 圓點指示器
const dot = new Badge({ type: 'dot', variant: 'success' });
dot.render(container);
```

| 參數 | 說明 | 預設值 |
|------|------|--------|
| `variant` | `default`/`primary`/`success`/`warning`/`danger`/`info` | `default` |
| `type` | `text`/`count`/`dot` | `text` |
| `size` | `small`/`medium`/`large` | `medium` |
| `maxCount` | 數字上限（count 類型） | `99` |

### 5.7 Tag — 標籤

用於分類、過濾、標示。支援可關閉和可點擊模式。

```javascript
import { Tag } from './ui_components/common/Tag/Tag.js';

const tag = new Tag({
  text: 'React',
  variant: 'primary',
  closable: true,
  icon: '⚛️'
});

tag.onClose(() => console.log('標籤已移除'));
tag.render(container);
```

| 參數 | 說明 | 預設值 |
|------|------|--------|
| `variant` | 9 種色彩：`default`/`primary`/`success`/`warning`/`danger`/`info`/`purple`/`teal`/`pink` | `default` |
| `closable` | 顯示關閉按鈕 | `false` |
| `clickable` | 可點擊模式 | `false` |
| `icon` | 前置圖示（emoji 或文字） | — |

### 5.8 Tooltip — 提示框

Hover/Focus 時顯示的工具提示。支援 4 方位、HTML 內容、自動避開邊界。

```javascript
import { Tooltip } from './ui_components/common/Tooltip/Tooltip.js';

// 附加到元素
const tooltip = new Tooltip({
  text: '這是提示文字',
  position: 'top',   // top | bottom | left | right
  trigger: 'hover'   // hover | click | manual
});
tooltip.attach(targetElement);

// 靜態快捷方法
Tooltip.create(element, '提示文字', { position: 'bottom' });
```

### 5.9 Progress — 進度條

線性和環形進度指示器。支援確定值和不確定動畫。

```javascript
import { Progress } from './ui_components/common/Progress/Progress.js';

// 線性進度條
const bar = new Progress({
  value: 60,
  variant: 'primary',
  showText: true
});
bar.render(container);
bar.setValue(80); // 動畫過渡到 80%

// 環形進度
const circle = new Progress({
  type: 'circle',
  value: 75,
  variant: 'success'
});
circle.render(container);

// 不確定模式
const loading = new Progress({ indeterminate: true });
loading.render(container);
```

| 參數 | 說明 | 預設值 |
|------|------|--------|
| `type` | `bar`/`circle` | `bar` |
| `variant` | `primary`/`success`/`warning`/`danger` | `primary` |
| `size` | `small`/`medium`/`large` | `medium` |
| `showText` | 顯示百分比文字 | `false` |
| `indeterminate` | 不確定動畫模式 | `false` |

### 5.10 Divider — 分隔線

水平/垂直分隔線，可帶文字標籤。

```javascript
import { Divider } from './ui_components/common/Divider/Divider.js';

// 基本水平分隔線
new Divider().render(container);

// 帶文字的分隔線
new Divider({
  text: 'OR',
  textPosition: 'center', // left | center | right
  lineStyle: 'dashed'     // solid | dashed | dotted
}).render(container);

// 垂直分隔線
new Divider({ orientation: 'vertical' }).render(container);
```

---

## 6. 佈局元件

佈局元件位於 `packages/javascript/browser/ui_components/layout/` 目錄下，共 10 個元件。

### 6.1 Panel 系列

![Panel 元件](screenshots/after/layout-Panel.png)

Panel 系列提供多種面板類型：BasePanel、BasicPanel、CardPanel、CollapsiblePanel、ModalPanel、DrawerPanel、FocusPanel、ToastPanel，以及統一管理的 PanelManager。

```javascript
import { ModalPanel } from './ui_components/layout/Panel/ModalPanel.js';
import { ToastPanel } from './ui_components/layout/Panel/ToastPanel.js';
import { PanelManager } from './ui_components/layout/Panel/PanelManager.js';

// 模態面板
const modal = new ModalPanel({
  title: '編輯使用者',
  width: '600px',
  onClose: () => console.log('關閉')
});

modal.setContent('<form>...</form>');
modal.mount(document.body);
modal.open();

// Toast 通知面板
const toast = new ToastPanel({
  message: '操作成功',
  type: 'success',
  duration: 3000
});

toast.mount(document.body);
toast.show();

// PanelManager — 統一管理所有面板
const panelManager = new PanelManager();
panelManager.register('editUser', modal);
panelManager.open('editUser');
panelManager.close('editUser');
```

### 6.2 DataTable — 資料表格

![DataTable 元件](screenshots/after/layout-DataTable.png)

```javascript
import { DataTable } from './ui_components/layout/DataTable/DataTable.js';

const table = new DataTable({
  container: document.getElementById('table-area'),
  title: '使用者管理',
  variant: 'default',       // 'default' 或 'search'
  columns: [
    { name: 'name', label: '姓名', options: { sort: true } },
    { name: 'email', label: '電子郵件' },
    { name: 'role', label: '角色', options: { sort: true } },
    {
      name: 'actions',
      label: '操作',
      options: {
        customBodyRender: (value, tableMeta) =>
          `<button onclick="edit(${tableMeta.rowData[0]})">編輯</button>`
      }
    }
  ],
  data: [
    ['王小明', 'wang@example.com', '管理員', ''],
    ['李小華', 'lee@example.com', '編輯者', '']
  ],
  pageSize: 20
});

// 另一種格式 — 物件陣列 + key/title 欄位（audit 模式）
const auditTable = new DataTable({
  container: document.getElementById('audit-area'),
  columns: [
    { key: 'name', title: '姓名', sortable: true },
    { key: 'email', title: '電子郵件' },
    { key: 'role', title: '角色', render: (row) => `<b>${row.role}</b>` }
  ],
  data: [
    { name: '王小明', email: 'wang@example.com', role: '管理員' },
    { name: '李小華', email: 'lee@example.com', role: '編輯者' }
  ]
});
```

### 6.3 SideMenu — 側邊選單

![SideMenu 元件](screenshots/after/layout-SideMenu.png)

```javascript
import { SideMenu } from './ui_components/layout/SideMenu/SideMenu.js';

const menu = new SideMenu({
  items: [
    { id: 'dashboard', icon: 'home', text: '儀表板', href: '#/' },
    {
      id: 'users', icon: 'users', text: '使用者管理',
      children: [
        { id: 'user-list', text: '使用者列表', href: '#/users' },
        { id: 'user-add', text: '新增使用者', href: '#/users/add' }
      ]
    },
    { id: 'settings', icon: 'settings', text: '系統設定', href: '#/settings' }
  ],
  activeId: 'dashboard',
  collapsed: false,          // 是否收合側選單
  accordion: true,           // 手風琴模式（一次只展開一個子選單）
  onSelect: (item) => console.log('選擇:', item)
});

menu.mount(document.getElementById('sidebar'));
```

### 6.4 TabContainer — 頁籤容器

![TabContainer 元件](screenshots/after/layout-TabContainer.png)

```javascript
import { TabContainer } from './ui_components/layout/TabContainer/TabContainer.js';

const tabs = new TabContainer({
  containerId: 'tab-area',  // 掛載容器的 DOM id
  tabs: [
    { id: 'basic', title: '基本資料', content: '<div>...</div>' },
    { id: 'contact', title: '聯絡方式', content: '<div>...</div>' },
    { id: 'permissions', title: '權限設定', content: '<div>...</div>', closable: false }
  ],
  position: 'top',          // 'top' | 'bottom' | 'left' | 'right'
  closable: true,           // 頁籤是否可關閉
  animated: true,
  onTabChange: (tabId) => console.log('切換到:', tabId),
  onTabClose: (tabId) => console.log('關閉:', tabId)
});
```

### 6.5 FormRow — 表單列

![FormRow 元件](screenshots/after/layout-FormRow.png)

```javascript
import { FormRow } from './ui_components/layout/FormRow/FormRow.js';

const row = new FormRow({
  columns: 3,  // 一列顯示 3 個欄位
  gap: '16px',
  fields: [nameInput, emailInput, phoneInput]
});

row.mount(document.getElementById('form-area'));
```

### 6.6 InfoPanel — 資訊面板

![InfoPanel 元件](screenshots/after/layout-InfoPanel.png)

```javascript
import { InfoPanel } from './ui_components/layout/InfoPanel/InfoPanel.js';

const infoPanel = new InfoPanel({
  containerId: 'info-area',  // 掛載容器的 DOM id
  panels: [
    { title: '基本資料', fields: [
      { label: '姓名', value: '王小明' },
      { label: '電話', value: '0912-345-678' }
    ]},
    { title: '系統資訊', fields: [
      { label: '建立日期', value: '2025-01-15' }
    ]}
  ],
  layout: 'grid',        // 'grid' | 'list' | 'masonry'
  columns: 3,
  collapsible: true
});
```

### 6.7 其他佈局元件

#### FunctionMenu — 功能選單

![FunctionMenu 元件](screenshots/after/layout-FunctionMenu.png)

```javascript
import { FunctionMenu } from './ui_components/layout/FunctionMenu/FunctionMenu.js';

const funcMenu = new FunctionMenu({
  containerId: 'func-menu',  // 掛載容器的 DOM id
  items: [
    { id: 'add', icon: 'add', label: '新增' },
    { id: 'export', icon: 'export', label: '匯出' },
    { id: 'print', icon: 'print', label: '列印' }
  ],
  layout: 'horizontal',      // 'horizontal' | 'vertical' | 'grid'
  columns: 4,
  size: 'medium',            // 'small' | 'medium' | 'large'
  onItemClick: (item) => console.log('點擊:', item.id)
});
```

#### WorkflowPanel — 工作流程面板

![WorkflowPanel 元件](screenshots/after/layout-WorkflowPanel.png)

```javascript
import { WorkflowPanel } from './ui_components/layout/WorkflowPanel/WorkflowPanel.js';

const workflow = new WorkflowPanel({
  data: [
    { StageName: '建立', DateTime: '2026-03-01 10:00', UnitName: '資訊室', UserName: '王小明' },
    { StageName: '送審', DateTime: '2026-03-02 14:00', UnitName: '資訊室', UserName: '王小明' },
    { StageName: '審核', DateTime: '2026-03-03 09:00', UnitName: '管理部', UserName: '李大華' },
    { StageName: '核准', DateTime: '2026-03-03 16:00', UnitName: '管理部', UserName: '陳主管' }
  ],
  itemsPerRow: 5,            // 每列節點數（3~7）
  nextStage: { StageName: '結案', NextUnit: '資訊室' },
  showDetails: true,
  onNodeClick: (node) => console.log('節點:', node)
});

workflow.mount(document.getElementById('workflow-area'));
```

### 6.8 DocumentWall — 文件牆

以卡片網格展示文件，支援多選、批次 ZIP 下載、描述編輯與刪除。

```javascript
import { DocumentWall } from './ui_components/layout/DocumentWall/DocumentWall.js';

const wall = new DocumentWall({
  documents: [
    { id: 1, title: '報告.pdf', type: 'pdf', src: '/files/report.pdf', description: '年度報告' }
  ],
  readOnly: false,
  onDownload: (doc) => {},
  onDescription: (doc, text) => {},
  onEdit: (doc) => {},
  onDelete: (doc) => {}
});

wall.mount(document.getElementById('doc-area'));
wall.removeDocument(0);
```

### 6.9 PhotoWall — 照片牆

圖片畫廊元件，支援預覽瀏覽、多選、批次 ZIP 下載。

```javascript
import { PhotoWall } from './ui_components/layout/PhotoWall/PhotoWall.js';

const photos = new PhotoWall({
  photos: [
    { id: 1, src: '/images/photo1.jpg', alt: '照片 1' }
  ],
  readOnly: false,
  onAdd: (photo) => {},
  onDelete: (photo) => {},
  onChange: (photos) => {}
});

photos.mount(document.getElementById('gallery'));
photos.addPhoto({ id: 2, src: '/images/photo2.jpg', alt: '照片 2' });
const allPhotos = photos.getPhotos();
```

---

## 7. 進階輸入元件

進階輸入元件位於 `packages/javascript/browser/ui_components/input/` 目錄下，共 10 個元件。這些元件處理複雜的輸入場景，如地址、電話清單、組織資訊等。

![進階輸入元件總覽](screenshots/after/input-CompositeInputs.png)

### 7.1 ChainedInput — 連動輸入

多層級連動下拉選單，適用於縣市/鄉鎮/村里等階層式資料。

```javascript
import { ChainedInput } from './ui_components/input/ChainedInput/ChainedInput.js';

const regionInput = new ChainedInput({
  label: '地區',
  levels: [
    { name: 'city', label: '縣市', options: citiesData },
    { name: 'district', label: '鄉鎮區', dependsOn: 'city' },
    { name: 'village', label: '村里', dependsOn: 'district' }
  ],
  onLoadOptions: async (level, parentValue) => {
    return await fetch(`/api/regions?parent=${parentValue}`).then(r => r.json());
  }
});

regionInput.mount(document.getElementById('region-field'));
```

### 7.2 AddressInput — 地址輸入

整合地區連動與詳細地址的複合元件。

```javascript
import { AddressInput } from './ui_components/input/AddressInput/AddressInput.js';

const addressInput = new AddressInput({
  label: '通訊地址',
  required: true
});

addressInput.mount(document.getElementById('address-field'));

// 取得完整地址
const address = addressInput.getValue();
// { city: '台北市', district: '中正區', detail: '重慶南路一段122號' }
```

### 7.3 AddressListInput — 多地址輸入

可新增/刪除多筆地址，適用於有多個通訊地址的場景。

```javascript
import { AddressListInput } from './ui_components/input/AddressListInput/AddressListInput.js';

const addressList = new AddressListInput({
  label: '地址清單',
  maxItems: 3
});

addressList.mount(document.getElementById('address-list-field'));
```

### 7.4 PersonInfoList — 人員資訊清單

```javascript
import { PersonInfoList } from './ui_components/input/PersonInfoList/PersonInfoList.js';

const personList = new PersonInfoList({
  label: '家庭成員',
  fields: ['name', 'relationship', 'phone', 'birthDate'],
  maxItems: 10
});

personList.mount(document.getElementById('person-list'));
```

### 7.5 PhoneListInput — 電話清單

```javascript
import { PhoneListInput } from './ui_components/input/PhoneListInput/PhoneListInput.js';

const phoneList = new PhoneListInput({
  label: '聯絡電話',
  maxItems: 5,
  types: ['行動電話', '家用電話', '公司電話']
});

phoneList.mount(document.getElementById('phone-list'));
```

### 7.6 OrganizationInput — 組織輸入

```javascript
import { OrganizationInput } from './ui_components/input/OrganizationInput/OrganizationInput.js';

const orgInput = new OrganizationInput({
  label: '服務單位',
  fields: ['name', 'department', 'title', 'phone']
});

orgInput.mount(document.getElementById('org-field'));
```

### 7.7 其他進階輸入

- **DateTimeInput** — 日期時間複合輸入
- **ListInput** — 通用清單輸入（可新增/刪除/排序項目）
- **SocialMediaList** — 社群帳號清單
- **StudentInput** — 學生資訊輸入

這些元件都遵循統一 API（`mount`、`getValue`、`setValue`、`destroy`）。

---

## 8. 社群元件

社群元件（`social/`）提供人際網絡、動態牆、個人檔案等社群功能的 UI 元件。

### 8.1 Avatar — 頭像

```javascript
import { Avatar } from './ui_components/social/Avatar/Avatar.js';

const avatar = new Avatar({
  src: '/images/user.jpg',
  alt: '王小明',
  size: 'lg',         // 'xs'|'sm'|'md'|'lg'|'xl' (24px ~ 96px)
  badge: 3,           // 通知數量
  onClick: () => {}
});

avatar.mount(document.getElementById('avatar-container'));
avatar.update({ badge: 5 });
```

### 8.2 FeedCard — 動態卡片

```javascript
import { FeedCard } from './ui_components/social/FeedCard/FeedCard.js';

const feed = new FeedCard({
  avatar: '/images/user.jpg',
  author: '王小明',
  authorSub: '資深工程師',
  timestamp: '2026-03-01T10:30:00',
  type: '公告',
  typeColor: 'var(--cl-primary)',
  title: '系統更新通知',
  content: '本次更新包含效能改善...',
  images: ['/images/screenshot.png'],
  tags: ['系統', '更新'],
  onClickDetail: () => {},
  onClickAuthor: () => {}
});

feed.mount(document.getElementById('feed'));

// 批次生成動態列表
const listHTML = FeedCard.listHTML(feedItems);
```

### 8.3 ConnectionCard — 人脈卡片

```javascript
import { ConnectionCard } from './ui_components/social/ConnectionCard/ConnectionCard.js';

const card = new ConnectionCard({
  avatar: '/images/user.jpg',
  name: '李大華',
  subtitle: '產品經理',
  tags: ['設計', 'UX'],
  onClick: () => {}
});

card.mount(container);

// 批次生成人脈網格
const gridHTML = ConnectionCard.gridHTML(contacts);
```

### 8.4 StatCard — 統計卡片

```javascript
import { StatCard } from './ui_components/social/StatCard/StatCard.js';

const stat = new StatCard({
  icon: '📊',
  label: '本月營收',
  value: 'NT$ 1,200,000',
  trend: 'up',           // 'up'|'down'|null
  trendValue: '+12%',
  color: 'var(--cl-success)',
  onClick: () => {}
});

stat.mount(container);
```

### 8.5 Timeline — 時間軸

```javascript
import { Timeline } from './ui_components/social/Timeline/Timeline.js';

const timeline = new Timeline({
  items: [
    {
      timestamp: '2026-03-01T10:00:00',
      type: '建立',
      color: 'var(--cl-success)',
      icon: '✅',
      title: '帳號建立',
      description: '系統自動建立帳號',
      onClick: () => {}
    }
  ],
  grouped: true,       // 按月份分組
  emptyText: '尚無事件'
});

timeline.mount(container);
```

---

## 9. 視覺化元件

視覺化元件位於 `packages/javascript/browser/ui_components/viz/` 目錄下，共 20 個元件。全部使用純 SVG + 原生 DOM 實作，零外部依賴。

![視覺化元件總覽](screenshots/after/viz-Charts.png)

### 9.1 圖表系列

所有圖表繼承自 `BaseChart`，共用統一介面。

```javascript
import { BarChart } from './ui_components/viz/BarChart.js';
import { LineChart } from './ui_components/viz/LineChart.js';
import { PieChart } from './ui_components/viz/PieChart.js';
import { RoseChart } from './ui_components/viz/RoseChart.js';

// 長條圖
const barChart = new BarChart({
  title: '月營收統計',
  data: [
    { label: '一月', value: 120000 },
    { label: '二月', value: 98000 },
    { label: '三月', value: 150000 }
  ],
  width: 600,
  height: 400
});

barChart.mount(document.getElementById('bar-chart'));

// 折線圖
const lineChart = new LineChart({
  title: '使用者趨勢',
  series: [
    { name: '新使用者', data: [100, 120, 115, 140, 160] },
    { name: '活躍使用者', data: [500, 520, 530, 550, 580] }
  ],
  labels: ['一月', '二月', '三月', '四月', '五月'],
  width: 600,
  height: 400
});

lineChart.mount(document.getElementById('line-chart'));

// 圓餅圖
const pieChart = new PieChart({
  title: '瀏覽器市佔率',
  data: [
    { label: 'Chrome', value: 65, color: '#4285F4' },
    { label: 'Safari', value: 19, color: '#FF9500' },
    { label: 'Firefox', value: 10, color: '#FF6611' },
    { label: '其他', value: 6, color: '#999' }
  ]
});

pieChart.mount(document.getElementById('pie-chart'));
```

### 9.2 階層與關係圖

```javascript
import { OrgChart } from './ui_components/viz/OrgChart.js';
import { RelationChart } from './ui_components/viz/RelationChart.js';

// 組織圖 — 支援扁平資料自動轉樹狀 (flatToHierarchy)
const orgChart = new OrgChart({
  data: [
    { id: 1, name: '總經理', parentId: null },
    { id: 2, name: '技術總監', parentId: 1 },
    { id: 3, name: '業務總監', parentId: 1 },
    { id: 4, name: '前端工程師', parentId: 2 }
  ],
  width: 800,
  height: 600
});

orgChart.mount(document.getElementById('org-chart'));

// 關係圖
const relationChart = new RelationChart({
  nodes: [
    { id: 'a', label: '使用者' },
    { id: 'b', label: '訂單' },
    { id: 'c', label: '商品' }
  ],
  edges: [
    { source: 'a', target: 'b', label: '建立' },
    { source: 'b', target: 'c', label: '包含' }
  ]
});

relationChart.mount(document.getElementById('relation-chart'));
```

### 9.3 其他視覺化元件

- **TimelineChart** — 時間軸圖
- **SankeyChart** — 桑基圖（流量視覺化）
- **SunburstChart** — 旭日圖（階層比例）
- **FlameChart** — 火焰圖（效能分析）
- **HierarchyChart** — 階層結構圖

### 9.4 地圖元件

![地圖元件](screenshots/after/data-RegionMap.png)

```javascript
import { LeafletMap } from './ui_components/viz/LeafletMap.js';

const map = new LeafletMap({
  center: [25.0330, 121.5654], // 台北 101
  zoom: 13,
  markers: [
    { lat: 25.0330, lng: 121.5654, popup: '台北 101' }
  ]
});

map.mount(document.getElementById('map-area'));
```

其他地圖元件：MapEditor、MapEditorV2、CanvasMap。

#### OSMMapEditor — OSM 地圖編輯器

繼承 WebPainter 的地圖編輯器，使用 OpenStreetMap 底圖，整合繪圖工具與地理功能。

```javascript
import { OSMMapEditor } from './ui_components/viz/OSMMapEditor/OSMMapEditor.js';

const editor = new OSMMapEditor({
    container: '#map-editor',
    width: 1000,
    height: 700,
    center: { lat: 25.033, lng: 121.565 },
    zoom: 13,
    tileLayer: 'osm',        // 'osm' | 'osmHot' | 'cartoDB'
    showCompass: true,
    showScale: true,
    showCoords: true
});

// 地圖操作
editor.setCenter(48.8566, 2.3522);  // 移動到巴黎
editor.setZoom(15);

// GeoJSON 匯入/匯出
await editor.importGeoJSON(file);
const geojson = editor.exportGeoJSON();
```

**功能**：OSM 底圖（3 種圖磚源）、距離/面積測量、座標面板（DD/DMS）、比例尺、指北針、GeoJSON 匯入匯出、地圖截圖、繼承 WebPainter 所有繪圖/圖層/匯出功能。

### 9.5 繪圖工具

```javascript
import { DrawingBoard } from './ui_components/viz/DrawingBoard/DrawingBoard.js';

const board = new DrawingBoard({
  width: 800,
  height: 600,
  tools: ['pen', 'line', 'rect', 'circle', 'eraser'],
  strokeColor: '#000',
  strokeWidth: 2
});

board.mount(document.getElementById('drawing-area'));

// 匯出圖片
const imageData = board.toDataURL('image/png');
```

### 9.6 WebTextEditor — 富文字編輯器

位於 `editor/WebTextEditor/`，完整的所見即所得編輯器。

```javascript
import { WebTextEditor } from './ui_components/editor/WebTextEditor/WebTextEditor.js';

const editor = new WebTextEditor({
  container: '#editor-area',
  placeholder: '請輸入內容...',
  height: '400px',
  content: '<p>初始內容</p>',
  readOnly: false,
  onChange: (html) => console.log('內容變更')
});

// 取得/設定內容
const html = editor.getContent();
editor.setContent('<p>新內容</p>');
```

**功能**：工具列、搜尋/取代 (Ctrl+F/H)、匯出 (PDF/Word/Markdown)、自動儲存、歷史 (Undo/Redo)、表格編輯、圖片縮放、全螢幕、字數統計。

### 9.7 RegionMap — 台灣行政區地圖

SVG 地圖元件，支援 22 個行政區的資料視覺化與互動。

```javascript
import { RegionMap } from './ui_components/data/RegionMap/RegionMap.js';

const map = new RegionMap({
  data: {
    'TPE': { value: 2700000, label: '台北市', color: '#FF5722' },
    'NWT': { value: 4000000, label: '新北市', color: '#4CAF50' }
  },
  width: '600px',
  height: '400px',
  showLabels: true,
  showValues: true,
  colorScale: RegionMap.createColorScale(0, 5000000, ['#e3f2fd', '#1565c0']),
  onClick: (regionCode, data) => console.log(regionCode, data)
});

map.mount(document.getElementById('map-area'));
map.highlightRegion('TPE');
map.setData(updatedData);
```

---

## 10. 行為模組與通用函式

### 10.1 TriggerEngine — 觸發引擎

TriggerEngine 提供 8 個內建原子行為，用於欄位間的聯動邏輯。

```javascript
import { TriggerEngine } from './page-generator/TriggerEngine.js';

const engine = new TriggerEngine();

// 內建行為：clear, setValue, show, hide, setReadonly, setRequired, reload, reloadOptions

// 定義觸發規則
engine.addRule({
  source: 'userType',           // 來源欄位
  condition: (value) => value === 'admin',  // 觸發條件
  actions: [
    { type: 'show', target: 'adminPanel' },
    { type: 'setRequired', target: 'adminCode', params: { required: true } }
  ]
});

// 當來源欄位值變更時觸發
engine.trigger('userType', 'admin');

// 自訂行為
engine.registerAction('highlight', (target, params) => {
  const el = document.getElementById(target);
  el.style.backgroundColor = params.color || '#FFFFCC';
});
```

**內建行為說明：**

| 行為 | 說明 | 範例 |
|------|------|------|
| `clear` | 清空目標欄位值 | `{ type: 'clear', target: 'email' }` |
| `setValue` | 設定目標欄位值 | `{ type: 'setValue', target: 'status', params: { value: 'active' } }` |
| `show` | 顯示目標元素 | `{ type: 'show', target: 'detailSection' }` |
| `hide` | 隱藏目標元素 | `{ type: 'hide', target: 'detailSection' }` |
| `setReadonly` | 設為唯讀 | `{ type: 'setReadonly', target: 'name', params: { readonly: true } }` |
| `setRequired` | 設為必填 | `{ type: 'setRequired', target: 'phone', params: { required: true } }` |
| `reload` | 重新載入元件資料 | `{ type: 'reload', target: 'dataTable' }` |
| `reloadOptions` | 重新載入選項 | `{ type: 'reloadOptions', target: 'cityDropdown' }` |

### 10.2 BehaviorDef — 行為定義

BehaviorDef 定義頁面級別的行為模式：

```javascript
const behaviorDef = {
  // 頁面初始化時執行
  onInit: (page) => {
    page.loadData();
    page.setFieldReadonly('createdDate', true);
  },

  // 儲存後執行
  onSave: (page, result) => {
    Notification.success('儲存成功');
    page.navigateTo('/list');
  },

  // 刪除後執行
  onDelete: (page, result) => {
    Notification.success('刪除成功');
    page.navigateTo('/list');
  },

  // 欄位變更聯動
  fieldTriggers: {
    'category': [
      {
        condition: (value) => value === 'urgent',
        actions: [
          { type: 'show', target: 'priorityPanel' },
          { type: 'setRequired', target: 'deadline' }
        ]
      }
    ],
    'country': [
      {
        condition: () => true,
        actions: [
          { type: 'reloadOptions', target: 'cityDropdown' }
        ]
      }
    ]
  }
};
```

### 10.3 SPA 核心框架

SPA 核心位於 `templates/spa/frontend/core/`，提供完整的單頁應用框架。

#### Router — Hash 路由

```javascript
import { Router } from './core/Router.js';

const router = new Router();

// 註冊路由
router.addRoute('/', HomePage);
router.addRoute('/users', UserListPage);
router.addRoute('/users/:id', UserDetailPage);
router.addRoute('/users/:id/edit', UserEditPage);

// 巢狀路由
router.addRoute('/admin', AdminPage, [
  { path: '/admin/settings', page: AdminSettingsPage },
  { path: '/admin/logs', page: AdminLogsPage }
]);

// 啟動路由
router.start();

// 程式化導航
router.navigate('/users/123');
```

#### Store — 狀態管理

```javascript
import { Store } from './core/Store.js';

const store = new Store({
  user: null,
  theme: 'light',
  notifications: []
});

// 訂閱狀態變更
store.subscribe('user', (newUser, oldUser) => {
  console.log('使用者變更:', newUser);
});

// 更新狀態
store.set('user', { id: 1, name: '王小明' });

// 取得狀態
const user = store.get('user');
```

#### ApiService — RESTful API 服務

```javascript
import { ApiService } from './core/ApiService.js';

const api = new ApiService({
  baseUrl: '/api',
  // JWT 自動附加在 Authorization: Bearer 標頭（token 預設存於 localStorage）
});

// CRUD 操作
const users = await api.get('/users');
const user = await api.get('/users/123');
const newUser = await api.post('/users', { name: '李小華', email: 'lee@example.com' });
await api.put('/users/123', { name: '李小華（更新）' });
await api.delete('/users/123');

// 分頁查詢
const result = await api.get('/users', { page: 1, pageSize: 20, keyword: '王' });
// { data: [...], total: 100, page: 1, pageSize: 20 }
```

#### BasePage — 頁面生命週期

```javascript
import { BasePage } from './core/BasePage.js';

class UserListPage extends BasePage {
  constructor() {
    super();
    this.title = '使用者管理';
  }

  // 頁面生命週期
  async onInit() {
    // 初始化元件
    this.table = new DataTable({ ... });
  }

  async onLoad(params) {
    // 載入資料（每次進入頁面觸發）
    const data = await this.api.get('/users');
    this.table.setData(data);
  }

  onRender(container) {
    // 渲染 UI
    this.table.mount(container);
  }

  onDestroy() {
    // 清理資源
    this.table.destroy();
  }
}
```

### 10.4 ComponentBinder / ComponentFactory

#### ComponentBinder — 元件資料繫結

```javascript
import { ComponentBinder } from './ui_components/binding/ComponentBinder.js';

const binder = new ComponentBinder();

// 將元件與資料模型繫結
binder.bind(nameInput, 'user.name');
binder.bind(emailInput, 'user.email');
binder.bind(roleDropdown, 'user.role');

// 設定資料模型（自動更新元件值）
binder.setModel({
  user: { name: '王小明', email: 'wang@example.com', role: 'admin' }
});

// 取得所有繫結元件的值
const formData = binder.getValues();
```

#### ComponentFactory — 元件工廠

```javascript
import { ComponentFactory } from './ui_components/binding/ComponentFactory.js';

// 根據欄位定義動態建立元件
const component = ComponentFactory.create({
  type: 'text',
  name: 'username',
  label: '使用者名稱',
  required: true,
  maxLength: 50
});

component.mount(container);
```

### 10.5 工具與服務 (utils/)

#### security.js — XSS 防護

```javascript
import { escapeHtml, sanitizeUrl, sanitizeHTML } from './utils/security.js';

// HTML 內容跳脫
const safeHtml = escapeHtml(userInput);
element.innerHTML = `<p>${escapeHtml(userInput)}</p>`;

// URL 安全處理（阻擋 javascript: / vbscript: 協議）
element.innerHTML = `<a href="${sanitizeUrl(userUrl)}">連結</a>`;

// HTML 標籤白名單過濾
const cleanHtml = sanitizeHTML(dirtyHtml);
```

> **重要提醒**：所有使用者輸入在輸出到 HTML 時，都必須使用 `escapeHtml()` 進行跳脫，並使用 `sanitizeUrl()` 處理 URL，以防止 XSS 攻擊。

#### GeolocationService — 地理定位服務

```javascript
import { GeolocationService } from './utils/GeolocationService.js';

const geo = new GeolocationService();

// 取得目前位置
const position = await geo.getCurrentPosition();
console.log('緯度:', position.latitude, '經度:', position.longitude);
```

#### WeatherService — 天氣服務

```javascript
import { WeatherService } from './utils/WeatherService.js';

const weather = new WeatherService({ apiKey: 'YOUR_API_KEY' });
const forecast = await weather.getForecast(25.033, 121.565);
```

---

## 11. 頁面生成器

頁面生成器位於 `packages/javascript/browser/page-generator/`，可根據欄位定義自動生成完整頁面。

### 11.1 支援的 30 種欄位類型

| 類別 | 欄位類型 | 說明 |
|------|----------|------|
| 基本文字 | `text` | 單行文字 |
| | `email` | 電子郵件 |
| | `password` | 密碼 |
| | `textarea` | 多行文字 |
| | `richtext` | 富文字編輯器 |
| 數值 | `number` | 數字輸入 |
| 日期時間 | `date` | 日期 |
| | `time` | 時間 |
| | `datetime` | 日期時間 |
| 選擇 | `select` | 單選下拉 |
| | `multiselect` | 多選下拉 |
| | `checkbox` | 核取方塊 |
| | `toggle` | 開關切換 |
| | `radio` | 單選按鈕 |
| | `color` | 色彩選擇 |
| 媒體 | `image` | 圖片上傳 |
| | `file` | 檔案上傳 |
| | `canvas` | 畫布繪圖 |
| 進階 | `geolocation` | 地理定位 |
| | `weather` | 天氣資訊 |
| | `address` | 地址輸入 |
| | `addresslist` | 多地址輸入 |
| | `chained` | 連動下拉 |
| | `list` | 清單輸入 |
| | `personinfo` | 人員資訊 |
| | `phonelist` | 電話清單 |
| | `socialmedia` | 社群帳號 |
| | `organization` | 組織資訊 |
| | `student` | 學生資訊 |
| 其他 | `hidden` | 隱藏欄位 |

### 11.2 頁面定義格式

```javascript
import { PageDefinitionAdapter } from './page-generator/PageDefinitionAdapter.js';

const pageDefinition = {
  page: {
    pageName: '使用者管理',
    entity: 'user',
    view: 'adminList'
  },
  fields: [
    {
      fieldName: 'name',
      label: '姓名',
      fieldType: 'text',
      formRow: 1,
      formCol: 6,
      listOrder: 1,
      isRequired: true,
      isSearchable: true
    },
    {
      fieldName: 'role',
      label: '角色',
      fieldType: 'select',
      formRow: 1,
      formCol: 6,
      listOrder: 2,
      optionsSource: {
        type: 'static',
        items: [
          { value: 'admin', label: '管理員' },
          { value: 'editor', label: '編輯者' },
          { value: 'viewer', label: '檢視者' }
        ]
      }
    }
  ]
};

// 若要給靜態 PageGenerator 使用，先轉成舊格式
const staticDefinition = PageDefinitionAdapter.toOldFormat(pageDefinition);
```

### 11.3 靜態生成 (PageGenerator)

```javascript
import {
  PageDefinitionAdapter,
  PageGenerator
} from './page-generator/index.js';

const generator = new PageGenerator();
const staticDefinition = PageDefinitionAdapter.toOldFormat(pageDefinition);
const result = generator.generate(staticDefinition);

if (result.errors.length > 0) {
  console.error(result.errors);
} else {
  console.log(result.code);
}
```

> `generate()` 的回傳值是 `{ code, errors }`。如果 definition 未提供完整 API 或自訂 behavior，產生碼會保留 `_save()` / behavior stub 供後續補實作。

### 11.4 動態渲染 (DynamicPageRenderer)

DynamicPageRenderer 支援三種渲染模式，無需生成靜態檔案即可動態建立頁面。

```javascript
import { DynamicPageRenderer } from './page-generator/DynamicPageRenderer.js';

const formPage = new DynamicPageRenderer({
  definition: pageDefinition,
  mode: 'form',
  data: existingData,
  onSave: async (values) => api.post('/users', values),
  onCancel: () => router.navigate('/users')
});

await formPage.init();
formPage.mount(document.getElementById('app'));

const detailPage = new DynamicPageRenderer({
  definition: pageDefinition,
  mode: 'detail',
  data: userData,
  onBack: () => router.navigate('/users'),
  onEdit: () => router.navigate(`/users/${userData.id}/edit`)
});

await detailPage.init();
detailPage.mount(document.getElementById('detail'));

const listPage = new DynamicPageRenderer({
  definition: pageDefinition,
  mode: 'list',
  onSearch: async (params) => api.get('/users', { params }),
  onAction: (action, row) => router.navigate(`/users/${row.id}`),
  pageSize: 20
});

await listPage.init();
listPage.mount(document.getElementById('list'));
```

### 11.5 PageDefinitionAdapter — 格式轉換

```javascript
import { PageDefinitionAdapter } from './page-generator/PageDefinitionAdapter.js';

// 新格式（DynamicPageRenderer / page-gen CLI）→ 舊格式（PageGenerator）
const oldDefinition = PageDefinitionAdapter.toOldFormat(pageDefinition);

// 舊格式（PageGenerator）→ 新格式（DynamicPageRenderer）
const newDefinition = PageDefinitionAdapter.toNewFormat(oldDefinition);
```

---

## 12. SPA 生成器

SPA 相關工具分成兩部分：

- `tools/spa-generator/`：Web UI
- `templates/spa/scripts/`：範本 CLI

### 12.1 Web UI（port 3080）

啟動方式：

```bash
npm run serve
# 瀏覽器開啟 http://localhost:3080
```

Web UI 提供以下功能：

- **專案建立** — ProjectCreatePage，指定專案名稱、輸出路徑、埠號與管理員帳號
- **頁面生成** — PageGeneratorPage，產生前端頁面骨架
- **API 生成** — ApiGeneratorPage，產生 Model / Service / API 路由
- **功能生成** — FeatureGeneratorPage，一次產生前後端 CRUD 骨架
- **Page Builder** — PageBuilderPage，以 JSON 定義即時預覽 form / detail / list
- **頁面定義編輯器** — PageDefinitionEditorPage，以 GUI 方式編輯 PageDefinition

### 12.2 CLI 指令

```bash
# 建立新專案（互動式）
node templates/spa/scripts/spa-cli.js new

# 建立新專案（非互動）
node templates/spa/scripts/spa-cli.js new --name my-blog --output ./projects

# 生成頁面（前端）
node templates/spa/scripts/spa-cli.js page article/ArticleList
node templates/spa/scripts/spa-cli.js page article/ArticleDetail --detail

# 生成 API（後端）
node templates/spa/scripts/spa-cli.js api Article --fields "Title:string,PublishedAt:datetime"

# 生成完整功能（前端 + 後端）
node templates/spa/scripts/spa-cli.js feature Article --fields "Title:string,PublishedAt:datetime"
```

### 12.3 生成的專案結構

```
projects/my-app/
├── frontend/
│   ├── adapters/           # 前端資料轉接器
│   ├── components/         # 範本內附元件
│   ├── core/               # SPA 核心框架
│   │   ├── Router.js       # Hash 路由
│   │   ├── Store.js        # 狀態管理
│   │   ├── ApiService.js   # API 呼叫
│   │   ├── BasePage.js     # 頁面基礎類別
│   │   ├── Layout.js       # 版面配置
│   │   └── Security.js     # 安全性工具
│   ├── pages/              # 頁面
│   │   └── users/
│   │       ├── UserListPage.js
│   │       └── UserDetailPage.js
│   ├── styles/             # 樣式
│   └── index.html          # 入口檔案
├── backend/
│   ├── Controllers/        # 控制器
│   ├── Data/               # AppDb (BaseOrm) / 初始化
│   ├── Models/             # 資料模型
│   ├── Services/           # 業務邏輯
│   ├── Program.cs          # .NET 8 Minimal API 入口
│   ├── appsettings.json    # 設定檔
│   └── my-app.csproj       # 專案檔
├── tools/
│   └── static-server/      # 前端靜態伺服器
├── project.json            # 安全裁剪後的專案設定
├── start.bat               # Windows 啟動腳本
└── start.sh                # Unix 啟動腳本
```

> SQLite 檔名由 `project.json` / `appsettings.json` 設定，實際資料庫檔案會在第一次啟動後建立。

---

## 13. 後端套件 C#

C# 後端套件位於 `packages/csharp/`，提供 .NET 8 Minimal API 的基礎架構。

### 13.1 BaseOrm — ORM 基礎

支援 CRUD、分頁查詢與 Schema 管理。

```csharp
using BaseOrm;

// 初始化
var orm = new BaseOrm("Data Source=app.db");

// 查詢
var users = await orm.QueryAsync<User>("SELECT * FROM Users WHERE Name LIKE @Name",
    new { Name = "%王%" });

// 分頁查詢
var paged = await orm.PagedQueryAsync<User>(
    "SELECT * FROM Users",
    page: 1,
    pageSize: 20
);
// paged.Data, paged.Total, paged.Page, paged.PageSize

// 新增
var id = await orm.InsertAsync("Users", new {
    Name = "王小明",
    Email = "wang@example.com",
    CreatedAt = DateTime.Now
});

// 更新
await orm.UpdateAsync("Users", new { Name = "王小明（修改）" },
    new { Id = 1 });

// 刪除
await orm.DeleteAsync("Users", new { Id = 1 });
```

### 13.2 Repository + UnitOfWork

```csharp
// Repository 模式
public class UserRepository : BaseRepository<User>
{
    public UserRepository(IDbConnection connection) : base(connection) { }

    public async Task<IEnumerable<User>> GetActiveUsersAsync()
    {
        return await QueryAsync("SELECT * FROM Users WHERE IsActive = 1");
    }
}

// UnitOfWork 模式
using var uow = new UnitOfWork(connectionString);
var userRepo = uow.GetRepository<UserRepository>();
var roleRepo = uow.GetRepository<RoleRepository>();

await userRepo.InsertAsync(newUser);
await roleRepo.InsertAsync(newRole);

uow.Commit(); // 交易提交
```

### 13.3 JWT Helper + PasswordHasher

```csharp
using Security;

// 密碼雜湊（PBKDF2, 100,000 iterations）
var hashedPassword = PasswordHasher.Hash("MyPassword123");
bool isValid = PasswordHasher.Verify("MyPassword123", hashedPassword);

// JWT 產生與驗證
var jwtHelper = new JwtHelper(secretKey, issuer, audience);

// 產生 Token
var token = jwtHelper.GenerateToken(new Dictionary<string, string>
{
    ["userId"] = "123",
    ["role"] = "admin"
}, expiresInMinutes: 60);

// 驗證 Token
var claims = jwtHelper.ValidateToken(token);
```

### 13.4 BaseController + Middleware

```csharp
// Program.cs — .NET 8 Minimal API
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// 註冊中介軟體
app.UseExceptionMiddleware();  // 全域例外處理
app.UseCors("AllowedOrigins");  // CORS
app.UseAuthentication();
app.UseAuthorization();

// API 端點
app.MapGet("/api/users", async (UserService service, int page = 1, int pageSize = 20) =>
{
    var result = await service.GetPagedAsync(page, pageSize);
    return ApiResponse.Success(result);
});

app.MapPost("/api/users", async (UserService service, CreateUserDto dto) =>
{
    var user = await service.CreateAsync(dto);
    return ApiResponse.Created(user);
});

app.MapPut("/api/users/{id}", async (UserService service, int id, UpdateUserDto dto) =>
{
    await service.UpdateAsync(id, dto);
    return ApiResponse.Success();
});

app.MapDelete("/api/users/{id}", async (UserService service, int id) =>
{
    await service.DeleteAsync(id);
    return ApiResponse.Success();
});

app.Run();
```

### 13.5 ApiResponse + Pagination

```csharp
// 統一 API 回應格式
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public T Data { get; set; }
    public int? Total { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

// 使用範例
return ApiResponse.Success(data);                    // 成功
return ApiResponse.Created(newItem);                  // 新增成功
return ApiResponse.Error("找不到資料", 404);           // 錯誤
return ApiResponse.Paged(data, total, page, pageSize); // 分頁
```

### 13.6 BaseCache — 記憶體快取

Redis 風格的記憶體快取，支援 Key-Value、Queue、Stack、List、Hash、Set、Pub/Sub。

```csharp
using BaseCache;

var cache = new BaseCache(new CachOptions {
    CleanupInterval = TimeSpan.FromMinutes(1),
    MaxItems = 10000
});

// Key-Value 操作
cache.Set("user:1", userData, ttl: TimeSpan.FromMinutes(30));
var user = cache.Get<User>("user:1");

// GetOrSet — 快取穿透防護
var data = cache.GetOrSet("report:daily", () => {
    return GenerateReport(); // 只在快取未命中時執行
}, ttl: TimeSpan.FromHours(1));

// Hash 操作（類似 Redis HSET/HGET）
cache.HSet("session:abc", "userId", "123");
cache.HSet("session:abc", "role", "admin");
var userId = cache.HGet<string>("session:abc", "userId");

// 佇列與堆疊
cache.Enqueue("tasks", new Task { Id = 1 });
var task = cache.Dequeue<Task>("tasks");

// Pub/Sub
cache.Subscribe("notifications", msg => Console.WriteLine(msg));
cache.Publish("notifications", "新訊息");

// 持久化
cache.SaveToFile("cache.json");
cache.LoadFromFile("cache.json");

// 統計
var stats = cache.Stats; // Hits, Misses, HitRate
```

---

## 14. 安全性指引

### 14.1 XSS 防護

**所有使用者輸入在輸出時都必須跳脫。**

```javascript
import { escapeHtml, sanitizeUrl } from './utils/security.js';

// 正確做法
element.innerHTML = `<p>${escapeHtml(userInput)}</p>`;
element.innerHTML = `<a href="${sanitizeUrl(url)}" title="${escapeHtml(title)}">連結</a>`;

// 錯誤做法 — 永遠不要這樣做！
element.innerHTML = `<p>${userInput}</p>`;         // XSS 漏洞！
element.innerHTML = `<a href="${url}">連結</a>`;   // XSS 漏洞！
```

### 14.2 密碼安全

使用 PBKDF2 演算法，100,000 次迭代：

```csharp
// 雜湊密碼（儲存到資料庫時）
var hashed = PasswordHasher.Hash(plainPassword);

// 驗證密碼（登入時）
bool isMatch = PasswordHasher.Verify(plainPassword, hashedPassword);
```

> 切勿以明文儲存密碼，也不要使用 MD5 或 SHA1 等過時的雜湊演算法。

### 14.3 JWT 認證

```csharp
// 設定 JWT（在 Program.cs）
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is required");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "SpaApi";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtIssuer,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
```

目前 SPA 範本與 SPA Generator 預設透過 `Authorization: Bearer` 標頭傳送 JWT，前端 token 目前保存在 `localStorage`。若改用 Cookie 傳輸，應啟用 `HttpOnly`、`Secure` 與 `SameSite`。

### 14.4 CORS 設定

```csharp
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3080")  // 明確指定允許來源
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
```

> 切勿在正式環境使用 `AllowAnyOrigin()`。只有在採用 Cookie 型認證時，才需要額外設定 `AllowCredentials()`。

### 14.5 速率限制

所有 API 端點必須實作速率限制，防止暴力攻擊與濫用：

```csharp
// .NET 8 內建速率限制
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("api", limiter =>
    {
        limiter.PermitLimit = 100;       // 每個視窗允許 100 次請求
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});

app.UseRateLimiter();
```

### 14.6 輸入驗證

```csharp
// 後端驗證範例
app.MapPost("/api/users", async (CreateUserDto dto) =>
{
    // 驗證必填欄位
    if (string.IsNullOrWhiteSpace(dto.Name))
        return ApiResponse.Error("姓名為必填欄位", 400);

    // 驗證格式
    if (!IsValidEmail(dto.Email))
        return ApiResponse.Error("電子郵件格式不正確", 400);

    // 驗證長度
    if (dto.Name.Length > 100)
        return ApiResponse.Error("姓名不得超過 100 字元", 400);

    // 通過驗證後處理
    var user = await service.CreateAsync(dto);
    return ApiResponse.Created(user);
});
```

### 14.7 安全性檢查清單

在部署前請確認以下項目：

- [ ] 所有使用者輸入已使用 `escapeHtml()` / `sanitizeUrl()` 跳脫
- [ ] 密碼使用 PBKDF2（100K iterations）雜湊
- [ ] JWT 傳輸與儲存方式已審查（預設 Bearer Header；若使用 Cookie 則需 `HttpOnly` / `Secure` / `SameSite`）
- [ ] CORS 已設定明確的允許來源（非 `*`）
- [ ] API 端點已啟用速率限制
- [ ] 所有輸入已在後端驗證
- [ ] 敏感設定（密鑰、連線字串）未寫死在程式碼中
- [ ] HTTPS 已啟用

---

> **本手冊涵蓋 Bricks4Agent 的核心功能與使用方式。如需更深入的特定元件文件，請參閱各元件目錄下的 README.md。**
