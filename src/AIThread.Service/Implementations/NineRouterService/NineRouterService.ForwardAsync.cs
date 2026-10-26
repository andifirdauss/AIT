using System.Net.Http.Headers;
using System.Text;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    public async Task<HttpResponseMessage> ForwardAsync(
        string pathAndQuery,
        string body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var message = new HttpRequestMessage(HttpMethod.Post, Combine(settings.NineRouterBaseUrl, pathAndQuery))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        foreach (var (name, value) in headers) message.Headers.TryAddWithoutValidation(name, value);
        if (!string.IsNullOrEmpty(settings.NineRouterApiKey))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.NineRouterApiKey);

        try
        {
            return await Client().SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new NineRouterException(Lang.T($"9router tidak dapat dihubungi di {settings.NineRouterBaseUrl}: {ex.Message}", $"9router cannot be reached at {settings.NineRouterBaseUrl}: {ex.Message}"), null, ex);
        }
    }
}
