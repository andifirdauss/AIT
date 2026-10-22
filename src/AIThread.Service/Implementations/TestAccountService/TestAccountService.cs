using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations;

// Project > Detail → Akun uji over ITestAccountStore: the project id becomes its key, the developer name is recorded on
// every change, and changes are written to the activity log (the development password never is).
public sealed partial class TestAccountService(
    IProjectService projectService,
    ITestAccountStore store,
    IImpersonationDetector impersonation,
    ISettingService settingService,
    IActivityLogService activityLog) : ITestAccountService
{
    private const string LogCategory = "testing";
}
