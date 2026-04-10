using System.Text.Json.Serialization;

namespace SpaApi.Generated;

public sealed record DefinitionBackendDocument(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("tier")] string Tier,
    [property: JsonPropertyName("template")] string Template,
    [property: JsonPropertyName("persistence")] DefinitionBackendPersistence Persistence,
    [property: JsonPropertyName("security")] DefinitionBackendSecurity Security,
    [property: JsonPropertyName("entities")] IReadOnlyList<string>? Entities,
    [property: JsonPropertyName("modules")] IReadOnlyList<string>? Modules,
    [property: JsonPropertyName("seed")] DefinitionBackendSeed Seed);

public sealed record DefinitionBackendPersistence(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("orm")] string Orm,
    [property: JsonPropertyName("database")] string Database);

public sealed record DefinitionBackendSecurity(
    [property: JsonPropertyName("auth_required")] bool AuthRequired,
    [property: JsonPropertyName("auth_mode")] string AuthMode,
    [property: JsonPropertyName("role_model")] IReadOnlyList<string>? RoleModel);

public sealed record DefinitionBackendSeed(
    [property: JsonPropertyName("admin_account")] bool AdminAccount,
    [property: JsonPropertyName("sample_products")] bool SampleProducts,
    [property: JsonPropertyName("sample_categories")] bool SampleCategories);

public sealed record DefinitionBackendModel(
    string Tier,
    string Template,
    string Orm,
    string Database,
    bool AuthenticationEnabled,
    bool RequireAdminSeed,
    bool SeedSampleProducts,
    bool SeedSampleCategories,
    string SecurityBaseline,
    IReadOnlyList<string> Entities,
    IReadOnlyList<string> Modules)
{
    public bool IsAuthenticatedTier => AuthenticationEnabled;
}
