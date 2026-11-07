using AIThread.Common.DTOs;

namespace AIThread.Service.CliBackend;

public sealed partial class CliModelService
{
    public async Task<IReadOnlyList<RouterModelDto>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var installed = (await DetectAsync(false, cancellationToken)).Where(s => s.Installed).Select(s => s.Tool).ToHashSet();
        var engines = _engines.Where(e => installed.Contains(e.Tool)).ToList();
        var models = await Task.WhenAll(engines.Select(e => e.ListModelsAsync(cancellationToken)));
        return engines
            .SelectMany((e, i) => models[i].Select(model => new RouterModelDto(CliModelId.Format(e.Tool, model), Owner(e), false, null, null)))
            .ToList();
    }
}
