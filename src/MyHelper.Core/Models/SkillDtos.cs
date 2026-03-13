namespace MyHelper.Core.Models;

public sealed record SkillSummaryDto(
    string Id,
    string DisplayName,
    string Description
);

public sealed record BuildSkillPromptRequestDto(
    string SkillId,
    string Input
);

public sealed record SkillInvocationDto(
    string SkillId,
    string DisplayName,
    string Prompt,
    IReadOnlyList<string> McpServers
);
