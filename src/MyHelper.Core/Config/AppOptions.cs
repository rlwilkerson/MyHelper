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

    /// <summary>SQLite-backed long-term memory settings.</summary>
    public MemoryOptions Memory { get; set; } = new();
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

public sealed class MemoryOptions
{
    /// <summary>SQLite database file path. Relative paths resolve under LocalApplicationData\MyHelper.</summary>
    public string DatabasePath { get; set; } = "memory.db";

    /// <summary>When true, prompt/response exchanges may be auto-captured as memories.</summary>
    public bool AutoCaptureEnabled { get; set; } = true;

    /// <summary>Minimum confidence required for auto-capture.</summary>
    public double AutoCaptureThreshold { get; set; } = 0.92;

    /// <summary>Maximum number of memories returned from search/list operations.</summary>
    public int MaxSearchResults { get; set; } = 50;
}
