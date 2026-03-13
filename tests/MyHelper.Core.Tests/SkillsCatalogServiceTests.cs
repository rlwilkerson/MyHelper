using Microsoft.Extensions.Options;
using MyHelper.Core.Config;
using MyHelper.Core.Services;

namespace MyHelper.Core.Tests;

public sealed class SkillsCatalogServiceTests
{
    [Fact]
    public void ListSkills_ReturnsUiSafeMetadata()
    {
        var sut = CreateService();

        var items = sut.ListSkills();

        Assert.Single(items);
        Assert.Equal("ms-learn-search", items[0].Id);
        Assert.Equal("Microsoft Learn", items[0].DisplayName);
        Assert.DoesNotContain("{input}", items[0].Description, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildInvocationPrompt_ComposesPromptAndServers()
    {
        var sut = CreateService();

        var result = sut.BuildInvocationPrompt("ms-learn-search", "how to use minimal apis");

        Assert.Equal("ms-learn-search", result.SkillId);
        Assert.Contains("how to use minimal apis", result.Prompt, StringComparison.Ordinal);
        Assert.Single(result.McpServers);
        Assert.Equal("learn", result.McpServers[0]);
    }

    [Fact]
    public void BuildInvocationPrompt_ThrowsForUnknownSkill()
    {
        var sut = CreateService();

        Assert.Throws<SkillNotFoundException>(() => sut.BuildInvocationPrompt("missing", "input"));
    }

    [Fact]
    public void Ctor_ThrowsWhenSkillReferencesUnknownServer()
    {
        var options = Options.Create(new AppOptions
        {
            McpServers = [],
            Skills = new SkillsCatalogOptions
            {
                Enabled = true,
                Catalog = new Dictionary<string, SkillOptions>
                {
                    ["bad"] = new()
                    {
                        DisplayName = "Bad",
                        Description = "Bad skill",
                        PromptTemplate = "{input}",
                        McpServers = ["does-not-exist"]
                    }
                }
            }
        });

        Assert.Throws<SkillsConfigurationException>(() => new SkillsCatalogService(options));
    }

    private static SkillsCatalogService CreateService()
    {
        var options = Options.Create(new AppOptions
        {
            McpServers = new Dictionary<string, McpServerOptions>
            {
                ["learn"] = new() { Type = "http", Url = "https://learn.microsoft.com/api/mcp" }
            },
            Skills = new SkillsCatalogOptions
            {
                Enabled = true,
                Catalog = new Dictionary<string, SkillOptions>
                {
                    ["ms-learn-search"] = new()
                    {
                        DisplayName = "Microsoft Learn",
                        Description = "Search Microsoft Learn docs",
                        PromptTemplate = "Use Learn MCP. Request: {input}",
                        McpServers = ["learn"]
                    }
                }
            }
        });

        return new SkillsCatalogService(options);
    }
}
