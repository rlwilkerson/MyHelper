using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyHelper.Core.Config;
using MyHelper.Core.Services;
using MyHelper.Tools.Tools;

namespace MyHelper.Core.Extensions;

public static class CoreServiceExtensions
{
    public static IServiceCollection AddCoreServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AppOptions>(configuration.GetSection(AppOptions.Section));

        services.AddSingleton<CopilotClientService>();
        services.AddHostedService(sp => sp.GetRequiredService<CopilotClientService>());

        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<IMemoryService, MemoryService>();
        services.AddSingleton<ISkillsCatalogService, SkillsCatalogService>();

        return services;
    }

    /// <summary>
    /// Registers all built-in tools with the IToolRegistry.
    /// Call after app.Build() so all services are available.
    /// </summary>
    public static IServiceProvider RegisterAllTools(this IServiceProvider provider)
    {
        var registry = provider.GetRequiredService<IToolRegistry>();
        var options = provider.GetRequiredService<IOptions<AppOptions>>().Value;

        registry.Register(FileReadTool.Create());
        registry.Register(FileWriteTool.Create());
        registry.Register(DirectoryListTool.Create());
        registry.Register(ShellCommandTool.Create(options.ShellCommandAllowList));
        registry.Register(HttpFetchTool.Create());
        registry.Register(GitStatusTool.Create());

        return provider;
    }
}
