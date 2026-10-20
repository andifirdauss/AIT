namespace AIThread.Service.Implementations;

public sealed partial class DocsService
{
    // Development runs from src/AIThread.UI, so the repository docs/ folder is two levels up (live edits);
    // a build or publish copies the files next to the binaries.
    public static string? ResolveDirectory(string contentRootPath, string? configuredPath)
    {
        string?[] candidates =
        [
            configuredPath,
            Path.Combine(contentRootPath, "..", "..", "docs"),
            Path.Combine(AppContext.BaseDirectory, "docs"),
            Path.Combine(contentRootPath, "docs")
        ];

        return candidates
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => Path.GetFullPath(c!))
            .FirstOrDefault(Directory.Exists);
    }
}
