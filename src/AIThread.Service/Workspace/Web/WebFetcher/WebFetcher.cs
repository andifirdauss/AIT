using System.Text.Encodings.Web;
using System.Text.Json;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Workspace.Web;

// HTTP GET only, without cookies or credentials, redirects followed by hand (at most MaxRedirects, each one checked),
// the body read up to MaxBodyBytes, and a cloud metadata address refused even after DNS (CreateHandler).
public sealed partial class WebFetcher(IHttpClientFactory httpClientFactory) : IWebFetcher
{
    public const string HttpClientName = "webfetch";
    public const int MaxRedirects = 5;
    public const int MaxBodyBytes = 2 * 1024 * 1024;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private static readonly int[] RedirectCodes = [301, 302, 303, 307, 308];

    // Media types never shown as text (JSON, XML and JavaScript types are text even under application/x- or vnd.).
    private static readonly string[] BinaryTypes =
    [
        "image/", "audio/", "video/", "font/", "application/octet-stream", "application/pdf", "application/zip", "application/gzip",
        "application/x-", "application/vnd.", "application/msword", "application/wasm"
    ];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}
