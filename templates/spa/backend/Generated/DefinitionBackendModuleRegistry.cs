namespace SpaApi.Generated;

public sealed class DefinitionBackendModuleRegistry
{
    public DefinitionBackendModuleRegistry(DefinitionBackendModel definition)
    {
        Definition = definition;
        ModuleNames = definition.Modules.Count == 0
            ? definition.Tier switch
            {
                "N1" => ["public"],
                "N2" => ["authentication", "commerce"],
                "N3" => ["authentication", "commerce"],
                _ => []
            }
            : definition.Modules;
    }

    public DefinitionBackendModel Definition { get; }

    public IReadOnlyList<string> ModuleNames { get; }

    public bool UsesAuthentication => Definition.AuthenticationEnabled;
}
