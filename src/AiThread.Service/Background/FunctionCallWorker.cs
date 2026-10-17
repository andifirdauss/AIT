using AiThread.Service.CodeCalls;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Background;

// Reads the calls of functions in the background (FunctionCallQueue → FunctionCallScanner), one file at a time. The
// reads of one file waiting together are merged into one pass (each function is read once); a failure only costs
// that file.
public sealed class FunctionCallWorker(FunctionCallQueue queue, FunctionCallScanner scanner, ILogger<FunctionCallWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                var waiting = new List<FunctionScanRequest>();
                while (queue.Reader.TryRead(out var request)) waiting.Add(request);

                foreach (var file in waiting.Where(r => r.Path is not null).GroupBy(r => (r.ProjectId, r.Root, Path: r.Path!.ToLowerInvariant())))
                {
                    var first = file.First();
                    await RunAsync(() => scanner.ScanFileAsync(first.ProjectId, first.Root, first.Path!, file.Select(r => (r.From, r.To)).ToList(), stoppingToken),
                        first.Path!, stoppingToken);
                }
                foreach (var thread in waiting.Where(r => r.ThreadId is not null).DistinctBy(r => r.ThreadId))
                    await RunAsync(() => scanner.ScanAsync(thread, stoppingToken), $"thread {thread.ThreadId}", stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; what is still waiting is read again the next time the code is read or changed.
        }
    }

    private async Task RunAsync(Func<Task> scan, string subject, CancellationToken stoppingToken)
    {
        try
        {
            await scan();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Reading the function calls of {Subject} failed", subject);
        }
    }
}
