using AIThread.Common.DTOs;
using AIThread.Service.CliBackend;

namespace AIThread.Service.Implementations;

public sealed partial class SetupService
{
    // Every active node runs on an official CLI ("cli:claude:…"): this machine needs no 9router at all.
    private static bool CliOnly(IReadOnlyList<NodeDto> nodes)
    {
        var active = nodes.Where(n => n.IsActive).ToList();
        return active.Count > 0 && active.All(n => CliModelId.IsCli(n.Model));
    }
}
