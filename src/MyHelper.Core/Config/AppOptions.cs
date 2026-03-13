namespace MyHelper.Core.Config;

public sealed class AppOptions
{
    public const string Section = "MyHelper";

    /// <summary>Connect to an already-running Copilot CLI server (e.g. "localhost:4321").
    /// When null the SDK spawns and manages the process itself.</summary>
    public string? CliUrl { get; set; }

    /// <summary>Optional GitHub PAT. When null the SDK uses the logged-in user's token.</summary>
    public string? GitHubToken { get; set; }

    public string DefaultModel { get; set; } = "gpt-4.1";

    public string SystemMessage { get; set; } =
        "You are MyHelper, a local AI assistant. Be concise, direct, and helpful.";

    /// <summary>Named MCP servers made available to every session.</summary>
    public Dictionary<string, McpServerOptions> McpServers { get; set; } = [];

    /// <summary>Shell commands the ShellCommandTool is permitted to run.
    /// When empty the allow-list is disabled and all commands are allowed.</summary>
    public string[] ShellCommandAllowList { get; set; } = [];
}

public sealed class McpServerOptions
{
    /// <summary>"http" for remote servers, "local" for stdio process servers.</summary>
    public string Type { get; set; } = "http";

    // Remote
    public string? Url { get; set; }

    // Local (stdio)
    public string? Command { get; set; }
    public string[]? Args { get; set; }
    public Dictionary<string, string>? Env { get; set; }
}
