using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyHelper.Core.Config;
using MyHelper.Core.Models;
using MyHelper.Core.Services;

namespace MyHelper.Core.Tests;

public sealed class MemoryServiceRegressionTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"myhelper-memory-tests-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Remember_DedupesNormalizedText()
    {
        var sut = CreateService();

        await sut.RememberAsync(new CreateMemoryRequestDto("  Hello   World  ", "fact"));
        await sut.RememberAsync(new CreateMemoryRequestDto("hello world", "fact"));
        var items = await sut.SearchAsync("hello", "fact", 10);

        Assert.Single(items);
        Assert.Equal("fact", items[0].Type);
    }

    [Fact]
    public async Task Search_ReturnsStoredMemory()
    {
        var sut = CreateService();
        await sut.RememberAsync(new CreateMemoryRequestDto("azure functions docs", "fact"));

        var items = await sut.SearchAsync("azure", null, 10);

        Assert.Single(items);
        Assert.Contains("azure", items[0].CanonicalText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_ThrowsConflictWhenCanonicalTargetExists()
    {
        var sut = CreateService();
        var one = await sut.RememberAsync(new CreateMemoryRequestDto("first topic", "fact"));
        var two = await sut.RememberAsync(new CreateMemoryRequestDto("second topic", "fact"));

        await Assert.ThrowsAsync<MemoryConflictException>(() =>
            sut.UpdateAsync(two.Id, new UpdateMemoryRequestDto(Text: one.CanonicalText)));
    }

    [Fact]
    public async Task Archive_HidesMemoryFromSearch_AndIsIdempotent()
    {
        var sut = CreateService();
        var saved = await sut.RememberAsync(new CreateMemoryRequestDto("archive me", "fact"));

        var firstArchive = await sut.ArchiveAsync(saved.Id);
        var secondArchive = await sut.ArchiveAsync(saved.Id);
        var items = await sut.SearchAsync("archive", null, 10);

        Assert.True(firstArchive);
        Assert.False(secondArchive);
        Assert.Empty(items);
    }

    private MemoryService CreateService()
    {
        var options = Options.Create(new AppOptions
        {
            Memory = new MemoryOptions
            {
                DatabasePath = _dbPath,
                AutoCaptureEnabled = true,
                AutoCaptureThreshold = 0.92,
                MaxSearchResults = 50
            }
        });

        return new MemoryService(options, NullLogger<MemoryService>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}
