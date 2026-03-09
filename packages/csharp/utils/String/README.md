# String

字串擴充方法工具類別 — 提供空值檢查、大小寫轉換、截斷、遮罩、驗證、擷取、比較、雜湊、編碼等常用字串操作。

## 初始化方式

```csharp
using YourNamespace.Utils.String;
// 靜態類別，直接以擴充方法呼叫
```

## API 列表

### 空值檢查

| 方法 | 回傳 | 說明 |
|------|------|------|
| `.IsNullOrEmpty()` | `bool` | 是否為 null 或空字串 |
| `.IsNullOrWhiteSpace()` | `bool` | 是否為 null、空或僅空白 |
| `.HasValue()` | `bool` | 是否有值（非空白） |
| `.OrDefault(defaultValue)` | `string` | 若為空則回傳預設值 |

### 大小寫轉換

| 方法 | 範例輸入 | 範例輸出 |
|------|----------|----------|
| `.ToTitleCase()` | `"hello world"` | `"Hello World"` |
| `.ToCamelCase()` | `"HelloWorld"` | `"helloWorld"` |
| `.ToPascalCase()` | `"helloWorld"` | `"HelloWorld"` |
| `.ToSnakeCase()` | `"HelloWorld"` | `"hello_world"` |
| `.ToKebabCase()` | `"HelloWorld"` | `"hello-world"` |

### 裁剪與截斷

| 方法 | 參數 | 說明 |
|------|------|------|
| `.TrimAndReduce()` | - | 去頭尾空白並將連續空白合併為一個 |
| `.RemoveWhitespace()` | - | 移除所有空白字元 |
| `.Truncate(maxLength, suffix)` | `int, string="..."` | 截斷至指定長度 |
| `.TruncateAtWord(maxLength, suffix)` | `int, string="..."` | 在單字邊界截斷 |

### 遮罩

| 方法 | 參數 | 範例 |
|------|------|------|
| `.Mask(visibleChars, maskChar)` | `int=4, char='*'` | `"1234567890"` → `"******7890"` |
| `.MaskEmail()` | - | `"user@example.com"` → `"u**r@example.com"` |

### 驗證

| 方法 | 回傳 | 說明 |
|------|------|------|
| `.IsValidEmail()` | `bool` | 是否為有效 Email（長度 <= 254） |
| `.IsValidUrl()` | `bool` | 是否為有效 URL（http/https） |
| `.IsNumeric()` | `bool` | 是否僅含數字 |
| `.IsAlphabetic()` | `bool` | 是否僅含字母 |
| `.IsAlphanumeric()` | `bool` | 是否僅含字母與數字 |

### 擷取

| 方法 | 參數 | 說明 |
|------|------|------|
| `.ExtractNumbers()` | - | 擷取所有數字字元 |
| `.ExtractLetters()` | - | 擷取所有字母字元 |
| `.Left(length)` | `int` | 取前 N 個字元 |
| `.Right(length)` | `int` | 取後 N 個字元 |

### 比較（不區分大小寫）

| 方法 | 參數 | 說明 |
|------|------|------|
| `.EqualsIgnoreCase(other)` | `string` | 忽略大小寫比較 |
| `.ContainsIgnoreCase(value)` | `string` | 忽略大小寫包含 |
| `.StartsWithIgnoreCase(value)` | `string` | 忽略大小寫開頭比對 |
| `.EndsWithIgnoreCase(value)` | `string` | 忽略大小寫結尾比對 |

### 雜湊

| 方法 | 回傳 | 說明 |
|------|------|------|
| `.ToMD5()` | `string` | MD5 雜湊（已標記 `[Obsolete]`，僅相容用途） |
| `.ToSHA256()` | `string` | SHA256 雜湊（推薦） |

### 編碼

| 方法 | 說明 |
|------|------|
| `.ToBase64()` | UTF-8 轉 Base64 |
| `.FromBase64()` | Base64 轉 UTF-8（失敗時回傳原字串） |

### 其他

| 方法 | 參數 | 說明 |
|------|------|------|
| `.Pluralize(count)` | `int` | 簡易英文複數化 |
| `.Reverse()` | - | 反轉字串 |
| `.Repeat(count)` | `int` | 重複字串 N 次 |
| `.WordCount()` | - | 計算單字數 |

## 使用範例

```csharp
using YourNamespace.Utils.String;

// 空值安全
string name = null;
string display = name.OrDefault("未知");  // "未知"

// 大小寫轉換
"UserName".ToSnakeCase();   // "user_name"
"UserName".ToKebabCase();   // "user-name"
"hello".ToPascalCase();     // "Hello"

// 截斷
"這是一段很長的文字描述".Truncate(6);  // "這是一段很長..."

// 遮罩敏感資料
"A123456789".Mask();                // "******6789"
"user@example.com".MaskEmail();     // "u**r@example.com"

// 驗證
"test@example.com".IsValidEmail();  // true
"https://google.com".IsValidUrl();  // true
"12345".IsNumeric();                // true

// 雜湊
"password".ToSHA256();  // "5e884898da28047151d0e56f..."

// 編碼
"Hello".ToBase64();     // "SGVsbG8="
"SGVsbG8=".FromBase64();  // "Hello"
```

## 依賴清單

| 依賴 | 說明 |
|------|------|
| `System` | .NET 基礎命名空間 |
| `System.Globalization` | TitleCase 轉換 |
| `System.Linq` | 字元篩選 |
| `System.Security.Cryptography` | MD5 / SHA256 雜湊 |
| `System.Text` | StringBuilder / Encoding |
| `System.Text.RegularExpressions` | Regex（含 100ms 超時防 ReDoS） |

無第三方依賴。
