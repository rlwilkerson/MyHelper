using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace MyHelper.Tools.Tools;

public static class FileReadTool
{
    public static AIFunction Create() => AIFunctionFactory.Create(
        ([Description("Absolute or relative path to the file to read.")] string path) =>
        {
            if (!File.Exists(path))
                return $"Error: file not found: {path}";

            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                return $"Error reading file: {ex.Message}";
            }
        },
        "read_file",
        "Read the full text content of a file at a given path.");
}
