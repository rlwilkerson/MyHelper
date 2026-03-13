using System.Collections.Concurrent;
using System.Threading.Channels;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyHelper.Core.Config;
using MyHelper.Core.Models;

namespace MyHelper.Core.Services;

public sealed class SessionManager : ISessionManager, IAsyncDisposable
{
    private sealed record SessionEntry(CopilotSession Session, string Model, DateTimeOffset CreatedAt);

    private readonly CopilotClientService _clientService;
    private readonly IToolRegistry _toolRegistry;
    private readonly AppOptions _options;
    private readonly ILogger<SessionManager> _logger;
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();

    public SessionManager(
        CopilotClientService clientService,
        IToolRegistry toolRegistry,
        IOptions<AppOptions> options,
        ILogger<SessionManager> logger)
    {
        _clientService = clientService;
        _toolRegistry = toolRegistry;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> CreateSessionAsync(CreateSessionDto dto, CancellationToken ct = default)
    {
        var model = dto.Model ?? _options.DefaultModel;
        var config = new SessionConfig
        {
            SessionId = dto.SessionId,
            Model = model,
            Streaming = true,
            SystemMessage = new SystemMessageConfig
            {
                Content = dto.SystemMessage ?? _options.SystemMessage,
            },
            Tools = _toolRegistry.GetAll().ToList(),
        };

        BuildMcpServers(config, dto.McpServers);

        var session = await _clientService.Client.CreateSessionAsync(config, ct);
        var entry = new SessionEntry(session, model, DateTimeOffset.UtcNow);
        _sessions[session.SessionId] = entry;

        _logger.LogInformation("Session created: {SessionId} model={Model}", session.SessionId, model);
        return session.SessionId;
    }

    public async Task<string> ResumeSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var resumeConfig = new ResumeSessionConfig
        {
            Model = _options.DefaultModel,
            Streaming = true,
            Tools = _toolRegistry.GetAll().ToList(),
        };

        var session = await _clientService.Client.ResumeSessionAsync(sessionId, resumeConfig, ct);
        var entry = new SessionEntry(session, _options.DefaultModel, DateTimeOffset.UtcNow);
        _sessions[session.SessionId] = entry;

        _logger.LogInformation("Session resumed: {SessionId}", session.SessionId);
        return session.SessionId;
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (_sessions.TryRemove(sessionId, out var entry))
        {
            await entry.Session.DisposeAsync();
            await _clientService.Client.DeleteSessionAsync(sessionId, ct);
            _logger.LogInformation("Session deleted: {SessionId}", sessionId);
        }
    }

    public async Task SendAsync(
        string sessionId,
        string prompt,
        Func<ChatEventDto, Task> onEvent,
        CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry))
            throw new KeyNotFoundException($"Session '{sessionId}' not found.");

        var channel = Channel.CreateUnbounded<ChatEventDto>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
        });

        var consumer = Task.Run(async () =>
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
                await onEvent(evt);
        }, ct);

        using var sub = entry.Session.On(evt =>
        {
            var chatEvent = MapEvent(sessionId, evt);
            if (chatEvent is not null)
                channel.Writer.TryWrite(chatEvent);
        });

        try
        {
            await entry.Session.SendAndWaitAsync(
                new MessageOptions { Prompt = prompt },
                timeout: null,
                cancellationToken: ct);
        }
        finally
        {
            channel.Writer.Complete();
            await consumer;
        }
    }

    public async Task AbortAsync(string sessionId, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(sessionId, out var entry))
            await entry.Session.AbortAsync(ct);
    }

    public IReadOnlyList<SessionInfoDto> GetActiveSessions() =>
        _sessions.Values
            .Select(e => new SessionInfoDto(e.Session.SessionId, e.CreatedAt, e.Model))
            .ToList();

    private static ChatEventDto? MapEvent(string sessionId, SessionEvent evt)
    {
        return evt switch
        {
            AssistantMessageDeltaEvent delta =>
                new ChatEventDto(ChatEventType.MessageDelta, sessionId, delta.Data?.DeltaContent),

            SessionIdleEvent =>
                new ChatEventDto(ChatEventType.MessageComplete, sessionId),

            ToolExecutionStartEvent toolStart =>
                new ChatEventDto(ChatEventType.ToolStarted, sessionId, toolStart.Data?.ToolName),

            ToolExecutionCompleteEvent toolDone =>
                new ChatEventDto(
                    ChatEventType.ToolCompleted,
                    sessionId,
                    toolDone.Data?.Success == true ? "ok" : "error"),

            SessionErrorEvent error =>
                new ChatEventDto(ChatEventType.Error, sessionId, error.Data?.Message),

            _ => null,
        };
    }

    private void BuildMcpServers(SessionConfig config, Dictionary<string, string>? requestServers)
    {
        var servers = new Dictionary<string, object>();

        foreach (var (name, opts) in _options.McpServers)
        {
            servers[name] = opts.Type.Equals("local", StringComparison.OrdinalIgnoreCase)
                ? (object)new McpLocalServerConfig
                {
                    Command = opts.Command ?? string.Empty,
                    Args = opts.Args?.ToList() ?? [],
                    Env = opts.Env,
                }
                : new McpRemoteServerConfig
                {
                    Url = opts.Url ?? string.Empty,
                };
        }

        if (requestServers is not null)
        {
            foreach (var (name, url) in requestServers)
                servers[name] = new McpRemoteServerConfig { Url = url };
        }

        if (servers.Count > 0)
            config.McpServers = servers;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _sessions.Values)
            await entry.Session.DisposeAsync();
        _sessions.Clear();
    }
}
