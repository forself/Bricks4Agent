# BaseOrm

`BaseOrm` is a micro ORM for Bricks4Agent.

It is closer to `Dapper + a small convention layer` than to a full ORM like EF Core:

- You write SQL explicitly.
- It maps rows to objects.
- It provides attribute-driven CRUD helpers.
- It can create simple tables from model metadata.
- It does not provide LINQ, change tracking, or migrations.

## Layout

| Path | Target | Notes |
| --- | --- | --- |
| `net8/` | .NET 8+ | Canonical implementation, async API, shared source used by the repo |
| `netfx48/` | .NET Framework 4.8 | Legacy variant kept for older systems |

## Usage Modes

### 1. Project/package mode

Use this when you reference `packages/csharp/database/BaseOrm/net8/BaseOrm.csproj` from another repo project.

In this mode the package already references the default ADO.NET providers for:

- `Microsoft.Data.Sqlite`
- `Microsoft.Data.SqlClient`
- `MySqlConnector`
- `Npgsql`

That means `BaseDb.UseSqlServer(...)`, `BaseDb.UseMySql(...)`, and `BaseDb.UsePostgreSql(...)` work without adding extra provider references in the consuming project.

### 2. Single-file copy mode

Use this when you copy `net8/BaseOrm.cs` into a standalone project.

In this mode you still need to add the provider packages yourself for the databases you want to use. The API remains the same, but package references are now the consumer's responsibility.

## Supported Databases

| Database | Factory entry point | Default provider in `net8` project mode |
| --- | --- | --- |
| SQLite | `new BaseDb(connStr)` or `BaseDb.UseSqlite(connStr)` | Yes |
| SQL Server | `BaseDb.UseSqlServer(connStr)` | Yes |
| MySQL | `BaseDb.UseMySql(connStr)` | Yes |
| PostgreSQL | `BaseDb.UsePostgreSql(connStr)` | Yes |

## Verification

The repository includes an executable verification project for provider resolution, dialect behavior, and async CRUD:

```bash
dotnet run --project packages/csharp/database/BaseOrm/net8/verify/BaseOrm.Verify.csproj
```

If these environment variables are set, the verifier also runs live CRUD integrations against temporary databases:

- `BASEORM_SQLSERVER_CONNECTION_STRING`
- `BASEORM_MYSQL_CONNECTION_STRING`
- `BASEORM_POSTGRESQL_CONNECTION_STRING`

From the repo root you can also run:

```bash
npm.cmd run validate:baseorm
```

## Quick Start

```csharp
using BaseOrm;

await using var db = BaseDb.UseSqlite("Data Source=app.db");
await db.EnsureTableAsync<User>();

var id = await db.InsertAsync(new User
{
    Name = "Ada",
    Email = "ada@example.com",
    CreatedAt = DateTime.UtcNow
});

var user = await db.GetAsync<User>(id);
```

## Model Example

```csharp
using BaseOrm;

[Table("Users")]
public class User
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Column("EmailAddress")]
    [MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    [Ignore]
    public string TemporaryValue { get; set; } = string.Empty;
}
```

## Core API

### Query

```csharp
var users = await db.QueryAsync<User>(
    "SELECT * FROM Users WHERE Name LIKE @Name ORDER BY Id",
    new { Name = "%Ada%" });

var first = await db.QueryFirstAsync<User>(
    "SELECT * FROM Users WHERE Id = @Id",
    new { Id = 1 });

var count = await db.ScalarAsync<int>("SELECT COUNT(*) FROM Users");
```

### CRUD

```csharp
var id = await db.InsertAsync(new User
{
    Name = "Ada",
    Email = "ada@example.com",
    CreatedAt = DateTime.UtcNow
});

var user = await db.GetAsync<User>(id);
user!.Email = "ada.lovelace@example.com";
await db.UpdateAsync(user);

await db.DeleteAsync<User>(id);
```

### Paging

```csharp
var page = await db.QueryPagedAsync<User>(
    "SELECT * FROM Users ORDER BY Id",
    page: 1,
    pageSize: 20);

Console.WriteLine(page.TotalCount);
Console.WriteLine(page.HasNext);
```

### Transactions

```csharp
await db.InTransactionAsync(async () =>
{
    await db.InsertAsync(new User
    {
        Name = "Ada",
        Email = "ada@example.com",
        CreatedAt = DateTime.UtcNow
    });

    await db.ExecuteAsync(
        "UPDATE Settings SET Value = @Value WHERE Name = @Name",
        new { Value = "ready", Name = "State" });
});
```

## Attributes

| Attribute | Purpose |
| --- | --- |
| `[Table("Users")]` | Override table name |
| `[Key]` | Mark primary key |
| `[Key(AutoIncrement = false)]` | Mark non-identity key |
| `[Column("email_address")]` | Override column name |
| `[Required]` | Emit `NOT NULL` in generated schema |
| `[MaxLength(100)]` | Control generated string column width |
| `[Ignore]` | Exclude property from mapping and schema |

## Database Notes

- Parameter names are written as `@Name` in user SQL. `BaseOrm` rewrites them for MySQL when needed.
- Identifier quoting is dialect-specific and guarded by an allowlist, so only letters, digits, and `_` are accepted.
- SQLite connections enable `busy_timeout`, `foreign_keys`, and WAL mode for file-backed databases.
- `EnsureTable` / `EnsureTableAsync` are for simple bootstrap tables, not migration workflows.

## Limitations

`BaseOrm` is intentionally small. It does not attempt to replace EF Core.

- No LINQ provider
- No change tracking
- No migrations
- No relation graph loading
- No expression-based query builder

If you need deterministic SQL with lightweight mapping, this package fits. If you need full ORM behavior, use EF Core instead.
