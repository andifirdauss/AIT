using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class TestScenarioService
{
    public async Task<ScenarioImportResultDto> ImportAsync(Guid projectId, Stream content, CancellationToken cancellationToken = default)
    {
        var (project, by) = await ProjectAsync(projectId, cancellationToken);
        return await ImportSheetAsync(project, content, null, by, cancellationToken);
    }
}
