using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using MyHelper.Core.Config;
using MyHelper.Core.Models;
using MyHelper.Core.Services;

namespace MyHelper.App.Endpoints;

public static class ApiEndpoints
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapSessionEndpoints();
        api.MapModelEndpoints();
        api.MapToolEndpoints();
        api.MapMcpServerEndpoints();
        api.MapMemoryEndpoints();
        api.MapHealthEndpoints();

        return app;
    }

    private static RouteGroupBuilder MapSessionEndpoints(this RouteGroupBuilder api)
    {
        var sessions = api.MapGroup("/sessions");

        sessions.MapPost("/", async Task<Ok<object>> (CreateSessionDto dto, ISessionManager sessionManager, CancellationToken ct) =>
        {
            var sessionId = await sessionManager.CreateSessionAsync(dto, ct);
            return TypedResults.Ok((object)new { sessionId });
        });

        sessions.MapPost("/{id}/resume", async Task<Ok<object>> (string id, ISessionManager sessionManager, CancellationToken ct) =>
        {
            var sessionId = await sessionManager.ResumeSessionAsync(id, ct);
            return TypedResults.Ok((object)new { sessionId });
        });

        sessions.MapDelete("/{id}", async Task<NoContent> (string id, ISessionManager sessionManager, CancellationToken ct) =>
        {
            await sessionManager.DeleteSessionAsync(id, ct);
            return TypedResults.NoContent();
        });

        sessions.MapGet("/", Ok<object> (ISessionManager sessionManager) =>
            TypedResults.Ok((object)sessionManager.GetActiveSessions()));

        return sessions;
    }

    private static RouteGroupBuilder MapModelEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/models", async Task<Ok<object>> (CopilotClientService copilot, CancellationToken ct) =>
        {
            var modelList = await copilot.Client.ListModelsAsync(ct);
            var models = modelList.Select(m => new { m.Id, m.Name }).ToArray();
            return TypedResults.Ok((object)new { models });
        });

        return api;
    }

    private static RouteGroupBuilder MapToolEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/tools", Ok<object> (IToolRegistry registry) =>
        {
            var tools = registry.GetAll()
                .Select(t => new { name = t.Name, description = t.Description })
                .ToArray();
            return TypedResults.Ok((object)tools);
        });

        return api;
    }

    private static RouteGroupBuilder MapHealthEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/health", Ok<object> (CopilotClientService _) =>
            TypedResults.Ok((object)new { status = "ok", timestamp = DateTimeOffset.UtcNow }));

        return api;
    }

    private static RouteGroupBuilder MapMcpServerEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/mcp-servers", Ok<object> (IOptions<AppOptions> options) =>
        {
            var servers = options.Value.McpServers
                .Select(s => new
                {
                    name = s.Key,
                    type = s.Value.Type,
                    enabledByDefault = true,
                })
                .ToArray();

            return TypedResults.Ok((object)new { servers });
        });

        return api;
    }

    private static RouteGroupBuilder MapMemoryEndpoints(this RouteGroupBuilder api)
    {
        var memories = api.MapGroup("/memories");

        memories.MapPost("/", async Task<Results<Ok<object>, BadRequest<object>>> (CreateMemoryRequestDto request, IMemoryService memoryService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Text))
                return TypedResults.BadRequest((object)new { error = "Memory text is required." });

            var saved = await memoryService.RememberAsync(request, ct);
            return TypedResults.Ok((object)saved);
        });

        memories.MapGet("/", async Task<Ok<object>> (
            string? query,
            string? type,
            int? limit,
            IMemoryService memoryService,
            CancellationToken ct) =>
        {
            var items = await memoryService.SearchAsync(query, type, limit, ct);
            return TypedResults.Ok((object)new { items, count = items.Count });
        });

        memories.MapPatch("/{id}", async Task<Results<Ok<object>, NotFound, BadRequest<object>, Conflict<object>>> (
            string id,
            UpdateMemoryRequestDto request,
            IMemoryService memoryService,
            CancellationToken ct) =>
        {
            try
            {
                var updated = await memoryService.UpdateAsync(id, request, ct);
                return updated is null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok((object)updated);
            }
            catch (ArgumentException ex)
            {
                return TypedResults.BadRequest((object)new { error = ex.Message });
            }
            catch (MemoryConflictException ex)
            {
                return TypedResults.Conflict((object)new { error = ex.Message });
            }
        });

        memories.MapDelete("/{id}", async Task<Results<NoContent, NotFound>> (
            string id,
            IMemoryService memoryService,
            CancellationToken ct) =>
        {
            var archived = await memoryService.ArchiveAsync(id, ct);
            return archived
                ? TypedResults.NoContent()
                : TypedResults.NotFound();
        });

        return api;
    }
}
