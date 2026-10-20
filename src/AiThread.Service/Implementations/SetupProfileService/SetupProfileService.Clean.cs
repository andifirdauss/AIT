namespace AiThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
