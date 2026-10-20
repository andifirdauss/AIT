namespace AiThread.Service.Memory;

public sealed partial class ModuleMerger
{
    private static string? Clip(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length > max ? value[..max] : value;
}
