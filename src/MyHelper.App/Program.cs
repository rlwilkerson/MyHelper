using MyHelper.App.Endpoints;
using MyHelper.App.Hubs;
using MyHelper.Core.Extensions;

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

public partial class Program;

