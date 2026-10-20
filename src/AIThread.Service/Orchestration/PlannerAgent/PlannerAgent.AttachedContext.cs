namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    private static string? AttachedContext(string? promptContext, string prompt)
    {
        if (string.IsNullOrWhiteSpace(promptContext)) return null;
        var context = prompt.Length > 0 ? promptContext.Replace(prompt, string.Empty) : promptContext;
        return string.IsNullOrWhiteSpace(context) ? null : context.Trim();
    }
}
