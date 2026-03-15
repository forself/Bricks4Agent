using System.Reflection;
using BaseOrm;
using Microsoft.Data.SqlClient;

var failures = new List<string>();

try
{
    VerifyProviderResolution();
    VerifyDialectSurface();
    await VerifyAsyncCrudAsync();
    await VerifySqlServerIntegrationIfConfiguredAsync();
}
catch (Exception ex)
{
    failures.Add(ex.ToString());
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("BaseOrm verification failed.");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    Environment.ExitCode = 1;
    return;
}

Console.WriteLine("BaseOrm verification passed.");

static void VerifyProviderResolution()
{
    using var sqlServer = BaseDb.UseSqlServer("Server=localhost;Database=baseorm_verify;Trusted_Connection=True;TrustServerCertificate=True");
    using var mySql = BaseDb.UseMySql("Server=localhost;Database=baseorm_verify;User Id=root;Password=test;");
    using var postgreSql = BaseDb.UsePostgreSql("Host=localhost;Database=baseorm_verify;Username=postgres;Password=test");

    Assert(sqlServer.DatabaseType == DbType.SqlServer, "SQL Server provider should resolve.");
    Assert(mySql.DatabaseType == DbType.MySql, "MySQL provider should resolve.");
    Assert(postgreSql.DatabaseType == DbType.PostgreSql, "PostgreSQL provider should resolve.");
}

static void VerifyDialectSurface()
{
    using var sqlServer = BaseDb.UseSqlServer("Server=localhost;Database=baseorm_verify;Trusted_Connection=True;TrustServerCertificate=True");
    using var mySql = BaseDb.UseMySql("Server=localhost;Database=baseorm_verify;User Id=root;Password=test;");
    using var postgreSql = BaseDb.UsePostgreSql("Host=localhost;Database=baseorm_verify;Username=postgres;Password=test");

    var normalizedMySql = InvokeNonPublic<string>(mySql, "NormalizeParameterNames", "SELECT * FROM Users WHERE Id = @Id");
    var sqlServerPaged = InvokeNonPublic<string>(sqlServer, "BuildPagedSql", "SELECT * FROM Users ORDER BY Id");
    var sqlServerCount = InvokeNonPublic<string>(sqlServer, "BuildCountSql", "SELECT * FROM Users ORDER BY Id");
    var quotedPostgreSql = InvokeNonPublic<string>(postgreSql, "QuoteIdentifier", "Users");

    Assert(normalizedMySql == "SELECT * FROM Users WHERE Id = ?Id", "MySQL parameters should normalize to '?'.");
    Assert(sqlServerPaged == "SELECT * FROM Users ORDER BY Id OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
        "SQL Server paging SQL should use OFFSET/FETCH.");
    Assert(sqlServerCount == "SELECT COUNT(*) FROM (SELECT * FROM Users) AS CountQuery",
        "SQL Server count SQL should strip the trailing ORDER BY.");
    Assert(quotedPostgreSql == "\"Users\"", "PostgreSQL identifiers should use double quotes.");
}

static async Task VerifyAsyncCrudAsync()
{
    var databasePath = Path.Combine(Path.GetTempPath(), $"baseorm-verify-{Guid.NewGuid():N}.db");

    try
    {
        await using (var db = BaseDb.UseSqlite($"Data Source={databasePath}"))
        {
            await RunWidgetCrudFlowAsync(db, "SQLite");
        }
    }
    finally
    {
        DeleteIfExists(databasePath);
        DeleteIfExists(databasePath + "-wal");
        DeleteIfExists(databasePath + "-shm");
    }
}

static async Task VerifySqlServerIntegrationIfConfiguredAsync()
{
    var connectionString = Environment.GetEnvironmentVariable("BASEORM_SQLSERVER_CONNECTION_STRING");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.WriteLine("Live SQL Server integration test skipped. Set BASEORM_SQLSERVER_CONNECTION_STRING to enable.");
        return;
    }

    var databaseName = $"BaseOrmVerify_{Guid.NewGuid():N}";
    var masterBuilder = new SqlConnectionStringBuilder(connectionString)
    {
        InitialCatalog = "master",
        TrustServerCertificate = true
    };
    var testBuilder = new SqlConnectionStringBuilder(masterBuilder.ConnectionString)
    {
        InitialCatalog = databaseName
    };

    await using var masterDb = BaseDb.UseSqlServer(masterBuilder.ConnectionString);
    await masterDb.ExecuteAsync($"CREATE DATABASE [{databaseName}]");

    try
    {
        await using var sqlServerDb = BaseDb.UseSqlServer(testBuilder.ConnectionString);
        await RunWidgetCrudFlowAsync(sqlServerDb, "SQL Server");
    }
    finally
    {
        await masterDb.ExecuteAsync(
            $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END");
    }
}

static async Task RunWidgetCrudFlowAsync(BaseDb db, string label)
{
    await db.EnsureTableAsync<Widget>();

    var createdAt = DateTime.UtcNow;
    var firstId = await db.InsertAsync(new Widget
    {
        Name = "First",
        IsActive = true,
        CreatedAt = createdAt
    });

    Assert(firstId > 0, $"{label}: InsertAsync should return the generated id.");

    var loaded = await db.GetAsync<Widget>(firstId);
    Assert(loaded != null, $"{label}: GetAsync should load the inserted row.");
    Assert(loaded!.Name == "First", $"{label}: loaded row should preserve mapped values.");

    loaded.Name = "Updated";
    await db.UpdateAsync(loaded);

    var updated = await db.QueryFirstAsync<Widget>(
        "SELECT * FROM Widgets WHERE Id = @Id",
        new { Id = firstId });
    Assert(updated?.Name == "Updated", $"{label}: UpdateAsync should persist changes.");

    await db.InTransactionAsync(async () =>
    {
        await db.InsertAsync(new Widget
        {
            Name = "Second",
            IsActive = false,
            CreatedAt = createdAt.AddMinutes(1)
        });

        await db.InsertAsync(new Widget
        {
            Name = "Third",
            IsActive = true,
            CreatedAt = createdAt.AddMinutes(2)
        });
    });

    var countAfterCommit = await db.ScalarAsync<int>("SELECT COUNT(*) FROM Widgets");
    Assert(countAfterCommit == 3, $"{label}: InTransactionAsync should commit all writes.");

    try
    {
        await db.InTransactionAsync(async () =>
        {
            await db.InsertAsync(new Widget
            {
                Name = "RolledBack",
                IsActive = true,
                CreatedAt = createdAt.AddMinutes(3)
            });

            throw new InvalidOperationException("rollback");
        });
    }
    catch (InvalidOperationException)
    {
    }

    var countAfterRollback = await db.ScalarAsync<int>("SELECT COUNT(*) FROM Widgets");
    Assert(countAfterRollback == 3, $"{label}: failed async transaction should rollback.");

    var paged = await db.QueryPagedAsync<Widget>(
        "SELECT * FROM Widgets ORDER BY Id",
        page: 1,
        pageSize: 2);
    Assert(paged.TotalCount == 3, $"{label}: QueryPagedAsync should calculate total count.");
    Assert(paged.Items.Count == 2, $"{label}: QueryPagedAsync should respect page size.");
    Assert(paged.HasNext, $"{label}: QueryPagedAsync should expose next-page metadata.");

    var dictionary = await db.QueryDictionaryAsync<int, Widget>(
        "SELECT * FROM Widgets ORDER BY Id",
        widget => widget.Id);
    Assert(dictionary.Count == 3, $"{label}: QueryDictionaryAsync should materialize keyed results.");

    await db.DeleteAsync<Widget>(firstId);
    var finalCount = await db.ScalarAsync<int>("SELECT COUNT(*) FROM Widgets");
    Assert(finalCount == 2, $"{label}: DeleteAsync should remove the target row.");
}

static T InvokeNonPublic<T>(object instance, string methodName, params object[] args)
{
    var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Method not found: {methodName}");
    var result = method.Invoke(instance, args);
    return (T)result!;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void DeleteIfExists(string path)
{
    if (!File.Exists(path))
    {
        return;
    }

    for (var attempt = 0; attempt < 10; attempt++)
    {
        try
        {
            File.Delete(path);
            return;
        }
        catch (IOException) when (attempt < 9)
        {
            Thread.Sleep(100);
        }
        catch (UnauthorizedAccessException) when (attempt < 9)
        {
            Thread.Sleep(100);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
    }
}

[Table("Widgets")]
internal sealed class Widget
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
}
