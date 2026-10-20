namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private static PlanTask CopyTask(PlanTask task, List<string>? dependsOn) => new()
    {
        Id = task.Id,
        Title = task.Title,
        File = task.File,
        Action = task.Action,
        To = task.To,
        Members = task.Members,
        Instruction = task.Instruction,
        Focus = task.Focus,
        Context = task.Context,
        DependsOn = dependsOn is { Count: > 0 } ? dependsOn : null
    };
}
