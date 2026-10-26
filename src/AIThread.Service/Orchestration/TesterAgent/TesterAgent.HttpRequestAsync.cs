using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Text;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Testing;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // Headers HttpClient sets itself.
    private static readonly HashSet<string> SkippedHeaders = new(StringComparer.OrdinalIgnoreCase) { "Host", "Content-Length", "Content-Type", "Transfer-Encoding" };

    // The developer sees the response under the progress line and in the final answer; the model gets it in full (clipped).
    private async Task<string> HttpRequestAsync(JsonObject args, TestRun run, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        var method = (PlannerToolRunner.Str(args, "method") ?? "GET").Trim().ToUpperInvariant();
        if (!TesterTools.Methods.Contains(method)) return $"Error: method must be one of {string.Join(", ", TesterTools.Methods)}.";
        var target = PlannerToolRunner.Str(args, "url")?.Trim() ?? "/";
        // A scenario target written like "POST /api/users" copied into url.
        if (target.Split(' ', 2) is [var verb, var rest] && TesterTools.Methods.Contains(verb.ToUpperInvariant())) target = rest.Trim();
        if (TestUrls.Resolve(run.Test.BaseUrl!, target) is not { } url)
            return $"Error: '{target}' is not on this machine. The tester only calls the app at {run.Test.BaseUrl} (localhost).";

        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        string? contentType = null;
        if (args["headers"] is JsonObject headers)
        {
            foreach (var (name, value) in headers)
            {
                if (value is not JsonValue v || !v.TryGetValue<string>(out var text)) continue;
                if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = text;
                else if (!SkippedHeaders.Contains(name)) request.Headers.TryAddWithoutValidation(name, text);
            }
        }

        string? body = null;
        if (args["body"] is { } bodyNode && method is not ("GET" or "HEAD"))
        {
            var isText = bodyNode is JsonValue textValue && textValue.TryGetValue<string>(out body);
            if (!isText) body = bodyNode.ToJsonString();
            contentType ??= isText && !LooksLikeJson(body!) ? "text/plain" : "application/json";
            request.Content = new StringContent(body!, Encoding.UTF8);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var parsed) ? parsed : new MediaTypeHeaderValue("text/plain");
        }

        var exchange = new HttpExchange { Method = method, Url = run.Mask(url.ToString()), RequestBody = Clip(body is null ? null : run.Mask(body), MaxStoredBodyChars) };
        var clock = Stopwatch.StartNew();
        string? responseText = null;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(RequestTimeout);
            try
            {
                using var response = await run.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                exchange.Status = (int)response.StatusCode;
                exchange.Reason = response.ReasonPhrase;
                exchange.ContentType = response.Content.Headers.ContentType?.MediaType;
                exchange.Location = response.Headers.Location?.ToString();
                responseText = run.Mask(Pretty(await ReadBodyAsync(response, timeout.Token)));
                exchange.ResponseBody = Clip(responseText, MaxStoredBodyChars);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                exchange.Error = Lang.T($"tidak ada respons dalam {RequestTimeout.TotalSeconds:0} detik", $"no response within {RequestTimeout.TotalSeconds:0} seconds");
            }
            catch (HttpRequestException ex)
            {
                exchange.Error = run.Mask(ex.Message);
            }
        }
        exchange.DurationMs = clock.ElapsedMilliseconds;
        if (run.Report.Requests.Count < MaxStoredRequests) run.Report.Requests.Add(exchange);

        var headline = exchange.Error is { } error
            ? $"❌ {error}"
            : $"→ {exchange.Status} {exchange.Reason} · {exchange.DurationMs} ms" + (exchange.ContentType is null ? string.Empty : $" · {exchange.ContentType}")
              + (exchange.Location is null ? string.Empty : $" · Location {exchange.Location}");
        recorder.Log(exchange.Error is null ? ActivityLevel.Info : ActivityLevel.Warning, run.Mask($"Tester: {method} {url.PathAndQuery} {headline}"));
        await progress.DetailAsync(headline, cancellationToken);
        foreach (var line in ProgressLines(responseText)) await progress.DetailAsync(line, cancellationToken, indent: 5);

        if (exchange.Error is not null) return $"Error: no response ({exchange.Error}).";
        var answer = new StringBuilder($"HTTP {exchange.Status} {exchange.Reason} ({exchange.DurationMs} ms)\n");
        if (exchange.ContentType is not null) answer.Append($"Content-Type: {exchange.ContentType}\n");
        if (exchange.Location is not null) answer.Append($"Location: {exchange.Location}\n");
        answer.Append('\n').Append(string.IsNullOrEmpty(responseText) ? "(empty body)" : Clip(responseText, MaxResponseChars));
        if (ResponseHint(method, url.AbsolutePath, exchange.Status, responseText, run.Routes) is { } hint) answer.Append("\n\n").Append(hint);
        return answer.ToString();
    }

    // The first lines of a body, each cut to fit one progress line.
    private static IEnumerable<string> ProgressLines(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) yield break;
        var lines = body.ReplaceLineEndings("\n").Split('\n').Where(l => l.Trim().Length > 0).ToList();
        foreach (var line in lines.Take(MaxProgressBodyLines))
            yield return line.Length > MaxProgressLineChars ? line[..MaxProgressLineChars] + "…" : line;
        if (lines.Count > MaxProgressBodyLines) yield return Lang.T($"… {lines.Count - MaxProgressBodyLines} baris lagi", $"… {lines.Count - MaxProgressBodyLines} more lines");
    }

    private static bool LooksLikeJson(string text) => text.TrimStart() is { Length: > 0 } trimmed && (trimmed[0] == '{' || trimmed[0] == '[');
}
