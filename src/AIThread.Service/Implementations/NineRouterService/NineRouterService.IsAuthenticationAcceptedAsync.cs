using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    // 9router authenticates before validating the body, so an empty request tells a rejected or missing key (401)
    // apart from an accepted one (400 "Missing model") without spending any tokens. A null key checks whether
    // 9router requires one at all (its "require API key" option).
    private async Task<bool> IsAuthenticationAcceptedAsync(string baseUrl, string? apiKey, bool useCache, CancellationToken cancellationToken)
    {
        var cached = _keyCheck;
        if (useCache && cached is not null && cached.BaseUrl == baseUrl && cached.Key == apiKey && DateTime.UtcNow - cached.CheckedAt < KeyCheckCacheDuration)
            return cached.Valid;

        using var request = new HttpRequestMessage(HttpMethod.Post, Combine(baseUrl, "/v1/chat/completions"))
        {
            Content = new StringContent("{\"messages\":[]}", Encoding.UTF8, "application/json")
        };
        if (apiKey is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var response = await Client().SendAsync(request, timeout.Token);
        var valid = response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);

        _keyCheck = new KeyCheck(baseUrl, apiKey, valid, DateTime.UtcNow);
        return valid;
    }
}
