using System.Diagnostics;

namespace AIThread.Service.Workspace.Web;

public sealed partial class WebFetcher
{
    public async Task<WebFetchResult> FetchAsync(Uri url, Func<Uri, CancellationToken, Task<bool>> mayVisit, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var client = httpClientFactory.CreateClient(HttpClientName);
        var current = url;
        try
        {
            for (var hop = 0; ; hop++)
            {
                if (WebUrls.IsRefused(current))
                    return new WebFetchResult(url, current, DurationMs: watch.ElapsedMilliseconds, Error: $"{current.IdnHost} is a cloud metadata address, which is never fetched.");
                if (hop > 0 && !await mayVisit(current, timeout.Token))
                    return new WebFetchResult(url, current, DurationMs: watch.ElapsedMilliseconds, BlockedRedirect: current);

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/json;q=0.9,text/plain;q=0.8,*/*;q=0.5");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                var status = (int)response.StatusCode;

                if (RedirectCodes.Contains(status) && response.Headers.Location is { } location)
                {
                    if (hop >= MaxRedirects)
                        return new WebFetchResult(url, current, status, DurationMs: watch.ElapsedMilliseconds, Error: $"more than {MaxRedirects} redirects.");
                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    if (next.Scheme is not ("http" or "https"))
                        return new WebFetchResult(url, current, status, DurationMs: watch.ElapsedMilliseconds, Error: $"redirected to '{next}', which is not an http(s) URL.");
                    current = next;
                    continue;
                }

                var (body, cut) = await ReadBodyAsync(response.Content, timeout.Token);
                var contentType = response.Content.Headers.ContentType;
                var (text, title, error) = Convert(body, contentType, current);
                return new WebFetchResult(url, current, status, response.ReasonPhrase, contentType?.MediaType, body.Length, cut, title, text,
                    watch.ElapsedMilliseconds, error);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebFetchResult(url, current, DurationMs: watch.ElapsedMilliseconds, Error: $"no answer within {Timeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex)
        {
            return new WebFetchResult(url, current, DurationMs: watch.ElapsedMilliseconds,
                Error: ex.InnerException is { } inner && !ex.Message.Contains(inner.Message, StringComparison.Ordinal) ? $"{ex.Message} {inner.Message}" : ex.Message);
        }
    }
}
