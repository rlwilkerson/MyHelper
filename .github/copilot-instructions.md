# Copilot instructions for MyHelper

## Build, test, and lint commands

- Use .NET SDK version from `global.json` (`10.0.200`).
- Restore/build solution:
  - `dotnet restore .\MyHelper.slnx`
  - `dotnet build .\MyHelper.slnx -nologo`
- Run the web app directly:
  - `dotnet run --project .\src\MyHelper.App\MyHelper.App.csproj`
- Run the Aspire host (orchestrates app + dashboard resources):
  - `dotnet run --project .\src\MyHelper.AppHost\MyHelper.AppHost.csproj`
- Lint/format check:
  - `dotnet format .\MyHelper.slnx --verify-no-changes --verbosity minimal`

### Tests

- Full suite: `dotnet test .\MyHelper.slnx -nologo`
- Single test (when test projects exist):  
  `dotnet test .\MyHelper.slnx --filter "FullyQualifiedName~Namespace.ClassName.MethodName"`
- Current state: no dedicated test project (`*Test*.csproj` / `*Tests*.csproj`) exists yet.

## High-level architecture

- `src/MyHelper.AppHost` is the Aspire entrypoint. `AppHost.cs` wires `MyHelper.App` into the distributed app.
- `src/MyHelper.App` is the ASP.NET Core web/UI host:
  - Razor Pages UI (`Pages/Index.cshtml`)
  - SignalR hub (`/hubs/chat`) in `Hubs/ChatHub.cs` for streamed assistant responses
  - Minimal API endpoints in `Program.cs` for sessions, models, tools, and health.
- `src/MyHelper.Core` holds Copilot session orchestration:
  - `CopilotClientService` owns `CopilotClient` lifecycle as a hosted singleton.
  - `SessionManager` creates/resumes/deletes sessions, streams events, and merges configured/requested MCP servers.
  - `CoreServiceExtensions` registers core services and tool registry.
- `src/MyHelper.Tools` contains AIFunction tool implementations (`read_file`, `write_file`, `run_shell`, `http_get`, etc.).
- `src/MyHelper.ServiceDefaults` applies shared Aspire defaults (OpenTelemetry, health endpoints, service discovery, resilience).

## Key conventions in this repo

- Configuration is under `MyHelper` in `appsettings*.json`, mapped by `AppOptions` (`AppOptions.Section`).
- Tool registration is centralized in `CoreServiceExtensions.RegisterAllTools()`; add new tools there so they appear in sessions and `/api/tools`.
- Session creation defaults:
  - Streaming is enabled (`Streaming = true`).
  - Permissions auto-approve (`OnPermissionRequest = PermissionHandler.ApproveAll`).
  - Default model/system prompt come from `MyHelper:DefaultModel` and `MyHelper:SystemMessage`.
- MCP server wiring pattern:
  - Static servers from config (`MyHelper:McpServers`)
  - Per-request servers merged in `SessionManager.BuildMcpServers(...)`, request values overriding same-name entries.
- `ShellCommandTool` behavior depends on `ShellCommandAllowList`:
  - Non-empty allow-list: direct executable invocation (no shell metacharacters).
  - Empty allow-list: full shell execution (`cmd.exe /c` on Windows).
- Event names between backend and frontend are contract-sensitive (`MessageDelta`, `MessageComplete`, `ToolStarted`, `ToolCompleted`, `SessionError`) and must stay aligned with `wwwroot/js/chat.js`.
