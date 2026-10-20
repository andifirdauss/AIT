namespace AiThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // Wrapped lines of a tool call line up with its arguments.
    public static int Hang(string prefix) => ClientProgress.DisplayWidth(prefix) + PlannerTools.NameColumn;
}
