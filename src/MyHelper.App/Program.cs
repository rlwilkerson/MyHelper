using System.Diagnostics;
using MyHelper.App.Hubs;
using MyHelper.Core.Extensions;
using MyHelper.Core.Models;
using MyHelper.Core.Services;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ── Services ───────────────────────────────────────────────────────────────
builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddCoreServices(config);

// ── Build ──────────────────────────────────────────────────────────────────
var app = builder.Build();

// Register tool implementations with the IToolRegistry
app.Services.RegisterAllTools();

// ── Startup validation ────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    EnsureCopilotCliAvailable(logger);
}

// ── Middleware ─────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseStaticFiles();
app.UseRouting();

// ── Endpoints ─────────────────────────────────────────────────────────────
app.MapHub<ChatHub>("/hubs/chat");
app.MapRazorPages();

// Sessions
app.MapPost("/api/sessions", async (CreateSessionDto dto, ISessionManager sessions, CancellationToken ct) =>
{
    var sessionId = await sessions.CreateSessionAsync(dto, ct);
    return Results.Ok(new { sessionId });
});

app.MapPost("/api/sessions/{id}/resume", async (string id, ISessionManager sessions, CancellationToken ct) =>
{
    var sessionId = await sessions.ResumeSessionAsync(id, ct);
    return Results.Ok(new { sessionId });
});

app.MapDelete("/api/sessions/{id}", async (string id, ISessionManager sessions, CancellationToken ct) =>
{
    await sessions.DeleteSessionAsync(id, ct);
    return Results.NoContent();
});

app.MapGet("/api/sessions", (ISessionManager sessions) =>
    Results.Ok(sessions.GetActiveSessions()));

// Models
app.MapGet("/api/models", async (CopilotClientService copilot, CancellationToken ct) =>
{
    var modelList = await copilot.Client.ListModelsAsync(ct);
    var models = modelList.Select(m => new { m.Id, m.Name }).ToArray();
    return Results.Ok(new { models });
});

// Tools
app.MapGet("/api/tools", (IToolRegistry registry) =>
{
    var tools = registry.GetAll()
        .Select(t => new { name = t.Name, description = t.Description })
        .ToArray();
    return Results.Ok(tools);
});

// Health
app.MapGet("/api/health", (CopilotClientService copilot) =>
    Results.Ok(new { status = "ok", timestamp = DateTimeOffset.UtcNow }));

app.Run();

// ── Helpers ────────────────────────────────────────────────────────────────
static void EnsureCopilotCliAvailable(ILogger logger)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "copilot",
            Arguments = "--version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi);
        process?.WaitForExit(3000);
        if (process?.ExitCode != 0)
            throw new InvalidOperationException("copilot CLI returned non-zero exit code.");
        logger.LogInformation("GitHub Copilot CLI found: {Version}",
            process.StandardOutput.ReadToEnd().Trim());
    }
    catch (Exception ex) when (ex is not InvalidOperationException)
    {
        logger.LogWarning(
            "GitHub Copilot CLI not found on PATH. " +
            "Install it with: gh extension install github/gh-copilot. " +
            "Error: {Error}", ex.Message);
    }
}

