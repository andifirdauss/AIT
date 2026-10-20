using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Service.Interfaces;

namespace AiThread.Service.Implementations;

public sealed partial class ProjectService
{
    private async Task OnKeyChangedAsync(Project project, string previous, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(previous) || previous == project.ProjectKey) return;

        var moved = await memoryKeys.RekeyAsync(previous, project.ProjectKey, project.Name, cancellationToken);
        activityLog.Info("project",
            $"Project key {project.Name} berubah: {previous} → {project.ProjectKey} ({KeySourceName(project.ProjectKeySource)})"
            + (moved > 0 ? $"; {moved} memory ikut dipindah." : "."),
            projectId: project.Id);
        if (moved > 0) eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryChanged, new { id = (Guid?)null, action = "updated" }));
    }
}
