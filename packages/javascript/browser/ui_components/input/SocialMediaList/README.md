# SocialMediaList

社群軟體帳號列表輸入元件，繼承 `ListInput`，每筆項目包含平台與帳號。

## API

### Constructor

```js
new SocialMediaList(options?)
```

繼承 `ListInput` 所有選項，預設值：

| 參數 | 預設值 | 說明 |
|------|--------|------|
| `title` | `'社群軟體列表'` | 標題 |
| `minItems` | `0` | 最少項目數 |
| `maxItems` | `5` | 最多項目數 |
| `addButtonText` | `'新增帳號'` | 新增按鈕文字 |

每筆項目欄位：

| 欄位 name | type | 說明 |
|-----------|------|------|
| `platform` | `select` | 平台（LINE/Facebook/Instagram/Twitter (X)/WeChat/Telegram/其他） |
| `account` | `text` | 帳號 ID 或連結 |

### 方法

繼承 `ListInput` 所有方法：`mount(container)`、`getValues()`、`setValues(items)`。

## 使用範例

```js
import { SocialMediaList } from './input/SocialMediaList/index.js';

const social = new SocialMediaList({
    onChange: (items) => console.log(items)
});
social.mount('#container');

// 取得所有帳號
const data = social.getValues();
// [{platform: 'LINE', account: 'my-line-id'}, ...]
```

## Demo

`packages/javascript/browser/ui_components/input/demo.html`
