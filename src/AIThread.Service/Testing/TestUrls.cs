using System.Net;

namespace AIThread.Service.Testing;

// The tester only reaches the app on this machine: whatever the plan or the model writes, a request or a navigation to
// another host is refused, so a test can never hit a shared or production system.
public static class TestUrls
{
    public static bool IsLocal(string? url) => TryParseLocal(url, out _);

    // An absolute http(s) URL whose host is localhost (or *.localhost) or a loopback address.
    public static bool TryParseLocal(string? url, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;

        var host = parsed.IdnHost.Trim('[', ']');
        var local = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
                    || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
        if (!local) return false;
        uri = parsed;
        return true;
    }

    // A path such as "/api/users" or "api/users?x=1" against the base URL; an absolute URL stays as it is. Null when the
    // result is not local.
    public static Uri? Resolve(string baseUrl, string target)
    {
        var trimmed = target.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
            return TryParseLocal(trimmed, out var local) ? local : null;
        if (!TryParseLocal(baseUrl, out var root)) return null;

        // The base URL may carry a virtual directory ("http://localhost/app"): relative paths stay under it.
        var rootText = root.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/";
        return Uri.TryCreate(new Uri(rootText), trimmed.TrimStart('/'), out var combined) && TryParseLocal(combined.ToString(), out var result)
            ? result
            : null;
    }
}
