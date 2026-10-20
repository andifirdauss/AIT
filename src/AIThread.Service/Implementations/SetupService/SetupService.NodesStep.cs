using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class SetupService
{
    private static SetupStepDto NodesStep(PipelineStatusDto pipeline)
    {
        var notes = pipeline.Issues.Count > 0 ? $" Catatan: {string.Join(" ", pipeline.Issues)}" : string.Empty;
        var detail = pipeline.IsReady
            ? $"{pipeline.PlannerCount} planner · {pipeline.ExecutorCount} executor · {pipeline.WrapperCount} wrapper.{notes}"
            : $"Belum lengkap. {string.Join(" ", pipeline.Issues)}";
        return new SetupStepDto("nodes", "Pilih model untuk tiap peran", true, pipeline.IsReady, detail);
    }
}
