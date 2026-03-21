namespace Broker.Services;

public class HighLevelLlmOptions
{
    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "ollama";
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ApiKey { get; set; } = string.Empty;
    public string ApiFormat { get; set; } = "chat";
    public string DefaultModel { get; set; } = "glm-4.7-flash:latest";
    public bool AllowModelOverride { get; set; }
    public bool SupportsToolCalling { get; set; }
    public bool StreamingEnabled { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
}
