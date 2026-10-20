namespace AiThread.Service.Orchestration;

public sealed partial class SessionHandoff
{
    // Claude Code keeps appending to the transcript, so it is opened with full sharing.
    private static IEnumerable<string> ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line) yield return line;
    }
}
