# Bricks4Agent 客製元件 JSON

客製元件是由現有 Bricks4Agent 元件組成的純 JSON 定義。JSON 不包含 JavaScript、HTML 或任意 CSS；執行期由 `CustomComponentRegistry` 驗證、註冊，再由 `CustomComponentRenderer` 建立元件與管理生命週期。

## 三種層級

層級由結構自動推導，匯出檔中的 `kind` 必須與推導結果一致：

- `atomic`：根節點是單一內建元件，或引用另一個 atomic 客製元件。

- `composite`：由 group 組合元件，有效組合深度不超過 3，且未同時組合兩個以上 composite 客製元件。

- `template`：有效組合深度超過 3、引用 template，或整棵組合圖含兩個以上 composite 客製元件。

分類器會拒絕未知元件、循環引用、重複 node id、宣告層級不符、prototype pollution key、raw HTML key 與非 JSON 值。

## 資料夾契約

```text
packages/javascript/browser/custom_components/
├─ definitions/                  # 開發者放置 *.json
├─ custom-component.schema.json  # JSON Schema
├─ registry.json                 # build 產生，瀏覽器從這裡發現定義
├─ build-registry.mjs
└─ README.md
```

新增或修改 JSON 後執行：

```bash
npm run custom-components:build
npm run custom-components:check
npm run test:custom-components
```

瀏覽器不能直接列舉資料夾，因此 `registry.json` 是必要的 deterministic 索引。`--check` 只比對，不會改檔，適合 CI。

## 最小定義

```json
{
  "$schema": "../custom-component.schema.json",
  "schema_version": 1,
  "component_id": "custom.customer_name",
  "registry_name": "CustomerName",
  "display_name": "客戶名稱",
  "version": "1.0.0",
  "kind": "atomic",
  "description": "單一客戶名稱輸入",
  "root": {
    "type": "component",
    "id": "name",
    "component": "TextInput",
    "options": {
      "label": "客戶名稱",
      "placeholder": "請輸入客戶名稱"
    }
  }
}
```

組合節點使用 `type: "group"`；引用另一個客製元件使用 `type: "custom"`。group 的 layout 僅接受下列 allowlist：

- `mode`: `stack | row | grid`

- `gap`: `none | xs | sm | md | lg | xl`

- `columns`: `1..12`

- `align`: `start | center | end | stretch`

## Runtime 使用

客製元件樣式刻意不併入 `ui_components/theme.css`，以維持核心 UI 套件可獨立發布。使用客製元件的頁面需另外載入：

```html
<link rel="stylesheet" href="./packages/javascript/browser/custom_components/custom-components.css">
```

```js
import {
  CustomComponentRegistry,
} from './packages/javascript/browser/custom_components/index.js';

const registry = new CustomComponentRegistry();
await registry.loadFolder('/packages/javascript/browser/custom_components/');

const component = registry.create('CustomerName', {
  nodeOptions: {
    name: { placeholder: '執行期覆寫，不寫回 JSON' },
  },
});
component.mount(document.querySelector('#app'));

// atomic 回傳單值；composite/template 回傳以 node id 為 key 的物件。
console.log(component.getValue());
component.destroy();
registry.dispose();
```

`loadFolder()` 成功後，預設也會把客製元件註冊到 `ComponentFactory`，所以可同步建立：

```js
const component = ComponentFactory.create('CustomerName');
```

Registry 採整批原子安裝：任何一個檔案驗證失敗時，該批次不會留下半套註冊結果；更新既有定義時會重驗整張相依圖，內建名稱也不可被覆寫。`dispose()` 只移除仍由該 Registry 擁有的 factory class，不會刪除外部後來替換的註冊。

## DynamicPageRenderer 使用

動態表單可在 `init()` 的非同步邊界載入整個資料夾：

```js
const page = new DynamicPageRenderer({
  mode: 'form',
  customComponents: {
    folder: '/packages/javascript/browser/custom_components/',
  },
  definition: {
    fields: [
      {
        fieldName: 'customerName',
        fieldType: 'text',
        component: 'CustomerName',
        label: '客戶名稱',
        defaultValue: '王小明',
      },
    ],
  },
});

await page.init();
page.mount('#app');
```

同時提供 `definitions` 與 `folder` 時，會先取得並驗證全部來源，再一次性註冊；folder 讀取失敗不會留下 inline definition。`DynamicPageRenderer` 自建的 Registry 不會寫入全域 `ComponentFactory`，並會在 `destroy()` 清理；外部注入的 Registry 仍由呼叫端管理。

SPA 的 `DefinitionRuntimePage` 不會從低信任頁面 JSON 讀取 folder URL。請由可信頁面 class 或 constructor options 提供：

```js
class CustomerPage extends DefinitionRuntimePage {
  static customComponents = {
    folder: '/packages/javascript/browser/custom_components/',
  };
}
```

目前這個自動接線針對 dynamic form。直接 runtime 使用不受頁面模式限制；靜態 `PageGenerator.generate()` 不會自動把 JSON 資料夾打包進輸出，產物若要使用客製元件，必須一併部署 `custom_components` 並在啟動階段 `loadFolder()`。

## Studio

以 repository root 啟動靜態伺服器後開啟 Theme Studio，切到「元件組合」頁籤：

```text
/tools/theme-studio/index.html?tab=components
```

`/tools/custom-component-studio/index.html` 保留為直接進入元件組合器的相容 URL，會導向同一頁的第二頁籤。Studio 沒有第二份手寫 UI：唯一權威頁面定義是 `tools/theme-studio/studio.page.json`，由 `DynamicPageRenderer` 的 `tool` mode 產生。JSON 只引用 Bricks4Agent 元件、state bindings 與可信 command ID；工具列、頁籤、輸入、清單、樹狀結構與按鈕都必須保有正式元件 instance provenance。只有檔案 picker、下載與剪貼簿等瀏覽器 transport 留在元件封裝或 utility 內。

Studio 提供 catalog 搜尋、結構 outline、group layout、options JSON、即時分類與預覽、JSON 匯入、驗證、複製及下載匯出。匯入只接受單一 `.json` 且上限 1 MB；匯出前一定會重算 `kind` 並跑同一套 runtime validator。這裡的「客製」是操作：同一工作台可調整既有元件樣式，也可組合並匯出 atomic、composite、template 三層 JSON 定義。

頁面內的「元件組合使用說明」會直接列出四步流程、三層自動分類條件、`custom_components/definitions/{registry-name}.json` 交付路徑，以及 build/check/test 指令；「元件導覽與展示」也有用途說明與前往組合器的同頁連結。這些說明與連結本身同樣定義在 `studio.page.json`，由正式 `Heading`、`Text`、`Alert`、`StepIndicator`、`CodeBlock`、`Link` 等元件產生，不是旁路手寫 UI。

自舉驗收不只檢查畫面存在，也會驗證 definition URL/SHA-256、同一 renderer、切頁與 state 更新不重建 workspace DOM、所有 chrome controls 的 instance provenance、Theme 與客製元件 JSON 的實際下載／上傳 round-trip，以及 CSP/SVG hard-zero。執行 `npm run test:studio:self-host` 與 `npm run test:studio:browser`。

## 安全邊界

- JSON 只允許純資料，事件 callback 必須由執行期 `nodeOptions` 注入，不得寫入 JSON。

- 禁止 `innerHTML`、`rawHtml`、`srcdoc` 等 raw HTML option；執行期 `nodeOptions` 也會遞迴且不分大小寫地剔除同類鍵。

- 禁止 `__proto__`、`prototype`、`constructor` 等 prototype-sensitive key。

- manifest entry 必須是資料夾內的安全相對 `.json` 路徑，不接受 absolute URL 或 `..` traversal。

- layout 只會映射到既定 CSS class，樣式使用 `var(--cl-*)` token。
