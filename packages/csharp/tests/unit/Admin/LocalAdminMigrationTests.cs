using BrokerCore.Data;
using BrokerCore.Models;
using FluentAssertions;

namespace Unit.Tests.Admin;

public class LocalAdminMigrationTests
{
    [Fact]
    public void Initializer_AddsOperatorColumns()
    {
        var path = TempDbPath("broker_admin_migration");
        try
        {
            using var db = new BrokerDb($"Data Source={path}");
            new BrokerDbInitializer(db).Initialize();

            var columns = db.Query<ColumnInfo>("PRAGMA table_info(local_admin_credentials)")
                .Select(c => c.Name)
                .ToArray();

            columns.Should().Contain("operator_id");
            columns.Should().Contain("username");
            columns.Should().Contain("display_name");
            columns.Should().Contain("role");
            columns.Should().Contain("permission_overrides");
            columns.Should().Contain("status");
            columns.Should().Contain("last_login_at");
        }
        finally
        {
            DeleteSqliteFiles(path);
        }
    }

    [Fact]
    public void Initializer_NormalizesLegacyLocalAdminCredential()
    {
        var path = TempDbPath("broker_legacy_admin");
        try
        {
            using var db = new BrokerDb($"Data Source={path}");
            db.EnsureTable<LocalAdminCredential>();
            db.Insert(new LocalAdminCredential
            {
                CredentialId = "local_admin",
                PasswordHash = "hash",
                PasswordSalt = "salt",
                HashIterations = 120000,
                MustChangePassword = false
            });

            new BrokerDbInitializer(db).Initialize();

            var credential = db.Get<LocalAdminCredential>("local_admin");
            credential.Should().NotBeNull();
            credential!.OperatorId.Should().Be("local_admin");
            credential.Username.Should().Be("admin");
            credential.Role.Should().Be("super_admin");
            credential.Status.Should().Be("active");
        }
        finally
        {
            DeleteSqliteFiles(path);
        }
    }

    private static string TempDbPath(string prefix)
        => Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}.db");

    private static void DeleteSqliteFiles(string path)
    {
        foreach (var file in new[] { path, path + "-shm", path + "-wal" })
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (File.Exists(file))
                        File.Delete(file);
                    break;
                }
                catch (IOException) when (attempt < 2)
                {
                    Thread.Sleep(50);
                }
                catch
                {
                    break;
                }
            }
        }
    }

    private sealed class ColumnInfo
    {
        public string Name { get; set; } = string.Empty;
    }
}
