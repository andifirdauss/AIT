using System.Diagnostics;

namespace AIThread.Service.Workspace.Commands;

public sealed partial class BackgroundProcesses
{
    // True when the awaited URL answered or text showed; without either, when the process still runs at the end.
    private static async Task<bool> WaitAsync(Entry entry, BackgroundWait wait, CancellationToken cancellationToken)
    {
        var seconds = Math.Clamp(wait.Seconds, 1, MaxWaitSeconds);
        var clock = Stopwatch.StartNew();
        TimeSpan? lastUrlCheck = null;
        var awaits = wait.Url is not null || !string.IsNullOrEmpty(wait.Text);
        while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            if (!string.IsNullOrEmpty(wait.Text) && entry.Text().Contains(wait.Text, StringComparison.OrdinalIgnoreCase)) return true;
            if (wait.Url is not null && (lastUrlCheck is null || clock.Elapsed - lastUrlCheck >= UrlInterval))
            {
                lastUrlCheck = clock.Elapsed;
                if (await AnswersAsync(wait.Url, cancellationToken)) return true;
            }
            if (!entry.Running)
            {
                // The last lines may still be on their way from the output pipes.
                entry.Process.WaitForExit(500);
                return !string.IsNullOrEmpty(wait.Text) && entry.Text().Contains(wait.Text, StringComparison.OrdinalIgnoreCase);
            }
            await Task.Delay(PollInterval, cancellationToken);
        }
        return !awaits && entry.Running;
    }
}
