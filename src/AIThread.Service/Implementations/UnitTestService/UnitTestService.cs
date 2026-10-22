using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations;

// Project > Detail → Unit test: the catalog of one project, its settings, scans, manual runs and the confirmation of
// obsolete tests.
public sealed partial class UnitTestService(
    IProjectService projectService,
    IUnitTestCatalog catalog,
    IUnitTestScanner scanner,
    IUnitTestExecutor executor,
    INodeService nodeService,
    ISettingService settingService,
    IThreadOrchestrator orchestrator,
    IActivityLogService activityLog) : IUnitTestService
{
    private const int HistoryRows = 10;
}
