using AiThread.Common.Options;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;

namespace AiThread.Service.Planner;

// Where local tools (the planner CLI's MCP client) can reach this AiThread instance. Plain HTTP is preferred:
// Node does not trust the ASP.NET Core development certificate.
public sealed class LocalServerUrl(IServer server, IOptionsMonitor<AiThreadOptions> options)
{
    public string? BaseUrl
    {
        get
        {
            var configured = options.CurrentValue.LocalBaseUrl;
            if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim().TrimEnd('/');

            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
            var address = addresses?.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) ?? addresses?.FirstOrDefault();
            return address is null ? null : Normalize(address);
        }
    }

    // Wildcard bindings are not connectable addresses.
    public static string Normalize(string address) => address
        .Replace("://+", "://localhost", StringComparison.Ordinal)
        .Replace("://*", "://localhost", StringComparison.Ordinal)
        .Replace("://0.0.0.0", "://localhost", StringComparison.Ordinal)
        .Replace("://[::]", "://localhost", StringComparison.Ordinal)
        .TrimEnd('/');
}
