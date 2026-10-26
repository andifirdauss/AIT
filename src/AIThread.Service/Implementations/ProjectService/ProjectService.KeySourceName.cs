using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Workspace;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    private static string KeySourceName(ProjectKeySource source) => source switch
    {
        ProjectKeySource.File => ProjectKeyResolver.OverrideFile,
        ProjectKeySource.Manual => Lang.T("key manual", "manual key"),
        ProjectKeySource.GitRemote => "git remote",
        _ => Lang.T("lokal, belum bisa dibagi", "local, cannot be shared yet")
    };
}
