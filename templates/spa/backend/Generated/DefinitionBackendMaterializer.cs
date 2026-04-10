using System.Text.Json;

namespace SpaApi.Generated;

public sealed class DefinitionBackendMaterializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public DefinitionBackendModel Materialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Backend definition JSON is empty.");
        }

        var document = JsonSerializer.Deserialize<DefinitionBackendDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("Backend definition JSON could not be parsed.");

        if (document.SchemaVersion != 1)
        {
            throw new InvalidOperationException("Backend definition schema_version must be 1.");
        }

        var tier = RequireString(document.Tier, "tier");
        var template = RequireString(document.Template, "template");
        var persistence = document.Persistence
            ?? throw new InvalidOperationException("Backend definition is missing required object property 'persistence'.");
        var security = document.Security
            ?? throw new InvalidOperationException("Backend definition is missing required object property 'security'.");
        var seed = document.Seed
            ?? throw new InvalidOperationException("Backend definition is missing required object property 'seed'.");

        var orm = RequireString(persistence.Orm, "persistence.orm");
        var database = RequireString(persistence.Database, "persistence.database");
        var modules = NormalizeStringList(document.Modules);
        var entities = NormalizeStringList(document.Entities);
        var requireAdminSeed = seed.AdminAccount;
        var authRequired = security.AuthRequired;

        if ((string.Equals(tier, "N2", StringComparison.Ordinal) || string.Equals(tier, "N3", StringComparison.Ordinal))
            && !authRequired)
        {
            throw new InvalidOperationException($"Backend tier '{tier}' requires security.auth_required to be true.");
        }

        var securityBaseline = authRequired ? "authentication" : "public";

        return tier switch
        {
            "N1" => new DefinitionBackendModel(
                Tier: tier,
                Template: template,
                Orm: orm,
                Database: database,
                AuthenticationEnabled: authRequired,
                RequireAdminSeed: requireAdminSeed,
                SeedSampleProducts: seed.SampleProducts,
                SeedSampleCategories: seed.SampleCategories,
                SecurityBaseline: securityBaseline,
                Entities: entities,
                Modules: modules.Count == 0 ? ["public"] : modules),
            "N2" => new DefinitionBackendModel(
                Tier: tier,
                Template: template,
                Orm: orm,
                Database: database,
                AuthenticationEnabled: authRequired,
                RequireAdminSeed: requireAdminSeed,
                SeedSampleProducts: seed.SampleProducts,
                SeedSampleCategories: seed.SampleCategories,
                SecurityBaseline: "authentication",
                Entities: entities,
                Modules: modules.Count == 0 ? ["authentication", "commerce"] : modules),
            "N3" => new DefinitionBackendModel(
                Tier: tier,
                Template: template,
                Orm: orm,
                Database: database,
                AuthenticationEnabled: authRequired,
                RequireAdminSeed: requireAdminSeed,
                SeedSampleProducts: seed.SampleProducts,
                SeedSampleCategories: seed.SampleCategories,
                SecurityBaseline: "authentication",
                Entities: entities,
                Modules: modules.Count == 0 ? ["authentication", "commerce"] : modules),
            _ => throw new NotSupportedException($"Unsupported backend tier '{tier}'.")
        };
    }

    private static string RequireString(string? value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Backend definition property '{propertyName}' cannot be empty.");
        }

        return value;
    }

    private static IReadOnlyList<string> NormalizeStringList(IReadOnlyList<string>? values)
    {
        if (values == null || values.Count == 0)
        {
            return [];
        }

        return values.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray();
    }
}
