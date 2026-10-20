using System.Net;
using AIThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    private async Task<string> GetDashboardJsonAsync(string baseUrl, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Combine(baseUrl, path));
        var token = CliToken(baseUrl);
        if (token is not null) request.Headers.TryAddWithoutValidation(RouterCliToken.HeaderName, token);

        using var response = await Client().SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode is (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) && !_dashboardLoginLogged)
        {
            _dashboardLoginLogged = true;
            logger.LogInformation(token is null
                ? "Kuota provider tidak bisa dibaca: dashboard 9router mewajibkan login dan 9router tidak berjalan di komputer ini (matikan 'Require login' di Settings 9router agar AIT bisa membacanya)."
                : "Kuota provider tidak bisa dibaca: 9router menolak token CLI lokal (restart 9router, atau matikan 'Require login' di Settings 9router).");
        }
        if (!response.IsSuccessStatusCode)
            throw new NineRouterException($"GET {path} HTTP {(int)response.StatusCode}: {ExtractError(json)}", (int)response.StatusCode);
        return json;
    }
}
