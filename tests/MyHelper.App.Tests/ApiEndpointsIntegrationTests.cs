using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyHelper.Core.Services;

namespace MyHelper.App.Tests;

public sealed class ApiEndpointsIntegrationTests : IClassFixture<MyHelperWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ApiEndpointsIntegrationTests(MyHelperWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SkillsEndpoints_ListAndInvoke_Work()
    {
        var listResponse = await _client.GetAsync("/api/skills");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var listJson = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var skills = listJson.GetProperty("skills");
        Assert.True(skills.GetArrayLength() >= 1);

        var invokeResponse = await _client.PostAsJsonAsync("/api/skills/invoke",
            new { skillId = "ms-learn-search", input = "minimal api route groups" });
        Assert.Equal(HttpStatusCode.OK, invokeResponse.StatusCode);

        var invokeJson = await invokeResponse.Content.ReadFromJsonAsync<JsonElement>();
        var prompt = invokeJson.GetProperty("prompt").GetString();
        Assert.Contains("minimal api route groups", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemoriesEndpoints_HandleCreateConflictAndArchive()
    {
        var first = await _client.PostAsJsonAsync("/api/memories", new
        {
            text = "Memory regression input",
            type = "fact",
            tags = new[] { "regression" }
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstJson = await first.Content.ReadFromJsonAsync<JsonElement>();
        var firstId = firstJson.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(firstId));

        var second = await _client.PostAsJsonAsync("/api/memories", new
        {
            text = "Another memory",
            type = "fact"
        });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var conflict = await _client.PatchAsJsonAsync($"/api/memories/{secondId}", new
        {
            text = "Memory regression input",
            type = "fact"
        });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var archive = await _client.DeleteAsync($"/api/memories/{firstId}");
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);

        var list = await _client.GetFromJsonAsync<JsonElement>("/api/memories?query=regression");
        var count = list.GetProperty("count").GetInt32();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ModelsEndpoint_ReturnsServiceUnavailable_WhenCopilotClientIsNotStarted()
    {
        var response = await _client.GetAsync("/api/models");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("GitHub Copilot unavailable", body.GetProperty("title").GetString());
        Assert.Contains("GitHub Copilot is unavailable", body.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionCreate_ReturnsServiceUnavailable_WhenCopilotClientIsNotStarted()
    {
        var response = await _client.PostAsJsonAsync("/api/sessions", new { });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("GitHub Copilot unavailable", body.GetProperty("title").GetString());
        Assert.Contains("GitHub Copilot is unavailable", body.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }
}

public sealed class MyHelperWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"myhelper-api-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["MyHelper:Memory:DatabasePath"] = _dbPath,
                ["MyHelper:McpServers:learn:Type"] = "http",
                ["MyHelper:McpServers:learn:Url"] = "https://learn.microsoft.com/api/mcp",
                ["MyHelper:Skills:Enabled"] = "true",
                ["MyHelper:Skills:Catalog:ms-learn-search:DisplayName"] = "Microsoft Learn",
                ["MyHelper:Skills:Catalog:ms-learn-search:Description"] = "Search Microsoft Learn docs",
                ["MyHelper:Skills:Catalog:ms-learn-search:PromptTemplate"] = "Use Learn MCP server. Request: {input}",
                ["MyHelper:Skills:Catalog:ms-learn-search:McpServers:0"] = "learn"
            };
            config.AddInMemoryCollection(overrides);
        });

        builder.ConfigureServices(services =>
        {
            var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
            foreach (var descriptor in hostedServices)
                services.Remove(descriptor);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try
        {
            if (disposing && File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}
