# BatchUploader 批次上傳元件

批次檔案上傳元件，支援拖放上傳、進度追蹤、檔案驗證、重試機制、順序/並行上傳模式。

## API

### Constructor

```javascript
import { BatchUploader } from './BatchUploader.js';

const uploader = new BatchUploader({
    container: '#upload-area',       // 容器（selector 或 DOM），必填
    apiEndpoint: '/api/files/upload', // 上傳 API 端點
    ruleId: null,                    // 上傳規則 ID
    tableName: null,                 // 關聯資料表名稱
    tablePk: null,                   // 資料表主鍵
    identify: null,                  // 自訂識別碼
    maxFiles: 10,                    // 最大檔案數
    maxFileSize: 10485760,           // 單檔上限（bytes，預設 10MB）
    allowedExtensions: null,         // 允許副檔名（如 ['.jpg','.png']），null=全部
    autoUpload: false,               // 加入檔案後自動上傳
    multiple: true,                  // 允許多檔選擇
    uploadMode: 'sequential',        // 'sequential' | 'parallel'
    headers: {},                     // 自訂 HTTP headers
    onFileAdded: (fileItem) => {},   // 檔案加入回調
    onFileRemoved: (fileItem) => {}, // 檔案移除回調
    onProgress: (fileItem, pct) => {},// 進度回調
    onFileComplete: (fileItem, result) => {}, // 單檔完成回調
    onComplete: ({total, success, failed, files}) => {}, // 全部完成回調
    onError: (fileItem, error) => {},// 錯誤回調
    labels: { ... }                  // 自訂 UI 文字
});
```

### 方法

| 方法 | 說明 |
|------|------|
| `upload()` | 開始上傳所有待傳檔案（async） |
| `removeFile(fileId)` | 移除指定檔案 |
| `clear()` | 清除所有檔案 |
| `getFiles()` | 取得所有檔案陣列 |
| `getPendingFiles()` | 取得待傳檔案 |
| `getUploadedFiles()` | 取得已上傳檔案 |
| `getFailedFiles()` | 取得失敗檔案 |
| `setOptions(obj)` | 動態更新設定 |
| `destroy()` | 銷毀元件 |

### fileItem 結構

```javascript
{
    id: 'file_xxx',    // 唯一 ID
    file: File,        // 原始 File 物件
    name: 'photo.jpg', // 檔名
    size: 12345,       // bytes
    type: 'image/jpeg',// MIME type
    status: 'pending', // 'pending' | 'uploading' | 'success' | 'error'
    progress: 0,       // 0-100
    error: null,       // 錯誤訊息
    result: null       // 上傳結果（API 回應）
}
```

### 屬性

- `element` — 根 DOM 元素
- `files` — 檔案陣列
- `isUploading` — 是否上傳中

## 使用範例

```javascript
import { BatchUploader } from './BatchUploader.js';

const uploader = new BatchUploader({
    container: '#upload-area',
    apiEndpoint: '/api/upload',
    maxFiles: 5,
    maxFileSize: 5 * 1024 * 1024,
    allowedExtensions: ['.jpg', '.png', '.pdf'],
    onComplete: (result) => console.log(`成功 ${result.success}/${result.total}`)
});
```

## Demo

`demo.html`（同目錄）
