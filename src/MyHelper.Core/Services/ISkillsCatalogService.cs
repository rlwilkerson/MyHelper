using MyHelper.Core.Models;

namespace MyHelper.Core.Services;

public interface ISkillsCatalogService
{
    IReadOnlyList<SkillSummaryDto> ListSkills();

    SkillInvocationDto BuildInvocationPrompt(string skillId, string input);
}
