using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

public sealed class AzureIisDeploymentTargetService
{
    private readonly BrokerDb _db;

    public AzureIisDeploymentTargetService(BrokerDb db)
    {
        _db = db;
    }

    public IReadOnlyList<AzureIisDeploymentTarget> ListTargets(string? status = null)
    {
        var sql = "SELECT * FROM azure_iis_deployment_targets WHERE 1=1";
        var args = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(status))
        {
            sql += " AND status = @status";
            args["status"] = status;
        }

        sql += " ORDER BY created_at DESC";
        return _db.Query<AzureIisDeploymentTarget>(sql, args);
    }

    public AzureIisDeploymentTarget? GetTarget(string targetId)
        => _db.Get<AzureIisDeploymentTarget>(targetId);

    public AzureIisDeploymentTarget UpsertTarget(AzureIisDeploymentTarget target)
    {
        ValidateTarget(target);

        if (string.IsNullOrWhiteSpace(target.TargetId))
        {
            target.TargetId = BrokerCore.IdGen.New("ait");
            target.CreatedAt = DateTime.UtcNow;
            _db.Insert(target);
            return target;
        }

        var existing = _db.Get<AzureIisDeploymentTarget>(target.TargetId);
        if (existing == null)
        {
            target.CreatedAt = target.CreatedAt == default ? DateTime.UtcNow : target.CreatedAt;
            _db.Insert(target);
            return target;
        }

        existing.DisplayName = target.DisplayName;
        existing.Provider = target.Provider;
        existing.VmHost = target.VmHost;
        existing.Port = target.Port;
        existing.UseSsl = target.UseSsl;
        existing.Transport = target.Transport;
        existing.SiteName = target.SiteName;
        existing.DeploymentMode = string.IsNullOrWhiteSpace(target.DeploymentMode) ? "site_root" : target.DeploymentMode;
        existing.ApplicationPath = target.ApplicationPath ?? string.Empty;
        existing.AppPoolName = target.AppPoolName;
        existing.PhysicalPath = target.PhysicalPath;
        existing.HealthCheckPath = target.HealthCheckPath ?? string.Empty;
        existing.HealthCheckBaseUrl = target.HealthCheckBaseUrl ?? string.Empty;
        existing.SecretRef = target.SecretRef;
        existing.Status = target.Status;
        existing.MetadataJson = string.IsNullOrWhiteSpace(target.MetadataJson) ? "{}" : target.MetadataJson;
        _db.Update(existing);
        return existing;
    }

    private void ValidateTarget(AzureIisDeploymentTarget target)
    {
        if (!Path.IsPathRooted(target.PhysicalPath))
        {
            throw new InvalidOperationException("physical_path must be an absolute path.");
        }

        var deploymentMode = string.IsNullOrWhiteSpace(target.DeploymentMode) ? "site_root" : target.DeploymentMode.Trim().ToLowerInvariant();
        if (deploymentMode is not ("site_root" or "iis_application"))
        {
            throw new InvalidOperationException("deployment_mode must be site_root or iis_application.");
        }

        target.DeploymentMode = deploymentMode;
        target.ApplicationPath = NormalizeApplicationPath(target.ApplicationPath);
        target.HealthCheckPath = NormalizeOptionalPath(target.HealthCheckPath);
        target.HealthCheckBaseUrl = NormalizeOptionalAbsoluteUrl(target.HealthCheckBaseUrl);

        if (deploymentMode == "iis_application" && string.IsNullOrWhiteSpace(target.ApplicationPath))
        {
            throw new InvalidOperationException("application_path is required when deployment_mode is iis_application.");
        }

        if (deploymentMode == "iis_application")
        {
            var duplicate = _db.Query<AzureIisDeploymentTarget>(
                @"SELECT * FROM azure_iis_deployment_targets
                  WHERE vm_host = @vmHost
                    AND site_name = @siteName
                    AND application_path = @applicationPath
                    AND status = 'active'
                    AND (@targetId = '' OR target_id <> @targetId)
                  LIMIT 1",
                new
                {
                    vmHost = target.VmHost,
                    siteName = target.SiteName,
                    applicationPath = target.ApplicationPath,
                    targetId = target.TargetId ?? string.Empty
                }).FirstOrDefault();

            if (duplicate != null)
            {
                throw new InvalidOperationException("An active child-application target already exists for the same vm_host, site_name, and application_path.");
            }
        }
    }

    private static string NormalizeApplicationPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }

    private static string NormalizeOptionalPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim();
        return normalized.StartsWith('/')
            ? normalized
            : "/" + normalized;
    }

    private static string NormalizeOptionalAbsoluteUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("health_check_base_url must be an absolute URL.");

        if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("health_check_base_url must use http or https.");
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
}
