namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    private static bool Matches(string? search, string label, string description) =>
        string.IsNullOrEmpty(search)
        || label.Contains(search, StringComparison.OrdinalIgnoreCase)
        || description.Contains(search, StringComparison.OrdinalIgnoreCase);
}
