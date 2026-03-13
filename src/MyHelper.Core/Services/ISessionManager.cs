using MyHelper.Core.Models;

namespace MyHelper.Core.Services;

public interface ISessionManager
{
    Task<string> CreateSessionAsync(CreateSessionDto dto, CancellationToken ct = default);
    Task<string> ResumeSessionAsync(string sessionId, CancellationToken ct = default);
    Task DeleteSessionAsync(string sessionId, CancellationToken ct = default);
    Task SendAsync(string sessionId, string prompt, Func<ChatEventDto, Task> onEvent, CancellationToken ct = default);
    Task AbortAsync(string sessionId, CancellationToken ct = default);
    IReadOnlyList<SessionInfoDto> GetActiveSessions();
}
