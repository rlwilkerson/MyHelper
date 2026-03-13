using Microsoft.Extensions.Options;
using MyHelper.Core.Config;
using MyHelper.Core.Models;

namespace MyHelper.Core.Services;

public sealed class SkillsCatalogService : ISkillsCatalogService
{
    private readonly Dictionary<string, SkillDefinition> _definitions;

    public SkillsCatalogService(IOptions<AppOptions> options)
    {
        var appOptions = options.Value;
        _definitions = ValidateAndBuild(appOptions);
    }

    public IReadOnlyList<SkillSummaryDto> ListSkills()
    {
        return _definitions.Values
            .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(s => new SkillSummaryDto(s.Id, s.DisplayName, s.Description))
            .ToArray();
    }

    public SkillInvocationDto BuildInvocationPrompt(string skillId, string input)
    {
        if (string.IsNullOrWhiteSpace(skillId))
            throw new SkillValidationException("Skill id is required.");

        if (string.IsNullOrWhiteSpace(input))
            throw new SkillValidationException("Input is required.");

        var normalizedSkillId = skillId.Trim().ToLowerInvariant();
        if (!_definitions.TryGetValue(normalizedSkillId, out var definition))
            throw new SkillNotFoundException(skillId.Trim());

        var prompt = definition.PromptTemplate.Replace("{input}", input.Trim(), StringComparison.Ordinal);
        return new SkillInvocationDto(definition.Id, definition.DisplayName, prompt, definition.McpServers);
    }

    private static Dictionary<string, SkillDefinition> ValidateAndBuild(AppOptions options)
    {
        if (!options.Skills.Enabled)
            return [];

        var knownServers = options.McpServers.Keys
            .Select(k => k.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var definitions = new Dictionary<string, SkillDefinition>(StringComparer.Ordinal);
        foreach (var item in options.Skills.Catalog)
        {
            var id = item.Key.Trim().ToLowerInvariant();
            var skill = item.Value;

            if (string.IsNullOrWhiteSpace(id))
                throw new SkillsConfigurationException("Skill id cannot be empty.");
            if (skill is null)
                throw new SkillsConfigurationException($"Skill '{id}' configuration is missing.");
            if (string.IsNullOrWhiteSpace(skill.DisplayName))
                throw new SkillsConfigurationException($"Skill '{id}' is missing DisplayName.");
            if (string.IsNullOrWhiteSpace(skill.Description))
                throw new SkillsConfigurationException($"Skill '{id}' is missing Description.");
            if (string.IsNullOrWhiteSpace(skill.PromptTemplate))
                throw new SkillsConfigurationException($"Skill '{id}' is missing PromptTemplate.");
            if (!skill.PromptTemplate.Contains("{input}", StringComparison.Ordinal))
                throw new SkillsConfigurationException($"Skill '{id}' PromptTemplate must contain '{{input}}'.");
            if (skill.McpServers is null || skill.McpServers.Length == 0)
                throw new SkillsConfigurationException($"Skill '{id}' must reference at least one MCP server.");

            var normalizedServers = skill.McpServers
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (normalizedServers.Length == 0)
                throw new SkillsConfigurationException($"Skill '{id}' has no valid MCP server names.");

            foreach (var server in normalizedServers)
            {
                if (!knownServers.Contains(server))
                    throw new SkillsConfigurationException($"Skill '{id}' references unknown MCP server '{server}'.");
            }

            definitions[id] = new SkillDefinition(
                id,
                skill.DisplayName.Trim(),
                skill.Description.Trim(),
                skill.PromptTemplate,
                normalizedServers);
        }

        return definitions;
    }

    private sealed record SkillDefinition(
        string Id,
        string DisplayName,
        string Description,
        string PromptTemplate,
        string[] McpServers);
}

public abstract class SkillsCatalogException : Exception
{
    protected SkillsCatalogException(string message) : base(message)
    {
    }
}

public sealed class SkillsConfigurationException : SkillsCatalogException
{
    public SkillsConfigurationException(string message) : base(message)
    {
    }
}

public sealed class SkillNotFoundException : SkillsCatalogException
{
    public SkillNotFoundException(string skillId) : base($"Skill '{skillId}' was not found.")
    {
    }
}

public sealed class SkillValidationException : SkillsCatalogException
{
    public SkillValidationException(string message) : base(message)
    {
    }
}
