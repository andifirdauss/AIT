namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
