namespace MyHelper.Core.Models;

public sealed record CreateSessionDto(
    string? SessionId = null,
    string? Model = null,
    string? SystemMessage = null,
    Dictionary<string, string>? McpServers = null
);
