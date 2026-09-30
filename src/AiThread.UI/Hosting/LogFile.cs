using System.Text;

namespace AiThread.UI.Hosting;

// Menu and tray modes cannot print logs to the console (they would garble the menu, or there is no console at all),
// so the host's console logger is pointed at a file in %LOCALAPPDATA%\AiThread\logs, the folder the script also uses.
public static class LogFile
{
    private static TextWriter? _original;

    // The real console writer, for the menu; Console.Out is the log file once Redirect has run.
    public static TextWriter ConsoleOut => _original ?? Console.Out;

    public static string? CurrentPath { get; private set; }

    // Once per process: a server restart from the menu keeps appending to the same log.
    public static void Redirect()
    {
        if (CurrentPath is not null) return;

        Directory.CreateDirectory(AppPaths.LogDir);
        AppPaths.PruneLogs();

        CurrentPath = AppPaths.NewLogPath();
        // FileShare.ReadWrite lets the menu's live-log view and other tools read while we write.
        var stream = new FileStream(CurrentPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        var writer = TextWriter.Synchronized(new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true });

        _original ??= Console.Out;
        Console.SetOut(writer);
        Console.SetError(writer);
    }

    // Prints the last lines of a log, then follows it until stop is signalled.
    public static async Task TailAsync(string path, int lastLines, TextWriter output, CancellationToken stop)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var recent = new Queue<string>();
        while (await reader.ReadLineAsync(stop) is { } line)
        {
            recent.Enqueue(line);
            if (recent.Count > lastLines) recent.Dequeue();
        }
        foreach (var line in recent) output.WriteLine(line);

        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (await reader.ReadLineAsync(stop) is { } line) output.WriteLine(line);
                else await Task.Delay(250, stop);
            }
        }
        catch (OperationCanceledException) { }
    }
}
