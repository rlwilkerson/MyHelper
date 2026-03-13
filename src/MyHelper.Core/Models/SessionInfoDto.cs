namespace MyHelper.Core.Models;

public sealed record SessionInfoDto(
    string SessionId,
    DateTimeOffset CreatedAt,
    string? Model = null
);
