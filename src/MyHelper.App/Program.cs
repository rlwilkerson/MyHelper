using System.Diagnostics;
using Microsoft.Extensions.Options;
using MyHelper.App.Endpoints;
using MyHelper.App.Hubs;
using MyHelper.Core.Config;
using MyHelper.Core.Extensions;
using MyHelper.Core.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
var config = builder.Configuration;

// ── Services ───────────────────────────────────────────────────────────────
builder.Services.AddRazorPages();
builder.Services.AddSignalR();
builder.Services.AddCoreServices(config);

// ── Build ──────────────────────────────────────────────────────────────────
var app = builder.Build();

app.MapDefaultEndpoints();

// Register tool implementations with the IToolRegistry
app.Services.RegisterAllTools();

// ── Startup validation ────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var opts = scope.ServiceProvider.GetRequiredService<IOptions<AppOptions>>().Value;
    EnsureCopilotCliAvailable(logger, opts);
}

// ── Middleware ─────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseStaticFiles();
app.UseRouting();

// ── Endpoints ─────────────────────────────────────────────────────────────
app.MapHub<ChatHub>("/hubs/chat");
app.MapRazorPages();
app.MapApiEndpoints();

app.Run();

// ── Helpers ────────────────────────────────────────────────────────────────
static void EnsureCopilotCliAvailable(ILogger logger, AppOptions options)
{
    // If pointing to an external CLI server, local CLI is not required.
    if (!string.IsNullOrWhiteSpace(options.CliUrl))
    {
        logger.LogInformation("CliUrl is configured — skipping local copilot CLI check.");
        return;
    }

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
        if (process is null || !process.WaitForExit(3000))
        {
            process?.Kill();
            logger.LogWarning(
                "copilot CLI check timed out. " +
                "Ensure GitHub Copilot CLI is installed: gh extension install github/gh-copilot");
            return;
        }

        if (process.ExitCode != 0)
        {
            logger.LogWarning(
                "copilot CLI returned exit code {Code}. " +
                "Ensure you are authenticated: gh auth login", process.ExitCode);
            return;
        }

        logger.LogInformation("GitHub Copilot CLI found: {Version}",
            process.StandardOutput.ReadToEnd().Trim());
    }
    catch (Exception ex)
    {
        logger.LogWarning(
            "GitHub Copilot CLI not found on PATH. " +
            "Install it with: gh extension install github/gh-copilot. " +
            "Error: {Error}", ex.Message);
    }
}

public partial class Program;

