# BaseOrm

極簡 ORM 元件，類似 Dapper 的輕量級實作。支援多種主流資料庫。

## 版本選擇

本元件提供兩個版本，依據您的目標框架選擇：

| 目錄 | 目標框架 | 適用場景 |
|------|----------|----------|
| `net8/` | .NET 8+ | 新專案、現代化應用 |
| `netfx48/` | .NET Framework 4.8 | 舊專案維護、企業內部系統 |

### .NET 8+ 版本 (`net8/`)

```bash
# 複製到專案
cp net8/BaseOrm.cs YourProject/
```

特色：
- 使用 `Microsoft.Data.SqlClient`
- 支援 C# 10+ 語法
- Nullable reference types

### .NET Framework 4.8 版本 (`netfx48/`)

```bash
# 複製到專案
cp netfx48/BaseOrm.cs YourProject/
```

特色：
- 使用 `System.Data.SqlClient` (無需額外安裝)
- C# 7.3 相容語法
- **支援舊版 Attribute 系統** (見下方說明)

#### 舊版 Attribute 相容性

`netfx48` 版本透過 `LegacyAttributeAdapter` 支援既有的 Attribute 定義：

| 功能 | 新版 Attribute | 舊版 Attribute (相容) |
|------|---------------|----------------------|
| 主鍵 | `[Key]` | `[Data("PK")]` |
| 忽略 | `[Ignore]` | `[Data("None")]` |
| 欄位名稱 | `[Column("col")]` | `[DBColName("col")]` |

這表示您可以**直接使用既有的 DTO**，無需修改 Attribute：

```csharp
// 既有的 DTO 定義
public class MemberDTO
{
    [Data("PK")]              // 自動識別為主鍵
    public string Member_SN { get; set; }

    [DBColName("MEMBER_NAME")] // 自動對應欄位名稱
    public string Name { get; set; }

    [Data("None")]            // 自動忽略
    public string TempField { get; set; }
}

// 直接使用
var member = db.Get<MemberDTO>("123");
```

---

## 支援的資料庫

| 資料庫 | NuGet 套件 | 工廠方法 |
|--------|-----------|----------|
| SQLite | Microsoft.Data.Sqlite | `new BaseDb(connStr)` 或 `BaseDb.UseSqlite(connStr)` |
| SQL Server | Microsoft.Data.SqlClient / System.Data.SqlClient | `BaseDb.UseSqlServer(connStr)` |
| MySQL | MySqlConnector 或 MySql.Data | `BaseDb.UseMySql(connStr)` |
| PostgreSQL | Npgsql | `BaseDb.UsePostgreSql(connStr)` |

## 特色

- **極簡依賴** - 僅依賴 ADO.NET Provider
- **多資料庫** - 統一 API 支援 SQLite、SQL Server、MySQL、PostgreSQL
- **零設定** - 自動物件映射，無需配置
- **高效能** - 使用快取的反射元資料
- **直覺 API** - 類似 Dapper 的使用方式
- **完整功能** - CRUD、交易、分頁、Schema 建立

## 安裝

依據使用的資料庫安裝對應套件：

```bash
# SQLite (預設)
dotnet add package Microsoft.Data.Sqlite

# SQL Server (.NET 8+)
dotnet add package Microsoft.Data.SqlClient

# SQL Server (.NET Framework 4.8) - 無需安裝，已內建

# MySQL
dotnet add package MySqlConnector

# PostgreSQL
dotnet add package Npgsql
```

將 `BaseOrm.cs` 加入專案即可使用。

## 快速開始

### 建立連線

```csharp
using BaseOrm;

// SQLite (預設)
using var db = new BaseDb("Data Source=app.db");

// SQL Server
using var db = BaseDb.UseSqlServer("Server=.;Database=MyApp;Trusted_Connection=True;TrustServerCertificate=True");

// MySQL
using var db = BaseDb.UseMySql("Server=localhost;Database=myapp;User=root;Password=xxx;");

// PostgreSQL
using var db = BaseDb.UsePostgreSql("Host=localhost;Database=myapp;Username=postgres;Password=xxx");
```

### 定義實體

```csharp
using BaseOrm;

[Table("Users")]
public class User
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = "";

    [Column("EmailAddress")]
    [MaxLength(200)]
    public string Email { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    [Ignore]
    public string TempData { get; set; } = "";
}
```

### 基本操作

```csharp
// 確保資料表存在 (依據實體自動建立) - 僅 net8 版本支援
db.EnsureTable<User>();

// 新增
var id = db.Insert(new User
{
    Name = "John",
    Email = "john@example.com",
    CreatedAt = DateTime.Now
});

// 查詢單筆
var user = db.Get<User>(id);

// 查詢多筆
var users = db.Query<User>(
    "SELECT * FROM Users WHERE Name LIKE @Name",
    new { Name = "%John%" }
);

// 更新
user.Email = "john.doe@example.com";
db.Update(user);

// 刪除
db.Delete<User>(id);
```

### 原生 SQL 查詢

```csharp
// 查詢多筆
var activeUsers = db.Query<User>(
    "SELECT * FROM Users WHERE IsActive = @Active ORDER BY CreatedAt DESC",
    new { Active = true }
);

// 查詢單筆
var admin = db.QueryFirst<User>(
    "SELECT * FROM Users WHERE Role = @Role",
    new { Role = "Admin" }
);

// 純量值
var count = db.Scalar<int>("SELECT COUNT(*) FROM Users");

// 執行命令
var affected = db.Execute(
    "UPDATE Users SET LastLogin = @Now WHERE Id = @Id",
    new { Now = DateTime.Now, Id = 1 }
);
```

### 交易

```csharp
// 方式一：手動控制
db.BeginTransaction();
try
{
    db.Insert(new User { Name = "User1" });
    db.Insert(new User { Name = "User2" });
    db.Commit();
}
catch
{
    db.Rollback();
    throw;
}

// 方式二：自動處理
db.InTransaction(() =>
{
    db.Insert(new User { Name = "User1" });
    db.Insert(new User { Name = "User2" });
});

// 方式三：有回傳值
var newId = db.InTransaction(() =>
{
    var id = db.Insert(new User { Name = "User1" });
    db.Execute("INSERT INTO Logs ...");
    return id;
});
```

### 分頁查詢

```csharp
var result = db.QueryPaged<User>(
    "SELECT * FROM Users WHERE IsActive = 1 ORDER BY Id",
    page: 1,
    pageSize: 10
);

Console.WriteLine($"第 {result.Page} 頁，共 {result.TotalPages} 頁");
Console.WriteLine($"總筆數: {result.TotalCount}");

foreach (var user in result.Items)
{
    Console.WriteLine(user.Name);
}

if (result.HasNext)
{
    // 載入下一頁...
}
```

## 屬性說明

| 屬性 | 用途 | 範例 |
|------|------|------|
| `[Table("name")]` | 指定資料表名稱 | `[Table("Users")]` |
| `[Key]` | 標記主鍵 | `[Key] public int Id { get; set; }` |
| `[Column("name")]` | 指定欄位名稱 | `[Column("email_address")]` |
| `[Ignore]` | 忽略此屬性 | `[Ignore] public string Temp { get; set; }` |
| `[Required]` | 必填欄位 (NOT NULL) | `[Required] public string Name { get; set; }` |
| `[MaxLength(n)]` | 欄位長度 | `[MaxLength(100)] public string Name { get; set; }` |

### Key 屬性選項

```csharp
// 自動遞增 (預設)
[Key]
public int Id { get; set; }

// 非自動遞增 (如 GUID)
[Key(AutoIncrement = false)]
public Guid Id { get; set; }
```

## 預設行為

- 若無 `[Table]`，資料表名稱為 `類別名 + s`
- 若無 `[Key]`，自動尋找 `Id` 或 `{類別名}Id` 屬性
- 若無 `[Column]`，欄位名稱與屬性名稱相同

## 資料類型對應

| C# 類型 | SQLite | SQL Server | MySQL | PostgreSQL |
|---------|--------|------------|-------|------------|
| int | INTEGER | INT | INT | INT |
| long | INTEGER | BIGINT | BIGINT | BIGINT |
| bool | INTEGER | BIT | TINYINT(1) | BOOLEAN |
| float | REAL | REAL | REAL | REAL |
| double | REAL | FLOAT | FLOAT | DOUBLE PRECISION |
| decimal | REAL | DECIMAL(18,2) | DECIMAL(18,2) | DECIMAL(18,2) |
| string | TEXT | NVARCHAR | VARCHAR | VARCHAR/TEXT |
| DateTime | TEXT | DATETIME2 | DATETIME | TIMESTAMP |
| Guid | TEXT | UNIQUEIDENTIFIER | CHAR(36) | UUID |
| byte[] | BLOB | VARBINARY(MAX) | LONGBLOB | BYTEA |

## 與 EF Core / Dapper 比較

| 功能 | BaseOrm | Dapper | EF Core |
|------|---------|--------|---------|
| 套件大小 | ~25KB | ~200KB | ~5MB |
| 依賴數量 | 1 | 1 | 10+ |
| 啟動時間 | 快 | 快 | 慢 |
| 遷移工具 | 無 | 無 | 有 |
| LINQ 查詢 | 無 | 無 | 有 |
| 變更追蹤 | 無 | 無 | 有 |
| 關聯載入 | 手動 | 手動 | 自動 |
| SQL 控制 | 完全 | 完全 | 部分 |
| 多資料庫 | 有 | 有 | 有 |
| CRUD 內建 | 有 | 需擴充 | 有 |

## 進階用法

### 批次操作

```csharp
db.InTransaction(() =>
{
    foreach (var user in users)
    {
        db.Insert(user);
    }
});
```

### 複雜查詢

```csharp
var result = db.Query<UserStats>(@"
    SELECT
        u.Id,
        u.Name,
        COUNT(o.Id) as OrderCount,
        SUM(o.Amount) as TotalAmount
    FROM Users u
    LEFT JOIN Orders o ON o.UserId = u.Id
    WHERE u.IsActive = @Active
    GROUP BY u.Id, u.Name
    HAVING COUNT(o.Id) > @MinOrders
", new { Active = true, MinOrders = 5 });
```

### 取得資料庫類型

```csharp
var db = BaseDb.UseSqlServer(connStr);
Console.WriteLine(db.DbType); // SqlServer
```

### 連線字串範例

```csharp
// SQLite
"Data Source=app.db"
"Data Source=:memory:"  // 記憶體資料庫

// SQL Server
"Server=.;Database=MyApp;Trusted_Connection=True;TrustServerCertificate=True"
"Server=tcp:myserver.database.windows.net,1433;Database=MyApp;User Id=user;Password=xxx;"

// MySQL
"Server=localhost;Database=myapp;User=root;Password=xxx;Port=3306"

// PostgreSQL
"Host=localhost;Database=myapp;Username=postgres;Password=xxx;Port=5432"
```

## 遷移指南：從手動 SQL 到 BaseOrm

### 之前 (手動 SQL)

```csharp
// 容易有 SQL 注入風險
string sql = "SELECT * FROM Members WHERE Name = '" + name + "'";
var dt = SqlTool.GetSqlDt(sql);
var member = Repository.DtRow2Dto<MemberDTO>(dt.Rows[0]);
```

### 之後 (BaseOrm)

```csharp
// 自動參數化，安全
var member = db.QueryFirst<MemberDTO>(
    "SELECT * FROM Members WHERE Name = @Name",
    new { Name = name }
);
```

## 注意事項

1. **參數化查詢**: 所有參數都使用 `@ParamName` 格式，BaseOrm 會自動轉換為對應資料庫格式
2. **識別符號**: 保留字會自動加上引號 (`[]` for SQL Server, `` ` `` for MySQL, `""` for PostgreSQL)
3. **分頁語法**: 自動使用對應資料庫的分頁語法 (LIMIT/OFFSET 或 OFFSET/FETCH)
4. **自動遞增 ID**: 每個資料庫使用對應的語法 (AUTOINCREMENT, IDENTITY, AUTO_INCREMENT, SERIAL)
