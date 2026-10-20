using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterLauncher
{
    private RouterLaunchResultDto Fail(string message)
    {
        activityLog.Warn(Category, message);
        return new RouterLaunchResultDto(false, false, false, message);
    }
}
