using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace MyHelper.Tools.Tools;

public static class FileWriteTool
{
    public static AIFunction Create() => AIFunctionFactory.Create(
        ([Description("Absolute or relative path to write.")] string path,
         [Description("Text content to write to the file. Overwrites if the file exists.")] string content) =>
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(path, content);
                return $"Written {content.Length} characters to {path}";
            }
            catch (Exception ex)
            {
                return $"Error writing file: {ex.Message}";
            }
        },
        "write_file",
        "Write or overwrite a file with text content.");
}
