using AiThread.Service.Models;

namespace AiThread.Service.Implementations;

public sealed partial class SettingService
{
    // The normalized 9router URL, or a validation error with an example.
    private static string ValidateRouterUrl(string? url)
    {
        var baseUrl = NormalizeRouterUrl(url);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new AppValidationException("URL 9router tidak valid. Contoh: http://localhost:20128");
        return baseUrl;
    }
}
