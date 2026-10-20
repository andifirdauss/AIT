using AiThread.Common.Entities;

namespace AiThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private static PlanTask ToPlanTask(ThreadTask row) => new()
    {
        Id = row.TaskKey,
        Title = row.Title,
        File = row.FilePath,
        Action = row.Action,
        To = row.TargetPath,
        Instruction = row.Instruction
    };
}
