namespace MyHelper.Core.Models;

/// <summary>Unified event envelope sent from SessionManager → ChatHub → SignalR client.</summary>
public sealed record ChatEventDto(ChatEventType Type, string SessionId, string? Payload = null);

public enum ChatEventType
{
    MessageDelta,
    MessageComplete,
    ToolStarted,
    ToolCompleted,
    Error,
}
