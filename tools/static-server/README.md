# Static Server (C#)

極簡靜態檔案伺服器，使用純 .NET HttpListener 實作，無需額外依賴。

## 功能特色

- 零依賴 - 僅使用 .NET 內建函式庫
- SPA 支援 - 自動 fallback 到 index.html
- CORS 支援 - 開發環境跨域請求
- 安全標頭 - X-Content-Type-Options, X-Frame-Options
- 目錄遍歷防護

## 使用方式

### 方式一：直接執行

```bash
cd tools/static-server
dotnet run ../spa-generator/frontend 3000
```

### 方式二：編譯為執行檔

```bash
# 編譯
dotnet publish -c Release -o ./dist

# 執行
./dist/serve ./frontend 3000
```

### 方式三：編譯為獨立執行檔 (不需安裝 .NET)

```bash
# Windows
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./dist

# Linux
dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o ./dist

# macOS
dotnet publish -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true -o ./dist
```

## 參數

```
serve [目錄] [埠號]
```

| 參數 | 預設值 | 說明 |
|------|--------|------|
| 目錄 | `.` | 靜態檔案根目錄 |
| 埠號 | `3000` | HTTP 監聽埠號 |

## 範例

```bash
# 預設：當前目錄、埠號 3000
serve

# 指定目錄
serve ./public

# 指定目錄和埠號
serve ./frontend 8080
```

## MIME 類型支援

| 副檔名 | MIME 類型 |
|--------|-----------|
| .html | text/html |
| .css | text/css |
| .js | application/javascript |
| .json | application/json |
| .png | image/png |
| .jpg, .jpeg | image/jpeg |
| .gif | image/gif |
| .svg | image/svg+xml |
| .ico | image/x-icon |
| .woff, .woff2 | font/woff, font/woff2 |
| .ttf | font/ttf |
| .pdf | application/pdf |
| 其他 | application/octet-stream |

## 與其他方案比較

| 方案 | 需求 | 優點 | 缺點 |
|------|------|------|------|
| **Static Server (C#)** | .NET 8 | 無依賴、可編譯為單檔 | 需要 .NET |
| npx serve | Node.js | 方便、功能豐富 | 需要 Node.js |
| python -m http.server | Python | 內建 | 無 SPA 支援 |
| Live Server (VS Code) | VS Code | 熱重載 | 需要 IDE |
