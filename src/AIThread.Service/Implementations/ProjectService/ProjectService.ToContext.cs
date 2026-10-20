using AIThread.Common.Entities;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    private ProjectContext ToContext(Project project)
    {
        var profile = ProfileOf(project.RootPath);
        return new ProjectContext(project.Id, project.Name, project.RootPath, profile.Stacks, profile.Modules, project.ProjectKey);
    }
}
