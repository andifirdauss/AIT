using AIThread.Common.Enums;
using AIThread.Service.Workspace;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    private static string KeySourceName(ProjectKeySource source) => source switch
    {
        ProjectKeySource.File => ProjectKeyResolver.OverrideFile,
        ProjectKeySource.Manual => "key manual",
        ProjectKeySource.GitRemote => "git remote",
        _ => "lokal, belum bisa dibagi"
    };
}
