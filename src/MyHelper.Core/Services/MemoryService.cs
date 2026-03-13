using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyHelper.Core.Config;
using MyHelper.Core.Models;

namespace MyHelper.Core.Services;

public sealed class MemoryService : IMemoryService
{
    private readonly ILogger<MemoryService> _logger;
    private readonly MemoryOptions _options;
    private readonly string _dbPath;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public MemoryService(IOptions<AppOptions> options, ILogger<MemoryService> logger)
    {
        _logger = logger;
        _options = options.Value.Memory;
        _dbPath = ResolveDatabasePath(_options.DatabasePath);
    }

    public async Task<MemoryItemDto> RememberAsync(CreateMemoryRequestDto request, CancellationToken ct = default)
        => await RememberInternalAsync(request, "user", "manual", ct);

    private async Task<MemoryItemDto> RememberInternalAsync(
        CreateMemoryRequestDto request,
        string evidenceRole,
        string capturedBy,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("Memory text is required.", nameof(request));

        await EnsureInitializedAsync(ct);

        var normalizedText = NormalizeText(request.Text);
        var normalizedHash = ComputeHash(normalizedText);
        var now = DateTimeOffset.UtcNow;
        var type = string.IsNullOrWhiteSpace(request.Type) ? "fact" : request.Type.Trim().ToLowerInvariant();
        var confidence = Clamp(request.Confidence ?? 0.99);
        var salience = Clamp(request.Salience ?? 0.90);
        var tags = NormalizeTags(request.Tags);

        await using var conn = OpenConnection();
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        var insert = conn.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO memories (
                id, canonical_text, normalized_hash, type, salience, confidence, status,
                first_seen_at, last_seen_at, created_at, updated_at
            ) VALUES (
                $id, $text, $hash, $type, $salience, $confidence, 'active',
                $now, $now, $now, $now
            )
            ON CONFLICT(normalized_hash, type) DO UPDATE SET
                canonical_text = excluded.canonical_text,
                last_seen_at = excluded.last_seen_at,
                updated_at = excluded.updated_at,
                confidence = CASE WHEN memories.confidence > excluded.confidence THEN memories.confidence ELSE excluded.confidence END,
                salience = CASE WHEN memories.salience > excluded.salience THEN memories.salience ELSE excluded.salience END,
                status = 'active';
            """;
        insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        insert.Parameters.AddWithValue("$text", request.Text.Trim());
        insert.Parameters.AddWithValue("$hash", normalizedHash);
        insert.Parameters.AddWithValue("$type", type);
        insert.Parameters.AddWithValue("$salience", salience);
        insert.Parameters.AddWithValue("$confidence", confidence);
        insert.Parameters.AddWithValue("$now", now.ToString("O"));
        await insert.ExecuteNonQueryAsync(ct);

        var idQuery = conn.CreateCommand();
        idQuery.Transaction = tx;
        idQuery.CommandText = """
            SELECT id FROM memories
            WHERE normalized_hash = $hash AND type = $type
            LIMIT 1;
            """;
        idQuery.Parameters.AddWithValue("$hash", normalizedHash);
        idQuery.Parameters.AddWithValue("$type", type);
        var memoryId = (string?)await idQuery.ExecuteScalarAsync(ct)
            ?? throw new InvalidOperationException("Memory upsert did not return an id.");

        await ReplaceTagsAsync(conn, tx, memoryId, tags, ct);
        await InsertEvidenceAsync(conn, tx, memoryId, request.SourceSessionId, evidenceRole, request.Text, capturedBy, ct);
        await RefreshSearchAsync(conn, tx, memoryId, ct);

        await tx.CommitAsync(ct);

        var memory = await GetByIdAsync(memoryId, ct);
        return memory ?? throw new InvalidOperationException($"Memory '{memoryId}' was not found after save.");
    }

    public async Task<IReadOnlyList<MemoryItemDto>> SearchAsync(
        string? query,
        string? type,
        int? limit,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var normalizedType = string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToLowerInvariant();
        var take = Math.Clamp(limit ?? _options.MaxSearchResults, 1, 200);
        var results = new List<MemoryItemDto>();

        await using var conn = OpenConnection();

        if (string.IsNullOrWhiteSpace(query))
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, canonical_text, type, confidence, salience, status, first_seen_at, last_seen_at
                FROM memories
                WHERE status != 'archived'
                  AND ($type IS NULL OR type = $type)
                ORDER BY last_seen_at DESC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$type", (object?)normalizedType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$limit", take);
            await ReadMemoriesAsync(conn, cmd, results, ct);
            return results;
        }

        try
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT m.id, m.canonical_text, m.type, m.confidence, m.salience, m.status, m.first_seen_at, m.last_seen_at
                FROM memory_search ms
                JOIN memories m ON m.id = ms.memory_id
                WHERE ms.memory_search MATCH $query
                  AND m.status != 'archived'
                  AND ($type IS NULL OR m.type = $type)
                ORDER BY m.last_seen_at DESC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$query", query!.Trim());
            cmd.Parameters.AddWithValue("$type", (object?)normalizedType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$limit", take);
            await ReadMemoriesAsync(conn, cmd, results, ct);
            return results;
        }
        catch (SqliteException)
        {
            var fallback = conn.CreateCommand();
            fallback.CommandText = """
                SELECT id, canonical_text, type, confidence, salience, status, first_seen_at, last_seen_at
                FROM memories
                WHERE status != 'archived'
                  AND ($type IS NULL OR type = $type)
                  AND canonical_text LIKE $pattern
                ORDER BY last_seen_at DESC
                LIMIT $limit;
                """;
            fallback.Parameters.AddWithValue("$pattern", $"%{query!.Trim()}%");
            fallback.Parameters.AddWithValue("$type", (object?)normalizedType ?? DBNull.Value);
            fallback.Parameters.AddWithValue("$limit", take);
            await ReadMemoriesAsync(conn, fallback, results, ct);
            return results;
        }
    }

    public async Task<MemoryItemDto?> UpdateAsync(string id, UpdateMemoryRequestDto request, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var existing = await GetByIdAsync(id, ct);
        if (existing is null)
            return null;

        var now = DateTimeOffset.UtcNow;
        var text = string.IsNullOrWhiteSpace(request.Text) ? existing.CanonicalText : request.Text.Trim();
        var type = string.IsNullOrWhiteSpace(request.Type) ? existing.Type : request.Type.Trim().ToLowerInvariant();
        var status = string.IsNullOrWhiteSpace(request.Status) ? existing.Status : request.Status.Trim().ToLowerInvariant();
        var confidence = request.Confidence.HasValue ? Clamp(request.Confidence.Value) : existing.Confidence;
        var salience = request.Salience.HasValue ? Clamp(request.Salience.Value) : existing.Salience;
        var tags = request.Tags is null ? null : NormalizeTags(request.Tags);
        var hash = ComputeHash(NormalizeText(text));

        await using var conn = OpenConnection();
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        var conflictCheck = conn.CreateCommand();
        conflictCheck.Transaction = tx;
        conflictCheck.CommandText = """
            SELECT id
            FROM memories
            WHERE normalized_hash = $hash
              AND type = $type
              AND id != $id
              AND status != 'archived'
            LIMIT 1;
            """;
        conflictCheck.Parameters.AddWithValue("$hash", hash);
        conflictCheck.Parameters.AddWithValue("$type", type);
        conflictCheck.Parameters.AddWithValue("$id", id);
        var conflictId = await conflictCheck.ExecuteScalarAsync(ct);
        if (conflictId is not null)
            throw new MemoryConflictException("A memory with the same normalized text and type already exists.");

        var update = conn.CreateCommand();
        update.Transaction = tx;
        update.CommandText = """
            UPDATE memories
            SET canonical_text = $text,
                normalized_hash = $hash,
                type = $type,
                status = $status,
                confidence = $confidence,
                salience = $salience,
                updated_at = $now
            WHERE id = $id;
            """;
        update.Parameters.AddWithValue("$id", id);
        update.Parameters.AddWithValue("$text", text);
        update.Parameters.AddWithValue("$hash", hash);
        update.Parameters.AddWithValue("$type", type);
        update.Parameters.AddWithValue("$status", status);
        update.Parameters.AddWithValue("$confidence", confidence);
        update.Parameters.AddWithValue("$salience", salience);
        update.Parameters.AddWithValue("$now", now.ToString("O"));
        await update.ExecuteNonQueryAsync(ct);

        if (tags is not null)
            await ReplaceTagsAsync(conn, tx, id, tags, ct);

        await RefreshSearchAsync(conn, tx, id, ct);
        await tx.CommitAsync(ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task<bool> ArchiveAsync(string id, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = OpenConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE memories
            SET status = 'archived',
                updated_at = $now
            WHERE id = $id AND status != 'archived';
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    public async Task<int> AutoCaptureAsync(string sessionId, string userPrompt, string assistantResponse, CancellationToken ct = default)
    {
        if (!_options.AutoCaptureEnabled)
            return 0;

        var candidates = ExtractCandidates(userPrompt, "user")
            .Concat(ExtractCandidates(assistantResponse, "assistant"))
            .Where(c => c.confidence >= _options.AutoCaptureThreshold)
            .Take(5)
            .ToList();

        var saved = 0;
        foreach (var candidate in candidates)
        {
            var dto = new CreateMemoryRequestDto(
                candidate.text,
                candidate.type,
                sessionId,
                candidate.tags,
                candidate.confidence,
                candidate.salience);

            await RememberInternalAsync(dto, candidate.role, "auto", ct);
            saved++;
        }

        if (saved > 0)
        {
            _logger.LogInformation(
                "Auto-captured {Count} memories for session {SessionId}",
                saved,
                sessionId);
        }

        return saved;
    }

    private async Task<MemoryItemDto?> GetByIdAsync(string id, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);

        await using var conn = OpenConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, canonical_text, type, confidence, salience, status, first_seen_at, last_seen_at
            FROM memories
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        var tags = await GetTagsAsync(conn, id, ct);
        return MapMemory(reader, tags);
    }

    private async Task ReadMemoriesAsync(
        SqliteConnection conn,
        SqliteCommand command,
        ICollection<MemoryItemDto> target,
        CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetString(0);
            var tags = await GetTagsAsync(conn, id, ct);
            target.Add(MapMemory(reader, tags));
        }
    }

    private static MemoryItemDto MapMemory(SqliteDataReader reader, IReadOnlyList<string> tags)
    {
        return new MemoryItemDto(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetDouble(3),
            reader.GetDouble(4),
            reader.GetString(5),
            DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
            tags);
    }

    private async Task<IReadOnlyList<string>> GetTagsAsync(SqliteConnection conn, string memoryId, CancellationToken ct)
    {
        var tags = new List<string>();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT tag FROM memory_tags WHERE memory_id = $id ORDER BY tag;";
        cmd.Parameters.AddWithValue("$id", memoryId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            tags.Add(reader.GetString(0));
        return tags;
    }

    private async Task ReplaceTagsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string memoryId,
        IReadOnlyList<string> tags,
        CancellationToken ct)
    {
        var delete = conn.CreateCommand();
        delete.Transaction = tx;
        delete.CommandText = "DELETE FROM memory_tags WHERE memory_id = $id;";
        delete.Parameters.AddWithValue("$id", memoryId);
        await delete.ExecuteNonQueryAsync(ct);

        foreach (var tag in tags)
        {
            var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO memory_tags(memory_id, tag) VALUES ($id, $tag);";
            insert.Parameters.AddWithValue("$id", memoryId);
            insert.Parameters.AddWithValue("$tag", tag);
            await insert.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task InsertEvidenceAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string memoryId,
        string? sessionId,
        string role,
        string excerpt,
        string capturedBy,
        CancellationToken ct)
    {
        var trimmed = excerpt.Trim();
        if (trimmed.Length > 500)
            trimmed = trimmed[..500];

        var insert = conn.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO memory_evidence(id, memory_id, session_id, role, message_excerpt, turn_index, captured_by, created_at)
            VALUES ($id, $memoryId, $sessionId, $role, $excerpt, NULL, $capturedBy, $now);
            """;
        insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        insert.Parameters.AddWithValue("$memoryId", memoryId);
        insert.Parameters.AddWithValue("$sessionId", (object?)sessionId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$role", role);
        insert.Parameters.AddWithValue("$excerpt", trimmed);
        insert.Parameters.AddWithValue("$capturedBy", capturedBy);
        insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await insert.ExecuteNonQueryAsync(ct);
    }

    private async Task RefreshSearchAsync(SqliteConnection conn, SqliteTransaction tx, string memoryId, CancellationToken ct)
    {
        var tags = new List<string>();
        var tagQuery = conn.CreateCommand();
        tagQuery.Transaction = tx;
        tagQuery.CommandText = "SELECT tag FROM memory_tags WHERE memory_id = $id ORDER BY tag;";
        tagQuery.Parameters.AddWithValue("$id", memoryId);
        await using (var tagReader = await tagQuery.ExecuteReaderAsync(ct))
        {
            while (await tagReader.ReadAsync(ct))
                tags.Add(tagReader.GetString(0));
        }

        string canonicalText;
        var textQuery = conn.CreateCommand();
        textQuery.Transaction = tx;
        textQuery.CommandText = "SELECT canonical_text FROM memories WHERE id = $id;";
        textQuery.Parameters.AddWithValue("$id", memoryId);
        canonicalText = (string)(await textQuery.ExecuteScalarAsync(ct)
            ?? throw new InvalidOperationException($"Memory '{memoryId}' missing canonical text."));

        var delete = conn.CreateCommand();
        delete.Transaction = tx;
        delete.CommandText = "DELETE FROM memory_search WHERE memory_id = $id;";
        delete.Parameters.AddWithValue("$id", memoryId);
        await delete.ExecuteNonQueryAsync(ct);

        var insert = conn.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO memory_search(memory_id, canonical_text, tags)
            VALUES ($id, $text, $tags);
            """;
        insert.Parameters.AddWithValue("$id", memoryId);
        insert.Parameters.AddWithValue("$text", canonicalText);
        insert.Parameters.AddWithValue("$tags", string.Join(' ', tags));
        await insert.ExecuteNonQueryAsync(ct);
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
            return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized)
                return;

            await using var conn = OpenConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS memories (
                    id TEXT PRIMARY KEY,
                    canonical_text TEXT NOT NULL,
                    normalized_hash TEXT NOT NULL,
                    type TEXT NOT NULL,
                    salience REAL NOT NULL,
                    confidence REAL NOT NULL,
                    status TEXT NOT NULL,
                    first_seen_at TEXT NOT NULL,
                    last_seen_at TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_memories_hash ON memories(normalized_hash, type, status);
                CREATE UNIQUE INDEX IF NOT EXISTS ux_memories_hash_type ON memories(normalized_hash, type);
                CREATE INDEX IF NOT EXISTS idx_memories_last_seen ON memories(last_seen_at DESC);

                CREATE TABLE IF NOT EXISTS memory_evidence (
                    id TEXT PRIMARY KEY,
                    memory_id TEXT NOT NULL,
                    session_id TEXT,
                    role TEXT NOT NULL,
                    message_excerpt TEXT NOT NULL,
                    turn_index INTEGER,
                    captured_by TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    FOREIGN KEY(memory_id) REFERENCES memories(id)
                );

                CREATE TABLE IF NOT EXISTS memory_tags (
                    memory_id TEXT NOT NULL,
                    tag TEXT NOT NULL,
                    PRIMARY KEY (memory_id, tag),
                    FOREIGN KEY(memory_id) REFERENCES memories(id)
                );

                CREATE TABLE IF NOT EXISTS memory_links (
                    from_memory_id TEXT NOT NULL,
                    to_memory_id TEXT NOT NULL,
                    relation TEXT NOT NULL,
                    PRIMARY KEY (from_memory_id, to_memory_id, relation)
                );

                CREATE TABLE IF NOT EXISTS memory_feedback (
                    id TEXT PRIMARY KEY,
                    memory_id TEXT NOT NULL,
                    feedback TEXT NOT NULL,
                    reason TEXT,
                    created_at TEXT NOT NULL,
                    FOREIGN KEY(memory_id) REFERENCES memories(id)
                );

                CREATE VIRTUAL TABLE IF NOT EXISTS memory_search USING fts5(
                    memory_id UNINDEXED,
                    canonical_text,
                    tags
                );
                """;
            await cmd.ExecuteNonQueryAsync(ct);

            _initialized = true;
            _logger.LogInformation("Memory database initialized at {Path}", _dbPath);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadWriteCreate;Cache=Shared;Default Timeout=10");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout = 10000;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    private static string ResolveDatabasePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
            return configuredPath;

        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyHelper");
        Directory.CreateDirectory(baseDir);
        return Path.Combine(baseDir, configuredPath);
    }

    private static string NormalizeText(string input)
    {
        var trimmed = input.Trim().ToLowerInvariant();
        var sb = new StringBuilder(trimmed.Length);
        var previousWasSpace = false;
        foreach (var ch in trimmed)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (previousWasSpace)
                    continue;
                sb.Append(' ');
                previousWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                previousWasSpace = false;
            }
        }
        return sb.ToString();
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private static double Clamp(double value) => Math.Min(1.0, Math.Max(0.0, value));

    private static string[] NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags is null)
            return [];

        return tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Take(16)
            .ToArray();
    }

    private static IEnumerable<(string text, string type, double confidence, double salience, string[] tags, string role)> ExtractCandidates(string message, string role)
    {
        if (string.IsNullOrWhiteSpace(message))
            yield break;

        var lines = message
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length >= 8 && line.Length <= 280);

        foreach (var line in lines)
        {
            var lower = line.ToLowerInvariant();
            if (lower.StartsWith("remember "))
            {
                yield return (line[9..].Trim(), "fact", 0.99, 0.95, ["explicit", role], role);
                continue;
            }

            var isPreference = lower.Contains("i prefer") || lower.Contains("i like") || lower.Contains("i don't like");
            var isProfile = lower.Contains("my name is") || lower.StartsWith("i am ") || lower.Contains("timezone");
            var isProject = lower.Contains("project") || lower.Contains("deadline") || lower.Contains("repository");

            if (isPreference)
                yield return (line, "preference", 0.95, 0.90, ["auto", role, "preference"], role);
            else if (isProfile)
                yield return (line, "profile", 0.95, 0.90, ["auto", role, "profile"], role);
            else if (isProject)
                yield return (line, "project", 0.92, 0.88, ["auto", role, "project"], role);
        }
    }
}

public sealed class MemoryConflictException : Exception
{
    public MemoryConflictException(string message) : base(message)
    {
    }
}
