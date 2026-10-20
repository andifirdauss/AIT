using AiThread.Common.Enums;

namespace AiThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    // Nodes are matched by role and name (case-insensitive), on this machine as in the file.
    private static bool IsNode(NodeRole role, string name, NodeRole otherRole, string? otherName) =>
        role == otherRole && string.Equals(name.Trim(), otherName?.Trim(), StringComparison.OrdinalIgnoreCase);
}
