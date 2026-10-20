using AiThread.Common.Enums;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class SetupService
{
    // "Claude Code (global)", "Codex (2 project)": every CLI pointed at this proxy, globally or in some project.
    private async Task<IReadOnlyList<string>> EnabledClisAsync(string proxyBaseUrl, CancellationToken cancellationToken)
    {
        var enabled = new List<string>();
        foreach (var cli in integrations)
        {
            try
            {
                var global = await cli.GetStatusAsync(CliScope.Global, null, proxyBaseUrl, cancellationToken);
                if (global.Enabled)
                {
                    enabled.Add($"{cli.Info.Name} (global)");
                    continue;
                }
                if (cli.Info.ProjectFile is null) continue;

                var projects = (await cli.GetProjectStatusesAsync(proxyBaseUrl, cancellationToken)).Count(p => p.Enabled);
                if (projects > 0) enabled.Add($"{cli.Info.Name} ({projects} project)");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Setup: status of CLI {Cli} unavailable.", cli.Info.Id);
            }
        }
        return enabled;
    }
}
