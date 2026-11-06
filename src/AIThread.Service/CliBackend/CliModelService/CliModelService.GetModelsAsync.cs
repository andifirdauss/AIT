using AIThread.Common.DTOs;

namespace AIThread.Service.CliBackend;

public sealed partial class CliModelService
{
    public async Task<IReadOnlyList<RouterModelDto>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var installed = await DetectAsync(false, cancellationToken);
        if (!installed.Any(c => c.Installed && c.Tool == CliModelId.Claude)) return [];
        return ClaudeAliases.Select(alias => new RouterModelDto(CliModelId.Format(CliModelId.Claude, alias), ClaudeOwner, false, null, null)).ToList();
    }
}
