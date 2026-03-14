using GitHub.Copilot.SDK;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyHelper.Core.Config;

namespace MyHelper.Core.Services;

public sealed class CopilotUnavailableException : InvalidOperationException
{
    public CopilotUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>Singleton IHostedService that owns the CopilotClient lifecycle.</summary>
public sealed class CopilotClientService : IHostedService, IAsyncDisposable
{
    private readonly AppOptions _options;
    private readonly ILogger<CopilotClientService> _logger;
    private CopilotClient? _client;
    private Exception? _startupException;

    public CopilotClientService(IOptions<AppOptions> options, ILogger<CopilotClientService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public CopilotClient Client => _client
        ?? throw CreateUnavailableException();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
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
            _startupException = null;

            _logger.LogInformation("CopilotClient started (CliUrl={CliUrl})",
                _options.CliUrl ?? "auto");
        }
        catch (Exception ex)
        {
            _startupException = ex;

            if (_client is not null)
            {
                await _client.DisposeAsync();
                _client = null;
            }

            _logger.LogError(
                ex,
                "CopilotClient failed to start. The app will continue running, but Copilot-backed endpoints will return unavailable until the SDK starts successfully.");
        }
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

    private CopilotUnavailableException CreateUnavailableException()
    {
        const string message = "GitHub Copilot is unavailable. Check the application logs for the SDK startup failure.";

        return _startupException is null
            ? new CopilotUnavailableException(message)
            : new CopilotUnavailableException(message, _startupException);
    }
}
