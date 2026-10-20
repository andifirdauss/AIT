using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;

namespace AiThread.Service.Implementations;

public sealed partial class NineRouterLauncher
{
    private RouterLaunchResultDto Fail(string message)
    {
        activityLog.Warn(Category, message);
        return new RouterLaunchResultDto(false, false, false, message);
    }
}
