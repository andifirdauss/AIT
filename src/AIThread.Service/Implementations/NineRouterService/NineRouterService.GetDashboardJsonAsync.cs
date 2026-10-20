using System.Net;
using AIThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    private async Task<string> GetDashboardJsonAsync(string baseUrl, string path, CancellationToken cancellationToken)
    {
        using var response = await Client().GetAsync(Combine(baseUrl, path), cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode is (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) && !_dashboardLoginLogged)
        {
            _dashboardLoginLogged = true;
            logger.LogInformation("Kuota provider tidak bisa dibaca: dashboard 9router mewajibkan login (matikan 'Require login' di Settings 9router agar AIT bisa membacanya).");
        }
        if (!response.IsSuccessStatusCode)
            throw new NineRouterException($"GET {path} HTTP {(int)response.StatusCode}: {ExtractError(json)}", (int)response.StatusCode);
        return json;
    }
}
