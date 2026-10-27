using AIThread.Common.Enums;
using AIThread.Service.Approvals;
using AIThread.Service.Testing;

namespace AIThread.Service.Workspace.Commands;

// curl reads when it only fetches: GET or HEAD, nothing uploaded, nothing written to a file. Data options are reads
// only with -G (they become the query string).
public static partial class CommandPolicy
{
    private static readonly HashSet<string> CurlFlags = new(StringComparer.Ordinal)
    {
        "-s", "--silent", "-S", "--show-error", "-i", "--include", "-I", "--head", "-L", "--location", "-v", "--verbose",
        "-k", "--insecure", "-f", "--fail", "--fail-with-body", "-G", "--get", "--compressed", "-g", "--globoff", "--http1.0",
        "--http1.1", "--http2", "-4", "-6", "-N", "--no-buffer", "-#", "--progress-bar", "--path-as-is", "--raw",
        "--no-progress-meter", "--ssl-no-revoke", "--location-trusted", "--no-keepalive", "-q"
    };

    private static readonly HashSet<string> CurlValueOptions = new(StringComparer.Ordinal)
    {
        "-H", "--header", "-m", "--max-time", "--connect-timeout", "-w", "--write-out", "-A", "--user-agent", "-b", "--cookie",
        "-u", "--user", "-x", "--proxy", "-e", "--referer", "--url", "--retry", "--retry-delay", "--max-redirs", "--resolve",
        "--cacert", "-r", "--range", "--oauth2-bearer", "--limit-rate", "--max-filesize", "--expect100-timeout"
    };

    private static readonly HashSet<string> CurlMethodOptions = new(StringComparer.Ordinal) { "-X", "--request" };

    private static readonly HashSet<string> CurlDataOptions = new(StringComparer.Ordinal)
    {
        "-d", "--data", "--data-raw", "--data-binary", "--data-urlencode", "--data-ascii"
    };

    // Uploads, files written, or a config file that could hold any option.
    private static readonly HashSet<string> CurlWriteOptions = new(StringComparer.Ordinal)
    {
        "-F", "--form", "--form-string", "-T", "--upload-file", "-o", "--output", "-O", "--remote-name", "--remote-name-all",
        "-J", "--remote-header-name", "-K", "--config", "-D", "--dump-header", "-c", "--cookie-jar", "--output-dir",
        "--create-dirs", "--trace", "--trace-ascii", "--libcurl", "--stderr", "--etag-save", "--hsts", "--alt-svc", "--json"
    };

    // Short options of curl that take a value, in a cluster such as "-sSH".
    private const string CurlShortWithValue = "HmwAbuxXerdFTKDcoEYyz";

    private static string? CheckCurl(IReadOnlyList<CommandWord> args, List<ApprovalItem> hosts)
    {
        var urls = new List<string>();
        var get = false;
        var data = false;
        string? method = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i].Text;
            if (arg.Length < 2 || arg[0] != '-')
            {
                urls.Add(arg);
                continue;
            }

            // "--opt=value" and short clusters ("-sSL", "-XPOST") are split into option and value.
            string name;
            string? inline = null;
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var equals = arg.IndexOf('=');
                name = equals > 0 ? arg[..equals] : arg;
                if (equals > 0) inline = arg[(equals + 1)..];
            }
            else
            {
                var cluster = arg[1..];
                var valueAt = cluster.IndexOfAny(CurlShortWithValue.ToCharArray());
                foreach (var flag in (valueAt < 0 ? cluster : cluster[..valueAt]).Select(c => "-" + c))
                    if (!CurlFlags.Contains(flag)) return $"uses curl option {flag}, which AIT cannot check";
                if (valueAt < 0)
                {
                    get |= cluster.Contains('G');
                    if (cluster.Contains('I')) method = "HEAD";
                    continue;
                }
                get |= cluster[..valueAt].Contains('G');
                name = "-" + cluster[valueAt];
                if (valueAt + 1 < cluster.Length) inline = cluster[(valueAt + 1)..];
            }

            if (CurlWriteOptions.Contains(name))
            {
                if (name is "-D" or "--dump-header" && (inline ?? (i + 1 < args.Count ? args[i + 1].Text : null)) == "-")
                {
                    if (inline is null) i++;
                    continue;
                }
                return $"curl {name} uploads or writes a file";
            }
            if (CurlFlags.Contains(name))
            {
                get |= name is "-G" or "--get";
                if (name is "-I" or "--head") method = "HEAD";
                continue;
            }
            if (!CurlMethodOptions.Contains(name) && !CurlDataOptions.Contains(name) && !CurlValueOptions.Contains(name))
                return $"uses curl option {name}, which AIT cannot check";

            var value = inline ?? (++i < args.Count ? args[i].Text : null);
            if (value is null) return $"curl {name} has no value";
            if (CurlMethodOptions.Contains(name)) method = value.ToUpperInvariant();
            else if (CurlDataOptions.Contains(name)) data = true;
            else if (name == "--url") urls.Add(value);
        }

        if (method is not null and not ("GET" or "HEAD")) return $"sends an HTTP {method}";
        if (data && !get) return "sends data (a POST); add -G to send it as the query string";
        if (urls.Count == 0) return "curl names no URL";

        foreach (var url in urls)
        {
            var text = url.Contains("://", StringComparison.Ordinal) ? url : "http://" + url;
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                return $"'{url}' is not an http(s) URL";
            if (TestUrls.IsLocal(uri.ToString())) continue;
            hosts.Add(new ApprovalItem(ApprovalKind.Host, $"{uri.IdnHost.ToLowerInvariant()}:{uri.Port}", $"{method ?? "GET"} {uri.GetLeftPart(UriPartial.Path)}"));
        }
        return null;
    }
}
