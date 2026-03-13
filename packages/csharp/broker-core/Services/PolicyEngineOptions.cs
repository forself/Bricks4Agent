namespace BrokerCore.Services;

/// <summary>
/// M-2 修復：外部化政策引擎配置 —— 黑名單不再硬編碼
///
/// 可透過 appsettings.json 的 "PolicyEngine" 區段覆寫。
/// 預設值與原始硬編碼一致，確保零配置可用。
/// </summary>
public class PolicyEngineOptions
{
    /// <summary>指令前綴黑名單（command 以此開頭即攔截）</summary>
    public string[] CommandPrefixBlacklist { get; set; } = new[]
    {
        "rm ", "rm\t", "del ", "del\t",
        "format ", "fdisk ", "mkfs ", "dd ",
        "shutdown", "reboot", "halt",
        "kill ", "killall ", "pkill ",
        "chmod 777", "chown ",
        "curl ", "wget ", "nc ", "ncat ",
        "eval ", "exec ",
    };

    /// <summary>指令精確黑名單（command 完全等於此即攔截）</summary>
    public string[] CommandExactBlacklist { get; set; } = new[]
    {
        "rm", "del", "halt", "reboot", "shutdown"
    };

    /// <summary>SQL 注入黑名單（不區分大小寫的子字串匹配）</summary>
    public string[] SqlBlacklist { get; set; } = new[]
    {
        "DROP TABLE", "DROP DATABASE", "TRUNCATE"
    };

    /// <summary>禁止存取的系統路徑前綴</summary>
    public string[] ForbiddenPathPrefixes { get; set; } = new[]
    {
        "/etc", "/proc", "/sys", "/dev",
        "c:/windows", "c:/system32",
        "/root", "/var/log",
        "/boot", "/sbin", "/usr/sbin"
    };
}
