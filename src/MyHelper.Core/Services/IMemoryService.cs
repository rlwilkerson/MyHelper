using MyHelper.Core.Models;

namespace MyHelper.Core.Services;

public interface IMemoryService
{
    Task<MemoryItemDto> RememberAsync(CreateMemoryRequestDto request, CancellationToken ct = default);

    Task<IReadOnlyList<MemoryItemDto>> SearchAsync(
        string? query,
        string? type,
        int? limit,
        CancellationToken ct = default);

    Task<MemoryItemDto?> UpdateAsync(string id, UpdateMemoryRequestDto request, CancellationToken ct = default);

    Task<bool> ArchiveAsync(string id, CancellationToken ct = default);

    Task<int> AutoCaptureAsync(string sessionId, string userPrompt, string assistantResponse, CancellationToken ct = default);
}
