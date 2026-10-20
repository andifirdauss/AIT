namespace AIThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    private void SetPhase(string phase, string? detail = null)
    {
        _phase = phase;
        _detail = detail;
        Publish("relabel");
        _lastProgressAt = DateTime.UtcNow;
    }
}
