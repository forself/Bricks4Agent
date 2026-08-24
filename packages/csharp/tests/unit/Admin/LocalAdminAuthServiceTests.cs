using Broker.Services;
using BrokerCore.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Unit.Tests.Admin;

public class LocalAdminAuthServiceTests
{
    [Fact]
    public void FirstLogin_BootstrapsSuperAdminWithPermissions()
    {
        using var testDb = CreateDb();
        var service = new LocalAdminAuthService(testDb.Db, NullLogger<LocalAdminAuthService>.Instance);
        var context = LocalContext();

        var result = service.Login(context, "admin", "admin", "new-password-1");

        result.Authenticated.Should().BeTrue();
        result.OperatorId.Should().Be("local_admin");
        result.Username.Should().Be("admin");
        result.Role.Should().Be(LocalAdminRoles.SuperAdmin);
        result.Permissions.Should().Contain(LocalAdminPermissions.PermissionOperatorManage);
    }

    [Fact]
    public void SystemAdminSession_DoesNotHaveOperatorManagePermission()
    {
        using var testDb = CreateDb();
        var service = new LocalAdminAuthService(testDb.Db, NullLogger<LocalAdminAuthService>.Instance);
        service.CreateOperator("sys", "System Admin", LocalAdminRoles.SystemAdmin, "system-password", "local_admin");

        var loginContext = LocalContext();
        var result = service.Login(loginContext, "sys", "system-password", null);
        result.Authenticated.Should().BeTrue();

        var sessionContext = LocalContext(ReadCookie(loginContext));
        service.TryRequirePermission(sessionContext, LocalAdminPermissions.SystemStatusRead, out _, out _).Should().BeTrue();
        service.TryRequirePermission(sessionContext, LocalAdminPermissions.PermissionOperatorManage, out _, out var denied).Should().BeFalse();
        denied.Should().NotBeNull();
    }

    [Fact]
    public void DisableOperator_RevokesActiveSessions()
    {
        using var testDb = CreateDb();
        var service = new LocalAdminAuthService(testDb.Db, NullLogger<LocalAdminAuthService>.Instance);
        service.CreateOperator("perm", "Permission Admin", LocalAdminRoles.PermissionAdmin, "permission-password", "local_admin");
        var loginContext = LocalContext();
        service.Login(loginContext, "perm", "permission-password", null).Authenticated.Should().BeTrue();

        service.DisableOperator("perm", "local_admin");

        service.GetAuthenticatedSession(LocalContext(ReadCookie(loginContext))).Should().BeNull();
    }

    private static TempBrokerDb CreateDb()
    {
        var path = Path.Combine(Path.GetTempPath(), $"broker_admin_auth_{Guid.NewGuid():N}.db");
        var db = new BrokerDb($"Data Source={path}");
        new BrokerDbInitializer(db).Initialize();
        return new TempBrokerDb(path, db);
    }

    private static DefaultHttpContext LocalContext(string? cookie = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        context.Response.Body = new MemoryStream();
        if (!string.IsNullOrWhiteSpace(cookie))
            context.Request.Headers.Cookie = cookie;
        return context;
    }

    private static string ReadCookie(DefaultHttpContext context)
    {
        var setCookie = context.Response.Headers.SetCookie.FirstOrDefault(value =>
            value?.StartsWith($"{LocalAdminAuthService.SessionCookieName}=", StringComparison.Ordinal) == true);
        setCookie.Should().NotBeNullOrWhiteSpace();
        return setCookie!.Split(';', 2)[0];
    }

    private sealed class TempBrokerDb : IDisposable
    {
        private readonly string _path;

        public TempBrokerDb(string path, BrokerDb db)
        {
            _path = path;
            Db = db;
        }

        public BrokerDb Db { get; }

        public void Dispose()
        {
            Db.Dispose();
            foreach (var file in new[] { _path, _path + "-shm", _path + "-wal" })
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
    }
}
