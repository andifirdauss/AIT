namespace AIThread.Service.Implementations;

public sealed partial class ThreadService
{
    // The first line of a prompt, cut short: how a chat session is named in the resume list.
    private static string SessionTitle(string prompt)
    {
        var line = prompt.ReplaceLineEndings("\n").Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? string.Empty;
        return line.Length <= ChatSessionTitleChars ? line : line[..(ChatSessionTitleChars - 1)] + "…";
    }
}
