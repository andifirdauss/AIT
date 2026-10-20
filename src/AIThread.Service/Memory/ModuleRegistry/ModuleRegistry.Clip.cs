namespace AIThread.Service.Memory;

public sealed partial class ModuleRegistry
{
    private static string? Clip(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length > max ? value[..max] : value;
}
