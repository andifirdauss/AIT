namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    private static string Clip(string text, int max) => text.Length > max ? text[..max] + "…" : text;
}
