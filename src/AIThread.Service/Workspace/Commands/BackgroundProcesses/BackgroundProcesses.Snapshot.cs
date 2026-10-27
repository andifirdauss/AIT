namespace AIThread.Service.Workspace.Commands;

public sealed partial class BackgroundProcesses
{
    // The last tailLines lines, and no more than a tool result holds (CommandRunner.MaxOutputChars, newest kept).
    private static BackgroundOutput Snapshot(Entry entry, int tailLines)
    {
        var lines = TextFileIo.NormalizeNewLines(entry.Text()).TrimEnd('\n').Split('\n');
        if (lines is [""]) lines = [];
        var take = Math.Max(1, tailLines);
        var truncated = entry.Dropped || lines.Length > take;
        var text = string.Join('\n', lines.Skip(Math.Max(0, lines.Length - take)));
        if (text.Length > CommandRunner.MaxOutputChars)
        {
            text = text[^CommandRunner.MaxOutputChars..];
            truncated = true;
        }
        return new BackgroundOutput(entry.Id, entry.Command, entry.WorkingDirectory, entry.Running, entry.ExitCode, entry.Expired, text, truncated,
            DateTime.UtcNow - entry.StartedAt);
    }
}
