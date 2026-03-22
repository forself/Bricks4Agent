namespace Broker.Services;

public sealed class AzureIisDeploymentSecretResolverOptions
{
    public Dictionary<string, AzureIisDeploymentSecretEntry> Mappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AzureIisDeploymentSecretEntry
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class AzureIisDeploymentSecret
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public interface IAzureIisDeploymentSecretResolver
{
    AzureIisDeploymentSecret? Resolve(string secretRef);
}

public sealed class AzureIisDeploymentSecretResolver : IAzureIisDeploymentSecretResolver
{
    private readonly AzureIisDeploymentSecretResolverOptions _options;

    public AzureIisDeploymentSecretResolver(AzureIisDeploymentSecretResolverOptions options)
    {
        _options = options;
    }

    public AzureIisDeploymentSecret? Resolve(string secretRef)
    {
        if (string.IsNullOrWhiteSpace(secretRef))
            return null;

        if (_options.Mappings.TryGetValue(secretRef, out var mapped) &&
            !string.IsNullOrWhiteSpace(mapped.UserName) &&
            !string.IsNullOrWhiteSpace(mapped.Password))
        {
            return new AzureIisDeploymentSecret
            {
                UserName = mapped.UserName,
                Password = mapped.Password
            };
        }

        var normalized = Normalize(secretRef);
        var userName = Environment.GetEnvironmentVariable($"BRICKS4AGENT_DEPLOY_SECRET__{normalized}__USERNAME");
        var password = Environment.GetEnvironmentVariable($"BRICKS4AGENT_DEPLOY_SECRET__{normalized}__PASSWORD");
        if (!string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(password))
        {
            return new AzureIisDeploymentSecret
            {
                UserName = userName,
                Password = password
            };
        }

        return null;
    }

    private static string Normalize(string value)
    {
        var chars = value
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_')
            .ToArray();
        return new string(chars);
    }
}
