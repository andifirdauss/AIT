namespace AiThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
