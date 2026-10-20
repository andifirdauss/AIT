using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    private static void EnsureLength(string? value, int max, string name)
    {
        if (value is not null && value.Length > max) throw new AppValidationException($"{name} maksimal {max} karakter.");
    }
}
