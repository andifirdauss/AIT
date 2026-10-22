using AIThread.Data.Context;
using AIThread.Data.Memory;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

// Scenarios next to the test accounts (LongMemoryStore.CreateTestingAsync). Their sheets live in the project under
// .aithread/testing/, so a developer filling one in the editor can run it from the chat; the web UI downloads and
// uploads the same file.
public sealed partial class TestScenarioService(
    LongMemoryStore longStore,
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IProjectService projectService,
    ITestAccountStore accounts,
    ISettingService settingService,
    IActivityLogService activityLog) : ITestScenarioService
{
    public const string SheetFolder = ".aithread/testing";

    private const string LogCategory = "testing";
    private const int MaxSheetBytes = 5 * 1024 * 1024;
    private const int MaxListedErrors = 10;
}
