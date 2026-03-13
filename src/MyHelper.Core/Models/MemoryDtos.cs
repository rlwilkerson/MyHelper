namespace MyHelper.Core.Models;

public sealed record CreateMemoryRequestDto(
    string Text,
    string Type = "fact",
    string? SourceSessionId = null,
    string[]? Tags = null,
    double? Confidence = null,
    double? Salience = null
);

public sealed record UpdateMemoryRequestDto(
    string? Text = null,
    string? Type = null,
    string? Status = null,
    string[]? Tags = null,
    double? Confidence = null,
    double? Salience = null
);

public sealed record MemoryItemDto(
    string Id,
    string CanonicalText,
    string Type,
    double Confidence,
    double Salience,
    string Status,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    IReadOnlyList<string> Tags
);
