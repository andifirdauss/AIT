namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    private static string? Clip(string? text, int max) =>
        text is null || text.Length <= max ? text : text[..max] + $"\n… ({text.Length - max} more characters)";
}
