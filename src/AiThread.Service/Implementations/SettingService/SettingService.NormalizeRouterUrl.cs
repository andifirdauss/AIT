namespace AiThread.Service.Implementations;

public sealed partial class SettingService
{
    // Accepts what users typically paste from the 9router dashboard ("http://localhost:20128/v1").
    public static string NormalizeRouterUrl(string? url)
    {
        var value = (url ?? string.Empty).Trim().TrimEnd('/');
        if (value.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) value = value[..^3];
        return value.TrimEnd('/');
    }
}
