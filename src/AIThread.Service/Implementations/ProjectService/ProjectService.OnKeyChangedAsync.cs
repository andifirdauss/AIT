using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    private async Task OnKeyChangedAsync(Project project, string previous, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(previous) || previous == project.ProjectKey) return;

        var moved = await memoryKeys.RekeyAsync(previous, project.ProjectKey, project.Name, cancellationToken);
        activityLog.Info("project",
            Lang.T($"Project key {project.Name} berubah: {previous} → {project.ProjectKey} ({KeySourceName(project.ProjectKeySource)})", $"Project key {project.Name} changed: {previous} → {project.ProjectKey} ({KeySourceName(project.ProjectKeySource)})")
            + (moved > 0 ? Lang.T($"; {moved} memory ikut dipindah.", $"; {moved} memories moved along.") : "."),
            projectId: project.Id);
        if (moved > 0) eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryChanged, new { id = (Guid?)null, action = "updated" }));
    }
}
