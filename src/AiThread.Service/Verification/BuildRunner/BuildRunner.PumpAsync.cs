namespace AiThread.Service.Verification;

public sealed partial class BuildRunner
{
    private static async Task PumpAsync(StreamReader reader, Action<string> onLine)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line) onLine(line);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The process is gone and its pipe with it.
        }
    }
}
