using Microsoft.AspNetCore.SignalR;
using MyHelper.Core.Models;
using MyHelper.Core.Services;

namespace MyHelper.App.Hubs;

public sealed class ChatHub : Hub
{
    private readonly ISessionManager _sessions;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(ISessionManager sessions, ILogger<ChatHub> logger)
    {
        _sessions = sessions;
        _logger = logger;
    }

    public async Task SendMessage(string sessionId, string prompt)
    {
        _logger.LogDebug("SendMessage: session={SessionId} promptLen={Len}",
            sessionId, prompt.Length);

        try
        {
            await _sessions.SendAsync(
                sessionId,
                prompt,
                evt => DispatchEvent(evt),
                Context.ConnectionAborted);
        }
        catch (OperationCanceledException)
        {
            // Connection closed — normal.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SendMessage for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("SessionError", sessionId, ex.Message);
        }
    }

    public async Task AbortSession(string sessionId)
    {
        await _sessions.AbortAsync(sessionId, Context.ConnectionAborted);
    }

    private Task DispatchEvent(ChatEventDto evt) => evt.Type switch
    {
        ChatEventType.MessageDelta =>
            Clients.Caller.SendAsync("MessageDelta", evt.SessionId, evt.Payload ?? ""),

        ChatEventType.MessageComplete =>
            Clients.Caller.SendAsync("MessageComplete", evt.SessionId),

        ChatEventType.ToolStarted =>
            Clients.Caller.SendAsync("ToolStarted", evt.SessionId, evt.Payload ?? ""),

        ChatEventType.ToolCompleted =>
            Clients.Caller.SendAsync("ToolCompleted", evt.SessionId, evt.Payload ?? ""),

        ChatEventType.Error =>
            Clients.Caller.SendAsync("SessionError", evt.SessionId, evt.Payload ?? ""),

        _ => Task.CompletedTask,
    };
}
