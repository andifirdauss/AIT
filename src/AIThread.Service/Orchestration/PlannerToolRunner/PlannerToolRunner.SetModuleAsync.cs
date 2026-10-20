using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // A class or file name is resolved to the project's file first.
    private async Task<PlannerToolOutcome> SetModuleAsync(PlannerInput input, string? path, string? module, string? layerText, ThreadRecorder recorder,
        CancellationToken cancellationToken)
    {
        if (path is null || module is null) return new PlannerToolOutcome("Error: path and module are required.", null, true);
        var target = path;
        if (!path.Contains('/') && !path.Contains('\\') && !Path.HasExtension(path))
        {
            var found = await codeMapper.ResolveReferencesAsync(input.Project, path, cancellationToken);
            if (found.Count == 0) return new PlannerToolOutcome($"Error: no file named '{path}' in the project. Give its path.", null, true);
            if (found.Count > 1) return new PlannerToolOutcome($"Error: '{path}' matches several files ({string.Join(", ", found.Take(5))}). Give the path.", null, true);
            target = found[0];
        }

        try
        {
            var hit = await codeMapper.SetManualAsync(input.Project, target, module,
                MemoryLabels.TryParseLayer(layerText, out var layer) ? layer : null, cancellationToken);
            recorder.Log(ActivityLevel.Info, $"Planner → set_module `{hit.Path}` → {hit.Module} · {hit.Layer}");
            return new PlannerToolOutcome($"Code map updated: {hit.Path} → {hit.Module} · {hit.Layer}.", null);
        }
        catch (AppValidationException ex)
        {
            return new PlannerToolOutcome($"Error: {ex.Message}", null, true);
        }
    }
}
