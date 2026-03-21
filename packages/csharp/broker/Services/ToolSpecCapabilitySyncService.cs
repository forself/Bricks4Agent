using System.Text.Json;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

public sealed class ToolSpecCapabilitySyncService : IHostedService
{
    private static readonly HashSet<string> ActiveStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "ready",
        "beta"
    };

    private readonly IToolSpecRegistry _registry;
    private readonly BrokerDb _db;
    private readonly ILogger<ToolSpecCapabilitySyncService> _logger;

    public ToolSpecCapabilitySyncService(
        IToolSpecRegistry registry,
        BrokerDb db,
        ILogger<ToolSpecCapabilitySyncService> logger)
    {
        _registry = registry;
        _db = db;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var inserted = 0;
        var updated = 0;
        var removed = 0;
        var skipped = 0;

        foreach (var spec in _registry.GetDefinitions())
        {
            if (!ActiveStatuses.Contains(spec.Status))
            {
                foreach (var binding in spec.CapabilityBindings)
                {
                    if (string.IsNullOrWhiteSpace(binding.CapabilityId))
                    {
                        skipped++;
                        continue;
                    }

                    removed += RemoveCapability(binding.CapabilityId);
                }
                continue;
            }

            foreach (var binding in spec.CapabilityBindings)
            {
                if (string.IsNullOrWhiteSpace(binding.CapabilityId) || string.IsNullOrWhiteSpace(binding.Route))
                {
                    skipped++;
                    continue;
                }

                var candidate = BuildCapability(spec, binding);
                var existing = _db.Get<Capability>(candidate.CapabilityId);
                if (existing == null)
                {
                    _db.Insert(candidate);
                    inserted++;
                    continue;
                }

                candidate.Version = existing.Version;
                if (!CapabilityChanged(existing, candidate))
                {
                    skipped++;
                    continue;
                }

                candidate.Version = existing.Version + 1;
                _db.Update(candidate);
                updated++;
            }
        }

        _logger.LogInformation(
            "Tool-spec capability sync complete: inserted={Inserted}, updated={Updated}, removed={Removed}, skipped={Skipped}",
            inserted, updated, removed, skipped);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    private static Capability BuildCapability(ToolSpecDocument spec, ToolCapabilityBindingFile binding)
    {
        var template = spec.CapabilityTemplate ?? new ToolCapabilityTemplateFile();
        return new Capability
        {
            CapabilityId = binding.CapabilityId,
            Route = binding.Route,
            ActionType = ParseActionType(template.ActionType),
            ResourceType = string.IsNullOrWhiteSpace(template.ResourceType) ? "tool" : template.ResourceType,
            RiskLevel = ParseRiskLevel(template.RiskLevel),
            ApprovalPolicy = string.IsNullOrWhiteSpace(template.ApprovalPolicy) ? "auto" : template.ApprovalPolicy,
            TtlSeconds = template.TtlSeconds <= 0 ? 900 : template.TtlSeconds,
            AuditLevel = string.IsNullOrWhiteSpace(template.AuditLevel) ? "summary" : template.AuditLevel,
            Quota = template.Quota.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? "{}"
                : template.Quota.GetRawText(),
            ParamSchema = spec.InputSchema.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? "{}"
                : spec.InputSchema.GetRawText()
        };
    }

    private static ActionType ParseActionType(string? raw)
        => Enum.TryParse<ActionType>(raw, true, out var parsed) ? parsed : ActionType.Read;

    private static RiskLevel ParseRiskLevel(string? raw)
        => Enum.TryParse<RiskLevel>(raw, true, out var parsed) ? parsed : RiskLevel.Low;

    private static bool CapabilityChanged(Capability existing, Capability candidate)
    {
        return existing.Route != candidate.Route ||
               existing.ActionType != candidate.ActionType ||
               existing.ResourceType != candidate.ResourceType ||
               existing.ParamSchema != candidate.ParamSchema ||
               existing.RiskLevel != candidate.RiskLevel ||
               existing.ApprovalPolicy != candidate.ApprovalPolicy ||
               existing.TtlSeconds != candidate.TtlSeconds ||
               existing.Quota != candidate.Quota ||
               existing.AuditLevel != candidate.AuditLevel;
    }

    private int RemoveCapability(string capabilityId)
    {
        var existing = _db.Get<Capability>(capabilityId);
        if (existing == null)
            return 0;

        _db.Execute("DELETE FROM capability_grants WHERE capability_id = @capabilityId", new { capabilityId });
        _db.Execute("DELETE FROM capabilities WHERE capability_id = @capabilityId", new { capabilityId });
        return 1;
    }
}
