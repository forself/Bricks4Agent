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
