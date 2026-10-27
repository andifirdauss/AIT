using System.Net;

namespace AIThread.Service.Workspace.Web;

// Which URLs web_fetch may read. Only http(s); a cloud metadata endpoint (credentials of the machine AIT runs on) is
// refused whatever the developer allowed, both by name and, after DNS, by address (WebFetcher.CreateHandler).
public static class WebUrls
{
    private static readonly string[] MetadataHosts = ["metadata.google.internal", "metadata.goog", "metadata"];

    private static readonly IPAddress[] MetadataAddresses =
    [
        IPAddress.Parse("169.254.169.254"),
        IPAddress.Parse("169.254.170.2"),
        IPAddress.Parse("fd00:ec2::254")
    ];

    public static bool TryParse(string? url, out Uri uri, out string error)
    {
        uri = null!;
        error = string.Empty;
        var text = url?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            error = "\"url\" is required.";
            return false;
        }
        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https"))
        {
            error = $"'{text}' is not an absolute http(s) URL.";
            return false;
        }
        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            error = "a URL with a user name or password is not fetched.";
            return false;
        }
        uri = parsed;
        return true;
    }

    // The approval target, the same as a curl to that host (CommandPolicy): "host:port".
    public static string HostTarget(Uri uri) => $"{uri.IdnHost.ToLowerInvariant()}:{uri.Port}";

    public static bool IsRefused(Uri uri)
    {
        var host = uri.IdnHost.Trim('[', ']');
        return MetadataHosts.Any(h => host.Equals(h, StringComparison.OrdinalIgnoreCase))
               || (IPAddress.TryParse(host, out var address) && IsRefused(address));
    }

    public static bool IsRefused(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return MetadataAddresses.Any(address.Equals);
    }
}
