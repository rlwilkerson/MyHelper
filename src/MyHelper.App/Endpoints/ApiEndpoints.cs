using Microsoft.AspNetCore.Http.HttpResults;
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
}
