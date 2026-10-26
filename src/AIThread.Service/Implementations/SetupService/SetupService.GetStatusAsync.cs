using AIThread.Common.DTOs;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class SetupService
{
    public async Task<SetupStatusDto> GetStatusAsync(string proxyBaseUrl, bool refreshRouter = false, CancellationToken cancellationToken = default)
    {
        var databaseConnected = await DatabaseConnectedAsync(cancellationToken);
        var routerStatus = !refreshRouter && router.LastStatus is { } cached
            ? cached
            : await Safe<RouterStatusDto?>(async () => await router.CheckStatusAsync(cancellationToken), null);

        var noDatabase = new PipelineStatusDto(false, 0, 0, 0, [Lang.T("Database tidak dapat diakses.", "The database cannot be accessed.")]);
        var pipeline = databaseConnected ? await Safe(() => nodeService.GetPipelineStatusAsync(cancellationToken), noDatabase) : noDatabase;
        var projectCount = databaseConnected ? await CountProjectsAsync(cancellationToken) : 0;
        var storage = databaseConnected ? await Safe<MemoryStorageSettingsDto?>(async () => await memoryStorage.GetAsync(cancellationToken), null) : null;
        var clis = await EnabledClisAsync(proxyBaseUrl, cancellationToken);

        IReadOnlyList<SetupStepDto> steps =
        [
            SystemStep(databaseConnected, shortMemoryStore.IsConnected, storage),
            RouterStep(routerStatus),
            NodesStep(pipeline),
            ProjectStep(projectCount),
            CliStep(clis),
            MemoryStep(storage)
        ];
        var required = steps.Count(s => s.Required);
        var done = steps.Count(s => s.Required && s.Done);
        return new SetupStatusDto(steps, required, done, done == required);
    }
}
