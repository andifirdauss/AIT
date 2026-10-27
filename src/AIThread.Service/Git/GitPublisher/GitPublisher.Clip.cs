namespace AIThread.Service.Git;

public sealed partial class GitPublisher
{
    // git's message on one line, short enough for the answer.
    private static string Clip(string? text)
    {
        var line = (text ?? string.Empty).ReplaceLineEndings(" ").Trim();
        return line.Length > 400 ? line[..400] + "…" : line;
    }
}
