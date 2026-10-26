using System.Text.Json;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // ThreadChange.Files; empty when missing or unreadable.
    private static IReadOnlyList<SharedFile> SharedFiles(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<SharedFile>>(json, FilesJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
