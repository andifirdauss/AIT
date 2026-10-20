namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private static string Seconds(long milliseconds) => $"{milliseconds / 1000.0:0.0}s";
}
