# Admin Permission Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the local admin backend and UI permission model so system administration and permission administration are separate, testable roles.

**Architecture:** Keep `line-admin.html` and `/api/v1/local-admin/*` as the local admin surface, but replace the singleton admin session behavior with named operators, role-derived permission snapshots, and backend permission gates. Do not merge this with broker scoped-token `Principal` / `Role`; local admin operator roles are a separate human-admin boundary.

**Tech Stack:** C# .NET 8 Minimal APIs, BaseOrm, SQLite, xUnit/FluentAssertions, vanilla JS single-file admin UI.

---

## File Structure

- Modify `packages/csharp/broker-core/Models/LocalAdminCredential.cs`: add operator identity, role, status and login metadata columns.
- Modify `packages/csharp/broker-core/Models/LocalAdminSession.cs`: add operator identity, role and permission snapshot columns.
- Modify `packages/csharp/broker-core/Data/BrokerDbInitializer.cs`: add migration columns, indexes and legacy bootstrap normalization.
- Create `packages/csharp/broker/Services/LocalAdminPermissions.cs`: canonical permission names, role mapping and permission evaluation.
- Modify `packages/csharp/broker/Services/LocalAdminAuthService.cs`: named operator login, permission checks, operator management and session revocation.
- Modify `packages/csharp/broker/Endpoints/LocalAdminEndpoints.cs`: gate each route by permission and add operator management endpoints.
- Modify `packages/csharp/broker/wwwroot/line-admin.html`: role-aware navigation, system monitoring tab and permission management tab.
- Create `packages/csharp/tests/unit/Admin/LocalAdminPermissionTests.cs`: role/permission unit tests.
- Create `packages/csharp/tests/integration/Api/LocalAdminPermissionEndpointTests.cs`: backend authorization integration tests.
- Create `tools/scripts/validate-line-admin.mjs`: static/admin UI permission-gating smoke validation.
- Modify `package.json`: add `validate:line-admin`.
- Modify `docs/manuals/current-user-manual.zh-TW.md`, `docs/manuals/current-technical-manual.zh-TW.md`, `docs/environment-setup.zh-TW.md`: document the operator roles and entry points.

## Task 1: Local Admin Permission Catalog

**Files:**
- Create: `packages/csharp/broker/Services/LocalAdminPermissions.cs`
- Test: `packages/csharp/tests/unit/Admin/LocalAdminPermissionTests.cs`

- [x] **Step 1: Write the failing unit tests**

Create `packages/csharp/tests/unit/Admin/LocalAdminPermissionTests.cs`:

```csharp
using Broker.Services;
using FluentAssertions;

namespace Unit.Tests.Admin;

public class LocalAdminPermissionTests
{
    [Fact]
    public void SuperAdmin_HasEveryDefinedPermission()
    {
        var permissions = LocalAdminPermissions.ForRole(LocalAdminRoles.SuperAdmin);

        permissions.Should().BeEquivalentTo(LocalAdminPermissions.All);
    }

    [Fact]
    public void SystemAdmin_CannotManageOperators()
    {
        var permissions = LocalAdminPermissions.ForRole(LocalAdminRoles.SystemAdmin);

        permissions.Should().Contain(LocalAdminPermissions.SystemStatusRead);
        permissions.Should().Contain(LocalAdminPermissions.SystemDeploymentManage);
        permissions.Should().NotContain(LocalAdminPermissions.PermissionOperatorManage);
    }

    [Fact]
    public void PermissionAdmin_CannotManageDeployment()
    {
        var permissions = LocalAdminPermissions.ForRole(LocalAdminRoles.PermissionAdmin);

        permissions.Should().Contain(LocalAdminPermissions.PermissionOperatorManage);
        permissions.Should().Contain(LocalAdminPermissions.ApprovalAdminManage);
        permissions.Should().NotContain(LocalAdminPermissions.SystemDeploymentManage);
    }

    [Fact]
    public void Auditor_IsReadOnly()
    {
        var permissions = LocalAdminPermissions.ForRole(LocalAdminRoles.Auditor);

        permissions.Should().Contain(LocalAdminPermissions.AuditRead);
        permissions.Should().Contain(LocalAdminPermissions.SystemMonitorRead);
        permissions.Should().NotContain(LocalAdminPermissions.SystemDeliveryManage);
        permissions.Should().NotContain(LocalAdminPermissions.ApprovalAdminManage);
    }
}
```

- [x] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter LocalAdminPermissionTests
```

Expected: FAIL because `LocalAdminPermissions` and `LocalAdminRoles` do not exist.

- [x] **Step 3: Implement permission catalog**

Create `packages/csharp/broker/Services/LocalAdminPermissions.cs`:

```csharp
namespace Broker.Services;

public static class LocalAdminRoles
{
    public const string SuperAdmin = "super_admin";
    public const string SystemAdmin = "system_admin";
    public const string PermissionAdmin = "permission_admin";
    public const string Auditor = "auditor";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        SuperAdmin,
        SystemAdmin,
        PermissionAdmin,
        Auditor
    };

    public static string Normalize(string? role)
        => string.IsNullOrWhiteSpace(role) ? Auditor : role.Trim().ToLowerInvariant();

    public static bool IsKnown(string? role)
        => All.Contains(Normalize(role));
}

public static class LocalAdminPermissions
{
    public const string SystemStatusRead = "system.status.read";
    public const string SystemMonitorRead = "system.monitor.read";
    public const string SystemWorkerManage = "system.worker.manage";
    public const string SystemDeploymentManage = "system.deployment.manage";
    public const string SystemDeliveryManage = "system.delivery.manage";
    public const string SystemBrowserManage = "system.browser.manage";
    public const string PermissionOperatorManage = "permission.operator.manage";
    public const string PermissionUserManage = "permission.user.manage";
    public const string PermissionRegistrationManage = "permission.registration.manage";
    public const string PermissionBrowserGrantManage = "permission.browser_grant.manage";
    public const string ApprovalAdminManage = "approval.admin.manage";
    public const string ToolSpecRead = "tool_spec.read";
    public const string AuditRead = "audit.read";
    public const string AuditVerify = "audit.verify";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        SystemStatusRead,
        SystemMonitorRead,
        SystemWorkerManage,
        SystemDeploymentManage,
        SystemDeliveryManage,
        SystemBrowserManage,
        PermissionOperatorManage,
        PermissionUserManage,
        PermissionRegistrationManage,
        PermissionBrowserGrantManage,
        ApprovalAdminManage,
        ToolSpecRead,
        AuditRead,
        AuditVerify
    };

    public static IReadOnlySet<string> ForRole(string? role)
    {
        var normalized = LocalAdminRoles.Normalize(role);
        if (normalized == LocalAdminRoles.SuperAdmin)
            return new HashSet<string>(All, StringComparer.OrdinalIgnoreCase);

        if (normalized == LocalAdminRoles.SystemAdmin)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                SystemStatusRead,
                SystemMonitorRead,
                SystemWorkerManage,
                SystemDeploymentManage,
                SystemDeliveryManage,
                SystemBrowserManage,
                ToolSpecRead,
                AuditRead
            };
        }

        if (normalized == LocalAdminRoles.PermissionAdmin)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                PermissionOperatorManage,
                PermissionUserManage,
                PermissionRegistrationManage,
                PermissionBrowserGrantManage,
                ApprovalAdminManage,
                ToolSpecRead,
                AuditRead
            };
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SystemStatusRead,
            SystemMonitorRead,
            ToolSpecRead,
            AuditRead,
            AuditVerify
        };
    }

    public static bool Has(string? role, string permission)
        => ForRole(role).Contains(permission);
}
```

- [x] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter LocalAdminPermissionTests
```

Expected: PASS.

## Task 2: Operator Data Model and Migration

**Files:**
- Modify: `packages/csharp/broker-core/Models/LocalAdminCredential.cs`
- Modify: `packages/csharp/broker-core/Models/LocalAdminSession.cs`
- Modify: `packages/csharp/broker-core/Data/BrokerDbInitializer.cs`
- Test: `packages/csharp/tests/unit/Admin/LocalAdminMigrationTests.cs`

- [x] **Step 1: Write the failing migration tests**

Create `packages/csharp/tests/unit/Admin/LocalAdminMigrationTests.cs`:

```csharp
using BrokerCore.Data;
using BrokerCore.Models;
using FluentAssertions;

namespace Unit.Tests.Admin;

public class LocalAdminMigrationTests
{
    [Fact]
    public void Initializer_AddsOperatorColumns()
    {
        using var db = CreateDb();
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

    [Fact]
    public void Initializer_NormalizesLegacyLocalAdminCredential()
    {
        var path = Path.Combine(Path.GetTempPath(), $"broker_legacy_admin_{Guid.NewGuid():N}.db");
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

    private static BrokerDb CreateDb()
    {
        var path = Path.Combine(Path.GetTempPath(), $"broker_admin_migration_{Guid.NewGuid():N}.db");
        var db = new BrokerDb($"Data Source={path}");
        new BrokerDbInitializer(db).Initialize();
        return db;
    }

    private sealed class ColumnInfo
    {
        public string Name { get; set; } = string.Empty;
    }
}
```

- [x] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter LocalAdminMigrationTests
```

Expected: FAIL because the new columns and model properties do not exist.

- [x] **Step 3: Add model properties**

Add these properties to `LocalAdminCredential`:

```csharp
[Column("operator_id")]
public string OperatorId { get; set; } = "local_admin";

[Column("username")]
public string Username { get; set; } = "admin";

[Column("display_name")]
public string DisplayName { get; set; } = "Local Super Admin";

[Column("role")]
public string Role { get; set; } = "super_admin";

[Column("permission_overrides")]
public string PermissionOverrides { get; set; } = "{}";

[Column("status")]
public string Status { get; set; } = "active";

[Column("last_login_at")]
public DateTime? LastLoginAt { get; set; }
```

Add these properties to `LocalAdminSession`:

```csharp
[Column("operator_id")]
public string OperatorId { get; set; } = "local_admin";

[Column("username")]
public string Username { get; set; } = "admin";

[Column("role")]
public string Role { get; set; } = "super_admin";

[Column("permissions_snapshot")]
public string PermissionsSnapshot { get; set; } = "[]";
```

- [x] **Step 4: Add initializer migration and legacy normalization**

In `BrokerDbInitializer.EnsureColumns()`, add:

```csharp
TryExecute("ALTER TABLE local_admin_credentials ADD COLUMN operator_id TEXT DEFAULT 'local_admin'");
TryExecute("ALTER TABLE local_admin_credentials ADD COLUMN username TEXT DEFAULT 'admin'");
TryExecute("ALTER TABLE local_admin_credentials ADD COLUMN display_name TEXT DEFAULT 'Local Super Admin'");
TryExecute("ALTER TABLE local_admin_credentials ADD COLUMN role TEXT DEFAULT 'super_admin'");
TryExecute("ALTER TABLE local_admin_credentials ADD COLUMN permission_overrides TEXT DEFAULT '{}'");
TryExecute("ALTER TABLE local_admin_credentials ADD COLUMN status TEXT DEFAULT 'active'");
TryExecute("ALTER TABLE local_admin_credentials ADD COLUMN last_login_at TEXT NULL");
TryExecute("ALTER TABLE local_admin_sessions ADD COLUMN operator_id TEXT DEFAULT 'local_admin'");
TryExecute("ALTER TABLE local_admin_sessions ADD COLUMN username TEXT DEFAULT 'admin'");
TryExecute("ALTER TABLE local_admin_sessions ADD COLUMN role TEXT DEFAULT 'super_admin'");
TryExecute("ALTER TABLE local_admin_sessions ADD COLUMN permissions_snapshot TEXT DEFAULT '[]'");
```

In `CreateUniqueConstraints()`, add:

```csharp
TryExecute(@"CREATE UNIQUE INDEX IF NOT EXISTS idx_local_admin_credentials_username
              ON local_admin_credentials(username)");
TryExecute(@"CREATE INDEX IF NOT EXISTS idx_local_admin_sessions_operator
              ON local_admin_sessions(operator_id, expires_at, revoked_at)");
```

Add a private method and call it from `Initialize()` after `EnsureTables()`:

```csharp
private void NormalizeLocalAdminBootstrap()
{
    var legacy = _db.Get<LocalAdminCredential>("local_admin");
    if (legacy == null)
        return;

    var changed = false;
    if (string.IsNullOrWhiteSpace(legacy.OperatorId))
    {
        legacy.OperatorId = legacy.CredentialId;
        changed = true;
    }
    if (string.IsNullOrWhiteSpace(legacy.Username))
    {
        legacy.Username = "admin";
        changed = true;
    }
    if (string.IsNullOrWhiteSpace(legacy.DisplayName))
    {
        legacy.DisplayName = "Local Super Admin";
        changed = true;
    }
    if (string.IsNullOrWhiteSpace(legacy.Role))
    {
        legacy.Role = "super_admin";
        changed = true;
    }
    if (string.IsNullOrWhiteSpace(legacy.PermissionOverrides))
    {
        legacy.PermissionOverrides = "{}";
        changed = true;
    }
    if (string.IsNullOrWhiteSpace(legacy.Status))
    {
        legacy.Status = "active";
        changed = true;
    }

    if (changed)
        _db.Update(legacy);
}
```

- [x] **Step 5: Run test to verify it passes**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter LocalAdminMigrationTests
```

Expected: PASS.

## Task 3: Named Operator Auth Service

**Files:**
- Modify: `packages/csharp/broker/Services/LocalAdminAuthService.cs`
- Test: `packages/csharp/tests/unit/Admin/LocalAdminAuthServiceTests.cs`

- [x] **Step 1: Write failing service tests**

Create `packages/csharp/tests/unit/Admin/LocalAdminAuthServiceTests.cs`:

```csharp
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
        using var db = CreateDb();
        var service = new LocalAdminAuthService(db, NullLogger<LocalAdminAuthService>.Instance);
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
        using var db = CreateDb();
        var service = new LocalAdminAuthService(db, NullLogger<LocalAdminAuthService>.Instance);
        service.CreateOperator("sys", "System Admin", LocalAdminRoles.SystemAdmin, "system-password", "local_admin");

        var context = LocalContext();
        var result = service.Login(context, "sys", "system-password", null);

        result.Authenticated.Should().BeTrue();
        service.TryRequirePermission(context, LocalAdminPermissions.SystemStatusRead, out _, out _).Should().BeTrue();
        service.TryRequirePermission(context, LocalAdminPermissions.PermissionOperatorManage, out _, out var denied).Should().BeFalse();
        denied.Should().NotBeNull();
    }

    [Fact]
    public void DisableOperator_RevokesActiveSessions()
    {
        using var db = CreateDb();
        var service = new LocalAdminAuthService(db, NullLogger<LocalAdminAuthService>.Instance);
        service.CreateOperator("perm", "Permission Admin", LocalAdminRoles.PermissionAdmin, "permission-password", "local_admin");
        var context = LocalContext();
        service.Login(context, "perm", "permission-password", null).Authenticated.Should().BeTrue();

        service.DisableOperator("perm", "local_admin");

        service.GetAuthenticatedSession(context).Should().BeNull();
    }

    private static BrokerDb CreateDb()
    {
        var path = Path.Combine(Path.GetTempPath(), $"broker_admin_auth_{Guid.NewGuid():N}.db");
        var db = new BrokerDb($"Data Source={path}");
        new BrokerDbInitializer(db).Initialize();
        return db;
    }

    private static DefaultHttpContext LocalContext()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        context.Response.Body = new MemoryStream();
        return context;
    }
}
```

- [x] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter LocalAdminAuthServiceTests
```

Expected: FAIL because the login signature and operator APIs are not implemented.

- [x] **Step 3: Implement named operator auth**

Modify `LocalAdminAuthService`:

- Keep the existing `Login(HttpContext, string password, string? newPassword)` overload as a compatibility wrapper calling `Login(context, "admin", password, newPassword)`.
- Add `Login(HttpContext context, string username, string password, string? newPassword)`.
- Lookup credentials by normalized username.
- Create bootstrap credential `credential_id = "local_admin"`, `operator_id = "local_admin"`, `username = "admin"`, `role = "super_admin"` on first login.
- Issue sessions with `operator_id`, `username`, `role`, `permissions_snapshot`.
- Return `OperatorId`, `Username`, `Role`, `Permissions` from `LocalAdminStatus` and `LocalAdminLoginResult`.
- Add `TryRequirePermission(...)`.
- Add `CreateOperator`, `ListOperators`, `UpdateOperatorRole`, `DisableOperator`, `ResetOperatorPassword`, `RevokeOperatorSessions`.

The denial body for missing permission must be:

```csharp
denied = Results.Json(
    Broker.Helpers.ApiResponseHelper.Error($"Forbidden: permission '{permission}' required.", 403),
    statusCode: 403);
```

- [x] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter LocalAdminAuthServiceTests
```

Expected: PASS.

## Task 4: Local Admin Endpoint Permission Gates

**Files:**
- Modify: `packages/csharp/broker/Endpoints/LocalAdminEndpoints.cs`
- Test: `packages/csharp/tests/integration/Api/LocalAdminPermissionEndpointTests.cs`

- [x] **Step 1: Write failing integration tests**

Create `packages/csharp/tests/integration/Api/LocalAdminPermissionEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Broker.Services;
using BrokerCore.Data;
using Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests.Api;

public class LocalAdminPermissionEndpointTests : IClassFixture<BrokerFixture>
{
    private readonly BrokerFixture _fixture;

    public LocalAdminPermissionEndpointTests(BrokerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SystemAdmin_CanReadSystemStatus_ButCannotListOperators()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<LocalAdminAuthService>();
        auth.CreateOperator("sysadmin", "System Admin", LocalAdminRoles.SystemAdmin, "system-password", "test");

        var cookie = await LoginAsync("sysadmin", "system-password");

        using var status = await SendWithCookieAsync(HttpMethod.Get, "/api/v1/local-admin/system/status", cookie);
        status.StatusCode.Should().Be(HttpStatusCode.OK);

        using var operators = await SendWithCookieAsync(HttpMethod.Get, "/api/v1/local-admin/operators", cookie);
        operators.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PermissionAdmin_CanListOperators_ButCannotPreviewDeployment()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<LocalAdminAuthService>();
        auth.CreateOperator("permadmin", "Permission Admin", LocalAdminRoles.PermissionAdmin, "permission-password", "test");

        var cookie = await LoginAsync("permadmin", "permission-password");

        using var operators = await SendWithCookieAsync(HttpMethod.Get, "/api/v1/local-admin/operators", cookie);
        operators.StatusCode.Should().Be(HttpStatusCode.OK);

        using var deployment = await SendWithCookieAsync(
            HttpMethod.Post,
            "/api/v1/local-admin/deployment/preview",
            cookie,
            new { target_id = "missing", artifact_path = "site.zip" });
        deployment.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Auditor_CannotApproveRequests()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<LocalAdminAuthService>();
        auth.CreateOperator("auditor", "Auditor", LocalAdminRoles.Auditor, "auditor-password", "test");

        var cookie = await LoginAsync("auditor", "auditor-password");

        using var response = await SendWithCookieAsync(
            HttpMethod.Post,
            "/api/v1/local-admin/approvals/apr_missing/approve",
            cookie,
            new { reason = "nope" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<string> LoginAsync(string username, string password)
    {
        using var response = await _fixture.Client.PostAsJsonAsync("/api/v1/local-admin/login", new
        {
            username,
            password
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("Set-Cookie", out var values).Should().BeTrue();
        var cookie = values!.First(value => value.StartsWith($"{LocalAdminAuthService.SessionCookieName}=", StringComparison.Ordinal));
        return cookie.Split(';', 2)[0];
    }

    private Task<HttpResponseMessage> SendWithCookieAsync(HttpMethod method, string path, string cookie, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", cookie);
        if (body != null)
            request.Content = JsonContent.Create(body);
        return _fixture.Client.SendAsync(request);
    }
}
```

- [x] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test packages/csharp/tests/integration/Integration.Tests.csproj --filter LocalAdminPermissionEndpointTests
```

Expected: FAIL because endpoint permission gates and `/operators` routes do not exist.

- [x] **Step 3: Gate local-admin routes**

In `LocalAdminEndpoints.Map`, add small local helper methods:

```csharp
static bool Require(HttpContext ctx, LocalAdminAuthService auth, string permission, out LocalAdminSession session, out IResult denied)
    => auth.TryRequirePermission(ctx, permission, out session, out denied);

static bool RequireAny(HttpContext ctx, LocalAdminAuthService auth, string[] permissions, out LocalAdminSession session, out IResult denied)
    => auth.TryRequireAnyPermission(ctx, permissions, out session, out denied);
```

Change route checks from `TryRequireAuthenticated` to the matching permission from the spec. Add `/operators` routes using `permission.operator.manage`.

For `/api/v1/local-admin/login`, parse optional `username`:

```csharp
var username = body.TryGetProperty("username", out var un) && un.ValueKind == JsonValueKind.String
    ? un.GetString()
    : "admin";
var result = auth.Login(ctx, username ?? "admin", password, newPassword);
```

- [x] **Step 4: Run test to verify it passes**

Run:

```powershell
dotnet test packages/csharp/tests/integration/Integration.Tests.csproj --filter LocalAdminPermissionEndpointTests
```

Expected: PASS.

## Task 5: Admin UI Role-Aware Navigation and Monitoring

**Files:**
- Modify: `packages/csharp/broker/wwwroot/line-admin.html`
- Create: `tools/scripts/validate-line-admin.mjs`
- Modify: `package.json`

- [x] **Step 1: Write failing validation script**

Create `tools/scripts/validate-line-admin.mjs`:

```javascript
import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';

const html = await readFile('packages/csharp/broker/wwwroot/line-admin.html', 'utf8');

assert.match(html, /data-tab="monitoring"/, 'line-admin must expose a monitoring tab');
assert.match(html, /data-tab="permissions"/, 'line-admin must expose a permissions tab');
assert.match(html, /hasPermission\(/, 'line-admin must contain permission-aware UI gating');
assert.match(html, /system\.monitor\.read/, 'line-admin must reference monitoring permission');
assert.match(html, /permission\.operator\.manage/, 'line-admin must reference operator management permission');
assert.match(html, /\/api\/v1\/local-admin\/operators/, 'line-admin must call operator management API');

console.log('line-admin validation passed');
```

Add to `package.json` scripts:

```json
"validate:line-admin": "node tools/scripts/validate-line-admin.mjs"
```

- [x] **Step 2: Run validation to verify it fails**

Run:

```powershell
npm run validate:line-admin
```

Expected: FAIL because monitoring/permissions tabs and permission-gating JS are absent.

- [x] **Step 3: Implement UI gating**

Modify `line-admin.html`:

- Add nav buttons `data-tab="monitoring"` and `data-tab="permissions"`.
- Add `const PERMISSIONS = { ... }` with the backend permission names.
- Add:

```javascript
function hasPermission(permission) {
  return Array.isArray(state.authStatus?.permissions)
    && state.authStatus.permissions.includes(permission);
}

function requireVisible(selector, permission) {
  const node = document.querySelector(selector);
  if (node) node.classList.toggle("hidden", !hasPermission(permission));
}
```

- In `renderAuthState()`, hide or show nav/actions using `hasPermission`.
- Build the monitoring tab from existing `state.system`, alerts, and health API responses.
- Build the permissions tab with operator list, create operator form, role update and disable buttons.

- [x] **Step 4: Run validation to verify it passes**

Run:

```powershell
npm run validate:line-admin
```

Expected: PASS.

## Task 6: Documentation and Full Verification

**Files:**
- Modify: `docs/manuals/current-user-manual.zh-TW.md`
- Modify: `docs/manuals/current-technical-manual.zh-TW.md`
- Modify: `docs/environment-setup.zh-TW.md`

- [x] **Step 1: Update manuals**

Document:

- Admin UI entry point remains `http://127.0.0.1:5361/line-admin.html`.
- First login uses username `admin` and initial password `admin`, then requires a new password.
- Local operator roles are `super_admin`, `system_admin`, `permission_admin`, `auditor`.
- System monitoring and permission management are separate tabs.
- Backend enforces role permissions; UI hiding is not the security boundary.

- [x] **Step 2: Run targeted backend tests**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter "LocalAdminPermissionTests|LocalAdminMigrationTests|LocalAdminAuthServiceTests"
dotnet test packages/csharp/tests/integration/Integration.Tests.csproj --filter LocalAdminPermissionEndpointTests
```

Expected: all targeted tests PASS.

- [x] **Step 3: Run full verification**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj
dotnet test packages/csharp/tests/integration/Integration.Tests.csproj
dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj
npm run validate:line-admin
npm run validate:user-portal
```

Expected: all commands PASS. If Smart App Control blocks built test assemblies, run the existing signing/WDAC repair workflow from `docs/manuals/dev-code-signing-wdac.zh-TW.md`, then re-run the blocked command with `--no-build`.

- [x] **Step 4: Commit implementation**

Run:

```powershell
git status --short
git add packages/csharp/broker-core/Models/LocalAdminCredential.cs packages/csharp/broker-core/Models/LocalAdminSession.cs packages/csharp/broker-core/Data/BrokerDbInitializer.cs packages/csharp/broker/Services/LocalAdminPermissions.cs packages/csharp/broker/Services/LocalAdminAuthService.cs packages/csharp/broker/Endpoints/LocalAdminEndpoints.cs packages/csharp/broker/wwwroot/line-admin.html packages/csharp/tests/unit/Admin packages/csharp/tests/integration/Api/LocalAdminPermissionEndpointTests.cs tools/scripts/validate-line-admin.mjs package.json docs/manuals/current-user-manual.zh-TW.md docs/manuals/current-technical-manual.zh-TW.md docs/environment-setup.zh-TW.md docs/superpowers/plans/2026-06-26-admin-permission-boundary.md
git commit -m "feat: split admin and permission management roles"
```

Expected: commit succeeds with only related files staged.

## Self-Review

- Spec coverage: data model, role split, API gates, UI tabs, migration, docs and tests are covered by Tasks 1-6.
- Placeholder scan: no deferred implementation markers are present.
- Type consistency: role constants, permission constants, endpoint names and test expectations use the same names across tasks.
