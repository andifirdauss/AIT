namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
