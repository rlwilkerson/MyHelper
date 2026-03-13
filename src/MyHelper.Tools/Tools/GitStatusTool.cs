using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace MyHelper.Tools.Tools;

public static class GitStatusTool
{
    public static AIFunction Create() => AIFunctionFactory.Create(
        async ([Description("Path to the git repository root.")] string repoPath = ".") =>
        {
            if (!Directory.Exists(repoPath))
                return $"Error: directory not found: {repoPath}";

            try
            {
                using var proc = new Process();
                proc.StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "status",
                    WorkingDirectory = repoPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                proc.Start();
                var stdout = await proc.StandardOutput.ReadToEndAsync();
                var stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();

                if (proc.ExitCode != 0 && !string.IsNullOrEmpty(stderr))
                    return $"git error: {stderr.Trim()}";

                return string.IsNullOrWhiteSpace(stdout) ? "(no output)" : stdout.TrimEnd();
            }
            catch (Exception ex)
            {
                return $"Error running git status: {ex.Message}";
            }
        },
        "git_status",
        "Run git status in a repository directory and return the result.");
}
