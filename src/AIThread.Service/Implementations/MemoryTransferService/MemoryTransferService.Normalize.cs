namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    private static string Normalize(string text) => Whitespace().Replace(text.Trim(), " ").ToLowerInvariant();
}
