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
                command = command.Trim();
                if (string.IsNullOrWhiteSpace(command))
                    return "Error: empty command.";

                var parts = SplitCommandLine(command);
                var executable = parts[0];
                var args = parts.Skip(1).ToArray();

                if (allowList.Length > 0)
                {
                    // Compare against allow-list. Use direct execution (no shell) to prevent
                    // metacharacter injection (&&, |, ;, & etc.) that could bypass the guard.
                    var execName = Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
                    if (!allowList.Any(a => a.Equals(execName, StringComparison.OrdinalIgnoreCase)
                                        || a.Equals(executable, StringComparison.OrdinalIgnoreCase)))
                        return $"Error: '{executable}' is not in the shell command allow-list.";

                    try
                    {
                        using var proc = new Process();
                        proc.StartInfo = new ProcessStartInfo
                        {
                            FileName = executable,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            WorkingDirectory = cwd ?? Directory.GetCurrentDirectory(),
                        };
                        foreach (var arg in args)
                            proc.StartInfo.ArgumentList.Add(arg);

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
                }
                else
                {
                    // No allow-list: full shell execution for developer flexibility.
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
                }
            },
            "run_shell",
            "Run a shell command and return its stdout and stderr output.");
    }

    /// <summary>
    /// Splits a command line string into tokens, respecting double-quoted segments.
    /// e.g. `git commit -m "my message"` → ["git", "commit", "-m", "my message"]
    /// </summary>
    private static string[] SplitCommandLine(string command)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (char c in command)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens.Count > 0 ? tokens.ToArray() : [command];
    }
}
