using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Broker.Services;

public enum ArchitectureFactKind
{
    Entity, Field, Relation, FieldType, Role, Permission,
    VerticalScope, HorizontalScope, QueryTolerance, Page
}

public enum ArchitectureFactStatus { Proposed, Confirmed, Superseded, Rejected }

public sealed record ArchitectureFact(
    string Id,
    ArchitectureFactKind Kind,
    string Subject,
    string? Object,
    string Value,
    IReadOnlyList<string> DependsOn,
    string EvidenceCode,
    ArchitectureFactStatus Status);

public sealed record ArchitectureProposal(
    ArchitectureFactKind Kind,
    string Subject,
    string? Object,
    string Value,
    IReadOnlyList<string> DependsOn,
    string EvidenceCode);

/// <summary>
/// A pure, versioned interview core. Only confirmed facts enter the generation projection.
/// The user's labels live in InterviewLabelVault and never enter the agent projection.
/// </summary>
public sealed class ProjectInterviewArchitecture
{
    private static readonly Regex CodePattern = new("^[A-Z][A-Za-z0-9_]{1,79}$", RegexOptions.Compiled);
    private static readonly IReadOnlyDictionary<ArchitectureFactKind, HashSet<string>> Values =
        new Dictionary<ArchitectureFactKind, HashSet<string>>
        {
            [ArchitectureFactKind.Entity] = Set("resource"),
            [ArchitectureFactKind.Field] = Set("field"),
            [ArchitectureFactKind.Relation] = Set("one_to_one", "one_to_many", "many_to_many"),
            [ArchitectureFactKind.FieldType] = Set("text", "integer", "number", "boolean"),
            [ArchitectureFactKind.Role] = Set("role"),
            [ArchitectureFactKind.Permission] = Set("create", "read", "update", "delete"),
            [ArchitectureFactKind.VerticalScope] = Set("self", "descendants", "self_descendants"),
            [ArchitectureFactKind.HorizontalScope] = Set("unit", "function", "unit_or_function", "unit_and_function"),
            [ArchitectureFactKind.QueryTolerance] = Set("strict", "ignore_invalid_user_filters"),
            [ArchitectureFactKind.Page] = Set("list", "detail", "form")
        };

    private readonly List<ArchitectureFact> _facts = [];
    private readonly InterviewLabelVault _vault;

    public ProjectInterviewArchitecture(InterviewLabelVault vault)
        => _vault = vault ?? throw new ArgumentNullException(nameof(vault));

    public int Version { get; private set; }
    public IReadOnlyList<ArchitectureFact> Facts => _facts.AsReadOnly();

    public ArchitectureFact Propose(JsonElement agentJson)
    {
        var proposal = ParseProposal(agentJson);
        var dependencies = proposal.DependsOn.Distinct(StringComparer.Ordinal).ToArray();
        if (dependencies.Length != proposal.DependsOn.Count ||
            dependencies.Any(id => _facts.All(fact => fact.Id != id || fact.Status is ArchitectureFactStatus.Rejected or ArchitectureFactStatus.Superseded)))
            throw new ArgumentException("A proposal has unknown, repeated, or inactive dependencies.", nameof(agentJson));
        var fact = new ArchitectureFact(NewCode("A"), proposal.Kind, proposal.Subject, proposal.Object,
            proposal.Value, dependencies, proposal.EvidenceCode, ArchitectureFactStatus.Proposed);
        _facts.Add(fact);
        Version++;
        return fact;
    }

    public ArchitectureFact Confirm(string factId)
    {
        var index = Find(factId);
        var fact = _facts[index];
        if (fact.Status != ArchitectureFactStatus.Proposed ||
            fact.DependsOn.Any(id => _facts.All(item => item.Id != id || item.Status != ArchitectureFactStatus.Confirmed)))
            throw new InvalidOperationException("Only a proposal whose dependencies are confirmed may be confirmed.");
        var confirmed = fact with { Status = ArchitectureFactStatus.Confirmed };
        _facts[index] = confirmed;
        Version++;
        return confirmed;
    }

    public void Reject(string factId)
    {
        var index = Find(factId);
        if (_facts[index].Status != ArchitectureFactStatus.Proposed)
            throw new InvalidOperationException("Only a proposed fact may be rejected.");
        var rejected = new HashSet<string>(StringComparer.Ordinal) { factId };
        bool found;
        do
        {
            found = false;
            foreach (var fact in _facts.Where(fact => fact.Status == ArchitectureFactStatus.Proposed))
            {
                if (!rejected.Contains(fact.Id) && fact.DependsOn.Any(rejected.Contains))
                    found |= rejected.Add(fact.Id);
            }
        } while (found);
        for (var factIndex = 0; factIndex < _facts.Count; factIndex++)
            if (rejected.Contains(_facts[factIndex].Id))
                _facts[factIndex] = _facts[factIndex] with { Status = ArchitectureFactStatus.Rejected };
        Version++;
    }

    public ArchitectureFact ReplaceConfirmed(string factId, JsonElement agentJson)
    {
        var index = Find(factId);
        var original = _facts[index];
        if (original.Status != ArchitectureFactStatus.Confirmed)
            throw new InvalidOperationException("Only a confirmed fact may be revised.");
        var proposal = ParseProposal(agentJson);
        if (proposal.Kind != original.Kind || proposal.Subject != original.Subject || proposal.Object != original.Object ||
            !proposal.DependsOn.SequenceEqual(original.DependsOn, StringComparer.Ordinal))
            throw new ArgumentException("A revision must retain the fact identity and dependencies.", nameof(agentJson));

        var replacement = new ArchitectureFact(NewCode("A"), proposal.Kind, proposal.Subject, proposal.Object,
            proposal.Value, proposal.DependsOn.ToArray(), proposal.EvidenceCode, ArchitectureFactStatus.Proposed);
        _facts[index] = original with { Status = ArchitectureFactStatus.Superseded };
        _facts.Add(replacement);
        var invalidated = new HashSet<string>(StringComparer.Ordinal) { factId };
        bool found;
        do
        {
            found = false;
            for (var childIndex = 0; childIndex < _facts.Count; childIndex++)
            {
                var child = _facts[childIndex];
                if (child.Status is ArchitectureFactStatus.Superseded or ArchitectureFactStatus.Rejected ||
                    !child.DependsOn.Any(invalidated.Contains) || invalidated.Contains(child.Id))
                    continue;
                var newDependencies = child.DependsOn
                    .Select(id => id == factId ? replacement.Id : id).ToArray();
                _facts[childIndex] = child with
                {
                    DependsOn = newDependencies,
                    Status = ArchitectureFactStatus.Proposed
                };
                invalidated.Add(child.Id);
                found = true;
            }
        } while (found);
        Version++;
        return replacement;
    }

    public IReadOnlyList<ArchitectureFact> ConfirmedProjection()
        => _facts.Where(fact => fact.Status == ArchitectureFactStatus.Confirmed).ToArray();

    public string ToAgentJson()
        => JsonSerializer.Serialize(new
        {
            version = Version,
            facts = _facts.Where(fact => fact.Status is ArchitectureFactStatus.Proposed or ArchitectureFactStatus.Confirmed)
                .Select(fact => new
                {
                    id = fact.Id,
                    kind = KindCode(fact.Kind),
                    subject = fact.Subject,
                    @object = fact.Object,
                    value = fact.Value,
                    depends_on = fact.DependsOn,
                    status = fact.Status == ArchitectureFactStatus.Confirmed ? "confirmed" : "proposed"
                })
        });

    private ArchitectureProposal ParseProposal(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Agent proposal must be one JSON object.", nameof(json));
        var properties = json.EnumerateObject().ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal)
            { "kind", "subject", "object", "value", "depends_on", "evidence" };
        if (properties.Length != 6 || properties.Any(property => !names.Remove(property.Name)) || names.Count != 0)
            throw new ArgumentException("Agent proposal has missing, duplicate, or unknown properties.", nameof(json));
        var kindCode = ReadCode(json, "kind");
        if (!Enum.TryParse<ArchitectureFactKind>(kindCode, true, out var kind) || !Enum.IsDefined(kind) ||
            !string.Equals(KindCode(kind), kindCode, StringComparison.Ordinal))
            throw new ArgumentException("Fact kind is outside the allowlist.", nameof(json));
        var subject = ReadCode(json, "subject");
        var value = ReadCode(json, "value");
        var evidence = ReadCode(json, "evidence");
        string? target = null;
        var targetNode = json.GetProperty("object");
        if (targetNode.ValueKind == JsonValueKind.String) target = targetNode.GetString();
        else if (targetNode.ValueKind != JsonValueKind.Null)
            throw new ArgumentException("Object must be a code or null.", nameof(json));
        if (target is not null && !CodePattern.IsMatch(target))
            throw new ArgumentException("Object code is invalid.", nameof(json));
        if (!CodePattern.IsMatch(subject) || !CodePattern.IsMatch(evidence) ||
            !_vault.ContainsLabelCode(subject) || !_vault.ContainsEvidenceCode(evidence) ||
            (target is not null && !_vault.ContainsLabelCode(target)) ||
            !Values[kind].Contains(value))
            throw new ArgumentException("Proposal contains an unknown code or non-enumerated value.", nameof(json));
        var dependenciesNode = json.GetProperty("depends_on");
        if (dependenciesNode.ValueKind != JsonValueKind.Array || dependenciesNode.GetArrayLength() > 32)
            throw new ArgumentException("Dependencies must be a short array.", nameof(json));
        var dependencies = dependenciesNode.EnumerateArray().Select(node =>
            node.ValueKind == JsonValueKind.String && CodePattern.IsMatch(node.GetString() ?? string.Empty)
                ? node.GetString()!
                : throw new ArgumentException("Dependency code is invalid.", nameof(json))).ToArray();
        return new ArchitectureProposal(kind, subject, target, value, dependencies, evidence);
    }

    private static string ReadCode(JsonElement json, string name)
    {
        var node = json.GetProperty(name);
        return node.ValueKind == JsonValueKind.String ? node.GetString() ?? string.Empty
            : throw new ArgumentException($"{name} must be a code.", nameof(json));
    }

    private int Find(string factId)
    {
        var index = _facts.FindIndex(fact => fact.Id == factId);
        return index < 0 ? throw new KeyNotFoundException("Fact does not exist.") : index;
    }

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);
    private static string KindCode(ArchitectureFactKind kind) => kind.ToString().ToLowerInvariant();
    private static string NewCode(string prefix) => prefix + "_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
}

public sealed class InterviewLabelVault
{
    private readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);
    private readonly HashSet<string> _evidenceCodes = new(StringComparer.Ordinal);

    public string RegisterConfirmedLabel(char kind, string label)
    {
        if (kind is not ('E' or 'F' or 'R' or 'P' or 'U') ||
            string.IsNullOrWhiteSpace(label) || label.Length > 120)
            throw new ArgumentException("Confirmed label is empty or too long.", nameof(label));
        var id = kind + "_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        _labels.Add(id, label);
        return id;
    }

    public string RegisterEvidence()
    {
        var id = "V_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        _evidenceCodes.Add(id);
        return id;
    }

    internal bool ContainsLabelCode(string id) => _labels.ContainsKey(id);
    internal bool ContainsEvidenceCode(string id) => _evidenceCodes.Contains(id);

    public string ResolveForDisplay(string id)
        => _labels.TryGetValue(id, out var label) ? label : throw new KeyNotFoundException("Label code is unknown.");
}
