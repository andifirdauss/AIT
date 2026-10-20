using AiThread.Common.DTOs;
using AiThread.Common.Entities;

namespace AiThread.Service.Implementations;

public sealed partial class ThreadService
{
    public static ThreadTaskDto ToTaskDto(ThreadTask t) => new(
        t.Id, t.TaskKey, t.Order, t.Title, t.FilePath, t.Action, t.TargetPath, t.Instruction, t.DependsOn, t.Status,
        t.NodeName, t.Model, t.Attempts, t.Summary, t.Diff, t.Error, t.Tokens, t.StartedAt, t.CompletedAt, t.DurationMs);
}
