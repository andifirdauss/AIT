using AiThread.Common.Enums;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    // Two memories are the same when they say the same thing about the same project, ignoring case and spacing.
    private static string Fingerprint(string? scope, MemoryType type, string label, string description) =>
        $"{scope}\n{type}\n{Normalize(label)}\n{Normalize(description)}";
}
