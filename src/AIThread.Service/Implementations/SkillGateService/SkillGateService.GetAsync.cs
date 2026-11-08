using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Projects;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class SkillGateService
{
    public async Task<SkillGateDto> GetAsync(string folder, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var projects = await db.Projects.AsNoTracking().Select(p => new { p.Name, p.RootPath }).ToListAsync(cancellationToken);
        var registered = ProjectFolders.Holding(projects, p => p.RootPath, folder).FirstOrDefault();

        var found = registered is null ? ProjectLocator.Resolve(folder).Root : null;
        if (registered is null && found is null) return new SkillGateDto(SkillGateState.NotAProject, null, null);

        var name = registered?.Name ?? found!.Name;
        var root = registered?.RootPath ?? found!.Path;
        if (!(await nodeService.GetPipelineStatusAsync(cancellationToken)).IsReady) return new SkillGateDto(SkillGateState.SetupIncomplete, name, root);
        return new SkillGateDto(registered is null ? SkillGateState.ProjectNotRegistered : SkillGateState.Ready, name, root);
    }
}
