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
                var option = recursive
                    ? SearchOption.AllDirectories
                    : SearchOption.TopDirectoryOnly;

                var entries = Directory.GetFileSystemEntries(path, "*", option);
                var sb = new StringBuilder();

                foreach (var entry in entries.OrderBy(e => e))
                {
                    var relative = Path.GetRelativePath(path, entry);
                    var isDir = Directory.Exists(entry);
                    sb.AppendLine(isDir ? $"[DIR]  {relative}" : $"[FILE] {relative}");
                }

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
