using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AiThread.Common.Enums;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Skills;

// Text and repository links of a web page source, for the skill orchestrator to read.
public sealed record WebPage(string Text, IReadOnlyList<string> Links);

// Downloads a source into an empty folder: ZIP archives from GitHub/GitLab (no git needed), git clone as the fallback
// and for other git hosts, a single Markdown file, or the text of a web page.
public sealed partial class SkillFetcher(IHttpClientFactory httpClientFactory, ILogger<SkillFetcher> logger)
{
    public const string HttpClientName = "skills";

    private const long MaxArchiveBytes = 200L * 1024 * 1024;
    private const int MaxTextBytes = 3 * 1024 * 1024;
    private const int MaxPageChars = 60_000;
    private const int MaxPageLinks = 60;
    private static readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(5);

    // Returns the page for a Web source (nothing is written then); null for the others.
    public async Task<WebPage?> FetchAsync(SkillSourceLocation location, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        switch (location.Kind)
        {
            case SkillSourceKind.GitHub:
            case SkillSourceKind.GitLab:
                try
                {
                    await DownloadArchiveAsync(location.DownloadUrl!, destination, location.SubPath, cancellationToken);
                }
                catch (HttpRequestException ex) when (location.CloneUrl is not null)
                {
                    // e.g. the API is rate limited or blocked by a proxy while git still works.
                    logger.LogInformation(ex, "Archive download of {Url} failed; trying git clone", location.Url);
                    ClearDirectory(destination);
                    await CloneAsync(location.CloneUrl!, location.Ref, location.SubPath, destination, cancellationToken);
                }
                return null;

            case SkillSourceKind.Git:
                await CloneAsync(location.CloneUrl!, location.Ref, location.SubPath, destination, cancellationToken);
                return null;

            case SkillSourceKind.RawFile:
                var text = await GetTextAsync(location.DownloadUrl!, cancellationToken);
                await File.WriteAllTextAsync(Path.Combine(destination, SkillDiscovery.EntryFileName), text, cancellationToken);
                return null;

            default:
                var html = await GetTextAsync(location.Url, cancellationToken);
                return new WebPage(HtmlToText(html), ExtractLinks(html, location.Url));
        }
    }

    private async Task DownloadArchiveAsync(string url, string destination, string? subPath, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSuccess(response, url);
        if (response.Content.Headers.ContentLength > MaxArchiveBytes)
            throw new InvalidDataException("Arsip repository lebih dari 200 MB.");

        // ZipArchive needs a seekable stream; the archive goes to a temp file first.
        var temp = Path.Combine(Path.GetTempPath(), $"aithread-skill-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var file = File.Create(temp))
            await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > MaxArchiveBytes) throw new InvalidDataException("Arsip repository lebih dari 200 MB.");
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            await using var zip = File.OpenRead(temp);
            SkillArchive.Extract(zip, destination, subPath);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private async Task CloneAsync(string cloneUrl, string? reference, string? subPath, string destination, CancellationToken cancellationToken)
    {
        var checkout = destination + "-git";
        SkillPaths.DeleteDirectory(checkout);
        try
        {
            var arguments = new List<string> { "clone", "--depth", "1", "--quiet" };
            if (!string.IsNullOrWhiteSpace(reference)) arguments.AddRange(["--branch", reference]);
            arguments.AddRange([cloneUrl, checkout]);
            await RunGitAsync(arguments, cancellationToken);

            var source = checkout;
            if (!string.IsNullOrWhiteSpace(subPath))
            {
                source = Path.GetFullPath(Path.Combine(checkout, subPath.Replace('/', Path.DirectorySeparatorChar)));
                if (!source.StartsWith(Path.GetFullPath(checkout), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(source))
                    throw new InvalidDataException($"Folder '{subPath}' tidak ditemukan di repository.");
            }

            SkillPaths.DeleteDirectory(Path.Combine(source, ".git"));
            SkillPaths.DeleteDirectory(destination);
            Directory.Move(source, destination);
        }
        finally
        {
            SkillPaths.DeleteDirectory(checkout);
        }
    }

    private static async Task RunGitAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        // Never wait for a credential prompt: private repositories simply fail.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException("git tidak ditemukan di PATH. Pasang Git, atau pakai link GitHub/GitLab yang bisa diunduh sebagai ZIP.");
        }

        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        _ = process.StandardOutput.ReadToEndAsync(cancellationToken);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(GitTimeout);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("git clone melebihi 5 menit.");
        }

        if (process.ExitCode != 0)
        {
            var error = (await stderr).Trim();
            throw new InvalidOperationException($"git clone gagal: {(error.Length > 400 ? error[..400] : error)}");
        }
    }

    private async Task<string> GetTextAsync(string url, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSuccess(response, url);
        if (response.Content.Headers.ContentLength > MaxTextBytes) throw new InvalidDataException("File atau halaman lebih dari 3 MB.");
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return text.Length > MaxTextBytes ? text[..MaxTextBytes] : text;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string url)
    {
        if (response.IsSuccessStatusCode) return;
        var hint = response.StatusCode switch
        {
            HttpStatusCode.NotFound => " — link tidak ditemukan atau repository privat",
            HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests => " — dibatasi oleh server (rate limit), coba lagi nanti",
            _ => string.Empty
        };
        throw new HttpRequestException($"Unduh {url} gagal: HTTP {(int)response.StatusCode}{hint}.", null, response.StatusCode);
    }

    public static string HtmlToText(string html)
    {
        var text = ScriptOrStyle().Replace(html, " ");
        text = BlockTag().Replace(text, "\n");
        text = AnyTag().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        text = Spaces().Replace(text, " ");
        text = BlankLines().Replace(text, "\n\n").Trim();
        return text.Length <= MaxPageChars ? text : text[..MaxPageChars] + "\n…";
    }

    // Absolute links to git repositories and Markdown files: what a page listing skills usually points to.
    public static IReadOnlyList<string> ExtractLinks(string html, string pageUrl)
    {
        var baseUri = new Uri(pageUrl);
        var links = new List<string>();
        foreach (Match match in Href().Matches(html))
        {
            if (!Uri.TryCreate(baseUri, WebUtility.HtmlDecode(match.Groups["url"].Value), out var uri)) continue;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) continue;
            var host = uri.Host.ToLowerInvariant();
            var path = uri.AbsolutePath;
            var interesting = host is "github.com" or "gitlab.com" && path.Count(c => c == '/') >= 2
                              || path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                              || path.EndsWith(".git", StringComparison.OrdinalIgnoreCase);
            if (!interesting) continue;
            var link = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            if (!links.Contains(link, StringComparer.OrdinalIgnoreCase)) links.Add(link);
            if (links.Count >= MaxPageLinks) break;
        }
        return links;
    }

    private static void ClearDirectory(string path)
    {
        SkillPaths.DeleteDirectory(path);
        Directory.CreateDirectory(path);
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [GeneratedRegex(@"<(script|style|noscript|svg)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<\s*(br|/p|/div|/li|/h[1-6]|/tr|/pre|/section|/article)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"[ \t\f\v]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s*\n\s*\n\s*")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"href\s*=\s*[""'](?<url>[^""'#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Href();
}
