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
