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
        existing.AppPoolName = target.AppPoolName;
        existing.PhysicalPath = target.PhysicalPath;
        existing.SecretRef = target.SecretRef;
        existing.Status = target.Status;
        existing.MetadataJson = string.IsNullOrWhiteSpace(target.MetadataJson) ? "{}" : target.MetadataJson;
        _db.Update(existing);
        return existing;
    }
}
