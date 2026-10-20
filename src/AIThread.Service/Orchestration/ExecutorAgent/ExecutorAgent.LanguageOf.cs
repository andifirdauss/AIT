namespace AIThread.Service.Orchestration;

public sealed partial class ExecutorAgent
{
    private static string LanguageOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" => "csharp",
        ".cshtml" or ".razor" => "cshtml",
        ".js" or ".mjs" or ".cjs" => "javascript",
        ".ts" or ".tsx" => "typescript",
        ".jsx" => "jsx",
        ".sql" => "sql",
        ".json" => "json",
        ".html" or ".htm" => "html",
        ".css" => "css",
        ".scss" => "scss",
        ".xml" or ".csproj" or ".config" or ".props" or ".targets" or ".slnx" => "xml",
        ".md" => "markdown",
        ".py" => "python",
        ".yml" or ".yaml" => "yaml",
        _ => "text"
    };
}
