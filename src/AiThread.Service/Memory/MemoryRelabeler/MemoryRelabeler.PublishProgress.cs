namespace AiThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    // Scan progress arrives after every model batch; the page reloads on each event, so at most one per interval.
    private void PublishProgress()
    {
        var now = DateTime.UtcNow;
        if (now - _lastProgressAt < ProgressInterval) return;
        _lastProgressAt = now;
        Publish("relabel");
    }
}
