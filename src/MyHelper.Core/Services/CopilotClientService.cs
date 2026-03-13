using GitHub.Copilot.SDK;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyHelper.Core.Config;

namespace MyHelper.Core.Services;

/// <summary>Singleton IHostedService that owns the CopilotClient lifecycle.</summary>
public sealed class CopilotClientService : IHostedService, IAsyncDisposable
{
    private readonly AppOptions _options;
    private readonly ILogger<CopilotClientService> _logger;
    private CopilotClient? _client;

    public CopilotClientService(IOptions<AppOptions> options, ILogger<CopilotClientService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public CopilotClient Client => _client
        ?? throw new InvalidOperationException("CopilotClient has not been started.");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var clientOptions = new CopilotClientOptions
        {
            UseLoggedInUser = true,
        };

        if (!string.IsNullOrWhiteSpace(_options.CliUrl))
            clientOptions.CliUrl = _options.CliUrl;

        if (!string.IsNullOrWhiteSpace(_options.GitHubToken))
            clientOptions.GitHubToken = _options.GitHubToken;

        _client = new CopilotClient(clientOptions);
        await _client.StartAsync(cancellationToken);
        _logger.LogInformation("CopilotClient started (CliUrl={CliUrl})",
            _options.CliUrl ?? "auto");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            await _client.StopAsync();
            _logger.LogInformation("CopilotClient stopped.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
    }
}
