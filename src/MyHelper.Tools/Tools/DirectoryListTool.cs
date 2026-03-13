using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;

namespace MyHelper.Tools.Tools;

public static class DirectoryListTool
{
    public static AIFunction Create() => AIFunctionFactory.Create(
        ([Description("Directory path to list.")] string path,
         [Description("Whether to recurse into subdirectories.")] bool recursive = false) =>
        {
            if (!Directory.Exists(path))
                return $"Error: directory not found: {path}";

            try
            {
                const int MaxEntries = 500;
                var option = recursive
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                var allEntries = Directory.GetFileSystemEntries(path, "*", option);
                var truncated = allEntries.Length > MaxEntries;
                var entries = truncated ? allEntries.Take(MaxEntries).ToArray() : allEntries;

                var sb = new StringBuilder();

                foreach (var entry in entries.OrderBy(e => e))
                {
                    var relative = Path.GetRelativePath(path, entry);
                    var isDir = Directory.Exists(entry);
                    sb.AppendLine(isDir ? $"[DIR]  {relative}" : $"[FILE] {relative}");
                }

                if (truncated)
                    sb.AppendLine($"\n(truncated — showing {MaxEntries} of {allEntries.Length} entries)");

                return sb.Length > 0 ? sb.ToString() : "(empty directory)";
            }
            catch (Exception ex)
            {
                return $"Error listing directory: {ex.Message}";
            }
        },
        "list_directory",
        "List files and subdirectories in a directory.");
}
