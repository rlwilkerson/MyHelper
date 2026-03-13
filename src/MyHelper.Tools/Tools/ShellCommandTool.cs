using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace MyHelper.Tools.Tools;

public static class ShellCommandTool
{
    public static AIFunction Create(string[] allowList)
    {
        return AIFunctionFactory.Create(
            async ([Description("Shell command to run (e.g. \"git log --oneline -10\").")] string command,
                   [Description("Working directory for the command. Defaults to current directory.")] string? cwd = null) =>
            {
                if (allowList.Length > 0)
                {
                    var executable = command.Split(' ', 2)[0].Trim().ToLowerInvariant();
                    if (!allowList.Any(a => a.Equals(executable, StringComparison.OrdinalIgnoreCase)))
                        return $"Error: '{executable}' is not in the shell command allow-list.";
                }

                try
                {
                    using var proc = new Process();
                    proc.StartInfo = new ProcessStartInfo
                    {
                        FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                        Arguments = OperatingSystem.IsWindows()
                            ? $"/c {command}"
                            : $"-c \"{command.Replace("\"", "\\\"")}\"",
                        WorkingDirectory = cwd ?? Directory.GetCurrentDirectory(),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };

                    proc.Start();

                    var stdout = await proc.StandardOutput.ReadToEndAsync();
                    var stderr = await proc.StandardError.ReadToEndAsync();
                    await proc.WaitForExitAsync();

                    var output = stdout;
                    if (!string.IsNullOrEmpty(stderr))
                        output += (output.Length > 0 ? "\n[stderr]\n" : "") + stderr;

                    return string.IsNullOrWhiteSpace(output)
                        ? $"Exit code: {proc.ExitCode}"
                        : $"Exit code: {proc.ExitCode}\n{output.TrimEnd()}";
                }
                catch (Exception ex)
                {
                    return $"Error executing command: {ex.Message}";
                }
            },
            "run_shell",
            "Run a shell command and return its stdout and stderr output.");
    }
}
