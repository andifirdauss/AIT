using AIThread.Common.DTOs;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class SetupService
{
    private static SetupStepDto NodesStep(PipelineStatusDto pipeline)
    {
        var notes = pipeline.Issues.Count > 0 ? Lang.T($" Catatan: {string.Join(" ", pipeline.Issues)}", $" Notes: {string.Join(" ", pipeline.Issues)}") : string.Empty;
        var detail = pipeline.IsReady
            ? $"{pipeline.PlannerCount} planner · {pipeline.ExecutorCount} executor · {pipeline.WrapperCount} wrapper.{notes}"
            : Lang.T($"Belum lengkap. {string.Join(" ", pipeline.Issues)}", $"Not complete yet. {string.Join(" ", pipeline.Issues)}");
        return new SetupStepDto("nodes", Lang.T("Pilih model untuk tiap peran", "Choose a model for each role"), true, pipeline.IsReady, detail);
    }
}
