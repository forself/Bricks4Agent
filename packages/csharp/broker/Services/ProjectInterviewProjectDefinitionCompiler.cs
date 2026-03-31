using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Broker.Services;

public sealed record ProjectInterviewCompileResult(
    int Version,
    ProjectInstanceDefinition ProjectDefinition,
    ProjectInterviewVersionDag Dag);

public sealed class ProjectInterviewProjectDefinitionCompiler
{
    public ProjectInterviewCompileResult Compile(int version, IReadOnlyList<string> confirmedAssertions)
    {
        var scale = ExtractSingleValue(confirmedAssertions, "project_scale=");
        var template = ExtractSingleValue(confirmedAssertions, "template_family=");
        var enabledModules = confirmedAssertions
            .Where(item => item.StartsWith("enabled_module=", StringComparison.Ordinal))
            .Select(item => item["enabled_module=".Length..])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var styleProfile = confirmedAssertions
            .FirstOrDefault(item => item.StartsWith("style_profile=", StringComparison.Ordinal))?["style_profile=".Length..]
            ?? "clean-enterprise";

        var projectDefinition = new ProjectInstanceDefinition(scale, template, enabledModules, styleProfile);
        var projectDefinitionJson = JsonSerializer.Serialize(projectDefinition);
        var dag = new ProjectInterviewVersionDag(
            version,
            new[]
            {
                new VersionDagNode("confirmed_assertions", "confirmed_assertions", ComputeDigest(string.Join('\n', confirmedAssertions))),
                new VersionDagNode("project_instance_definition_json", "project_instance_definition_json", ComputeDigest(projectDefinitionJson))
            },
            new[]
            {
                new VersionDagEdge("confirmed_assertions", "project_instance_definition_json")
            });

        return new ProjectInterviewCompileResult(version, projectDefinition, dag);
    }

    private static string ExtractSingleValue(IReadOnlyList<string> confirmedAssertions, string prefix)
    {
        var assertion = confirmedAssertions.Single(item => item.StartsWith(prefix, StringComparison.Ordinal));
        return assertion[prefix.Length..];
    }

    private static string ComputeDigest(string payload)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
