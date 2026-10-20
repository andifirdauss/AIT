using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Implementations;

public sealed partial class SetupService
{
    private static NodeDto? FindPreset(IReadOnlyList<NodeDto> nodes, NodeRole role) =>
        nodes.FirstOrDefault(n => n.Role == role && string.Equals(n.Name, PresetNames[role], StringComparison.OrdinalIgnoreCase));
}
