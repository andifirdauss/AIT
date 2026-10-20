namespace AiThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    private static string Quote(string? text) => text is null ? string.Empty : $"\"{Clip(text, 120)}\"";
}
