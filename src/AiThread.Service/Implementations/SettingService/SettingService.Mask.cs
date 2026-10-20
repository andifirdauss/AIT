namespace AiThread.Service.Implementations;

public sealed partial class SettingService
{
    private static string? Mask(string? key) =>
        key is null ? null : key.Length <= 10 ? new string('•', key.Length) : $"{key[..6]}••••{key[^4..]}";
}
