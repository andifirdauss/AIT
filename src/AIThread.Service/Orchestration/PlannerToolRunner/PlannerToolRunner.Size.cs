namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    private static string Size(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / 1024.0 / 1024.0:0.#} MB";
}
