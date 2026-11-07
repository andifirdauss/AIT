using System.Text.Json;

namespace AIThread.Service.Git;

// One file of ThreadChange.Files: what a thread (or an ingested commit) left in it.
public sealed record SharedFile(string Path, string? Blob, bool Removed);

// ThreadChange.Files (JSON) and ThreadChange.Commits (CSV), read and written the same way everywhere.
public static class ThreadChangeFiles
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Write(IEnumerable<SharedFile> files) => JsonSerializer.Serialize(files, Json);

    // Empty when missing or unreadable.
    public static IReadOnlyList<SharedFile> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<SharedFile>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static IReadOnlyList<string> Commits(string? csv) =>
        (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ThreadChange.Commits with one more commit (unchanged when it is there). The oldest go first when the column is full:
    // the newest commits are where the change is now.
    public static string AddCommit(string? csv, string commit, int maxLength = MaxCommitsLength)
    {
        var commits = Commits(csv).ToList();
        if (commits.Contains(commit, StringComparer.OrdinalIgnoreCase)) return csv ?? commit;
        commits.Add(commit);
        while (commits.Count > 1 && string.Join(',', commits).Length > maxLength) commits.RemoveAt(0);
        return string.Join(',', commits);
    }

    // The length of ThreadChange.Commits in the store.
    public const int MaxCommitsLength = 1000;
}
