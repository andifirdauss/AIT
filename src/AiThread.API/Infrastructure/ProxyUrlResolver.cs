using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AiThread.API.Infrastructure;

// The URL Claude Code should use as ANTHROPIC_BASE_URL. Plain HTTP is preferred: Node does not trust
// the ASP.NET Core development certificate, so an https base URL would fail inside Claude Code.
public static class ProxyUrlResolver
{
    public static string Resolve(HttpContext context)
    {
        var addresses = context.RequestServices.GetService<IServer>()?.Features.Get<IServerAddressesFeature>()?.Addresses;
        var http = addresses?.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
        return http is not null ? Normalize(http) : $"{context.Request.Scheme}://{context.Request.Host}";
    }

    private static string Normalize(string address) => address
        .Replace("://+", "://localhost", StringComparison.Ordinal)
        .Replace("://*", "://localhost", StringComparison.Ordinal)
        .Replace("://0.0.0.0", "://localhost", StringComparison.Ordinal)
        .Replace("://[::]", "://localhost", StringComparison.Ordinal)
        .TrimEnd('/');
}
