namespace BrokerCore.Services;

public class LlmProxyOptions
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "ollama";
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ApiKey { get; set; } = string.Empty;
    public string ApiFormat { get; set; } = "chat";
    public string DefaultModel { get; set; } = "llama3.1";
    public bool AllowModelOverride { get; set; }
    public bool SupportsToolCalling { get; set; } = true;
    public bool StreamingEnabled { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
}
