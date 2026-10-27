using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Orchestration;
using AIThread.Service.Workspace.Web;
using Microsoft.Extensions.DependencyInjection;
using static AIThread.Tests.ThreadOrchestratorTests;

namespace AIThread.Tests;

// The planner's web_fetch: pages as readable text, redirects checked per host, cloud metadata refused, and an outside
// host waiting for the developer's permission. No test reaches the internet: a loopback server or a fake fetcher.
public class WebFetchTests
{
    [Fact]
    public void A_page_becomes_markdown_without_scripts_or_navigation()
    {
        const string html = """
            <html><head><title>Users &amp; roles</title><style>body{color:red}</style></head>
            <body>
              <nav><a href="/home">Home</a> <a href="/about">About</a></nav>
              <script>alert('x')</script>
              <h1>Users API</h1>
              <p>Call <code>GET /users</code> to list&nbsp;users. See <a href="/docs/paging">paging</a>.</p>
              <ul><li>id</li><li>name</li></ul>
              <pre><code>{
              "id": 1
            }</code></pre>
              <footer>© 2026</footer>
            </body></html>
            """;

        var text = HtmlText.ToMarkdown(html, new Uri("https://docs.example.com/api/users"), out var title);

        Assert.Equal("Users & roles", title);
        Assert.Contains("# Users API", text);
        Assert.Contains("Call `GET /users` to list users.", text);
        Assert.Contains("[paging](https://docs.example.com/docs/paging)", text);
        Assert.Contains("- id", text);
        Assert.Contains("- name", text);
        Assert.Contains("```\n{\n  \"id\": 1\n}\n```", text);
        Assert.DoesNotContain("alert", text);
        Assert.DoesNotContain("Home", text);
        Assert.DoesNotContain("color:red", text);
        Assert.DoesNotContain("2026", text);
    }

    [Fact]
    public async Task A_local_page_and_json_are_read()
    {
        await using var server = new LoopbackServer(new()
        {
            ["/page"] = Respond(200, "text/html; charset=utf-8", "<html><body><h2>Hello</h2><p>World</p></body></html>"),
            ["/api/user"] = Respond(200, "application/json", """{"id":1,"name":"Ana"}""")
        });
        var fetcher = NewFetcher();

        var page = await fetcher.FetchAsync(server.Url("/page"), NoOtherHost);
        var json = await fetcher.FetchAsync(server.Url("/api/user"), NoOtherHost);

        Assert.Null(page.Error);
        Assert.Equal(200, page.StatusCode);
        Assert.Contains("## Hello", page.Text);
        Assert.Contains("World", page.Text);
        Assert.Equal("application/json", json.ContentType);
        Assert.Equal("{\n  \"id\": 1,\n  \"name\": \"Ana\"\n}", json.Text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task A_redirect_is_followed_on_the_same_host_and_stopped_before_another_one()
    {
        await using var server = new LoopbackServer(new()
        {
            ["/old"] = Respond(301, "text/plain", string.Empty, "/new"),
            ["/new"] = Respond(200, "text/plain", "moved here"),
            ["/away"] = Respond(302, "text/plain", string.Empty, "http://docs.example.invalid/guide")
        });
        var fetcher = NewFetcher();
        var asked = new List<Uri>();

        var moved = await fetcher.FetchAsync(server.Url("/old"), (uri, _) =>
        {
            asked.Add(uri);
            return Task.FromResult(uri.IsLoopback);
        });
        var away = await fetcher.FetchAsync(server.Url("/away"), (uri, _) =>
        {
            asked.Add(uri);
            return Task.FromResult(uri.IsLoopback);
        });

        Assert.Equal("moved here", moved.Text);
        Assert.Equal(server.Url("/new"), moved.FinalUrl);
        Assert.Equal(new Uri("http://docs.example.invalid/guide"), away.BlockedRedirect);
        Assert.Equal(string.Empty, away.Text);
        Assert.Contains(new Uri("http://docs.example.invalid/guide"), asked);
    }

    [Fact]
    public async Task Cloud_metadata_and_binary_bodies_are_refused()
    {
        await using var server = new LoopbackServer(new()
        {
            ["/logo.png"] = Respond(200, "image/png", "\u0089PNG"),
            ["/loop"] = Respond(302, "text/plain", string.Empty, "/loop")
        });
        var fetcher = NewFetcher();

        var metadata = await fetcher.FetchAsync(new Uri("http://169.254.169.254/latest/meta-data/"), NoOtherHost);
        var image = await fetcher.FetchAsync(server.Url("/logo.png"), NoOtherHost);
        var loop = await fetcher.FetchAsync(server.Url("/loop"), (_, _) => Task.FromResult(true));

        Assert.Contains("cloud metadata", metadata.Error);
        Assert.Null(metadata.StatusCode);
        Assert.True(WebUrls.IsRefused(new Uri("http://metadata.google.internal/computeMetadata/v1/")));
        Assert.True(WebUrls.IsRefused(IPAddress.Parse("::ffff:169.254.169.254")));
        Assert.False(WebUrls.IsRefused(new Uri("http://10.0.0.5/")));
        Assert.Contains("binary", image.Error);
        Assert.Contains("redirects", loop.Error);
        Assert.False(WebUrls.TryParse("ftp://example.com/file", out _, out _));
        Assert.False(WebUrls.TryParse("https://user:secret@example.com/", out _, out _));
        Assert.Equal("example.com:443", WebUrls.HostTarget(new Uri("https://Example.com/docs")));
    }

    [Fact]
    public async Task A_local_url_is_read_without_asking()
    {
        var fetcher = new FakeFetcher("Users: Ana, Budi");
        await using var harness = await CreateWithFetcherAsync(fetcher);
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.WebFetch, new { url = "http://localhost:5000/api/users", reason = "lihat response" }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Dijawab.", Answer = "Ada dua user." }));

        var (result, observer) = await harness.RunAsync("Lihat response /api/users", ThreadSource.ClaudeCode, "session-local");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Contains("Ada dua user.", observer.Final);
        Assert.Equal(new Uri("http://localhost:5000/api/users"), Assert.Single(fetcher.Calls));
        Assert.Contains("Users: Ana, Budi", LastToolMessage(router, 1));
    }

    [Fact]
    public async Task An_outside_host_waits_for_permission_and_always_is_remembered()
    {
        using var _ = Lang.Use("en");
        var fetcher = new FakeFetcher("EF Core querying guide");
        await using var harness = await CreateWithFetcherAsync(fetcher);
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.WebFetch, new { url = "https://docs.example.com/ef/querying", reason = "baca dokumentasi" }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Dijawab.", Answer = "Sudah dibaca." }));

        var (first, firstObserver) = await harness.RunAsync("Read the EF docs", ThreadSource.ClaudeCode, "session-web");

        Assert.Equal(ThreadStatus.AwaitingInput, first.Status);
        Assert.Contains("docs.example.com:443", firstObserver.Final);
        Assert.Empty(fetcher.Calls);

        var (second, _) = await harness.RunAsync("2", ThreadSource.ClaudeCode, "session-web");

        Assert.Equal(ThreadStatus.Completed, second.Status);
        Assert.Single(fetcher.Calls);
        Assert.Contains("EF Core querying guide", LastToolMessage(router, 1));
        var grant = Assert.Single(await harness.Service<IApprovalService>().ListAsync(harness.ProjectId));
        Assert.Equal(ApprovalKind.Host, grant.Kind);
        Assert.Equal("docs.example.com:443", grant.Target);

        // The next thread reads that host without asking.
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.WebFetch, new { url = "https://docs.example.com/ef/saving" }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Dijawab.", Answer = "Sudah." }));
        var (third, _) = await harness.RunAsync("Read the saving docs", ThreadSource.ClaudeCode, "session-web");

        Assert.Equal(ThreadStatus.Completed, third.Status);
        Assert.Equal(2, fetcher.Calls.Count);
    }

    [Fact]
    public async Task A_denied_host_tells_the_planner_and_reads_nothing()
    {
        var fetcher = new FakeFetcher("never");
        await using var harness = await CreateWithFetcherAsync(fetcher);
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.WebFetch, new { url = "https://api.example.com/v1/items" }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Dijawab.", Answer = "Tidak dibaca." }));

        await harness.RunAsync("Explore the items API", ThreadSource.ClaudeCode, "session-web-deny");
        var (second, _) = await harness.RunAsync("tolak", ThreadSource.ClaudeCode, "session-web-deny");

        Assert.Equal(ThreadStatus.Completed, second.Status);
        Assert.Empty(fetcher.Calls);
        Assert.Contains("did not allow fetching api.example.com:443", LastToolMessage(router, 1));
    }

    [Fact]
    public async Task Web_fetch_is_offered_only_when_turned_on()
    {
        Assert.Contains(PlannerTools.WebFetch, PlannerTools.Delegable);
        Assert.Contains(PlannerTools.All(canFetch: true), t => t?["function"]?["name"]?.GetValue<string>() == PlannerTools.WebFetch);
        Assert.DoesNotContain(PlannerTools.All(canFetch: false), t => t?["function"]?["name"]?.GetValue<string>() == PlannerTools.WebFetch);
        await Task.CompletedTask;
    }

    private static readonly Func<Uri, CancellationToken, Task<bool>> NoOtherHost = (_, _) => Task.FromResult(false);

    private static WebFetcher NewFetcher() => new(new HandlerFactory());

    private static async Task<OrchestrationHarness> CreateWithFetcherAsync(IWebFetcher fetcher)
    {
        var harness = await OrchestrationHarness.CreateAsync(configureServices: services => services.AddSingleton(fetcher));
        harness.Project.Write(GreeterFile, Greeter);
        await harness.AddStandardNodesAsync();
        return harness;
    }

    private static string LastToolMessage(ScriptedRouter router, int call) =>
        router.CallsTo(ScriptedAgent.Planner)[call].Messages.OfType<JsonObject>().Last(m => m["role"]?.GetValue<string>() == "tool")["content"]!.GetValue<string>();

    private static string Respond(int status, string contentType, string body, string? location = null)
    {
        var bytes = Encoding.UTF8.GetByteCount(body);
        var head = $"HTTP/1.1 {status} {(status is >= 300 and < 400 ? "Found" : "OK")}\r\nContent-Type: {contentType}\r\nContent-Length: {bytes}\r\nConnection: close\r\n";
        if (location is not null) head += $"Location: {location}\r\n";
        return head + "\r\n" + body;
    }

    private sealed class HandlerFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(WebFetcher.CreateHandler(), disposeHandler: true);
    }

    private sealed class FakeFetcher(string text) : IWebFetcher
    {
        public List<Uri> Calls { get; } = [];

        public Task<WebFetchResult> FetchAsync(Uri url, Func<Uri, CancellationToken, Task<bool>> mayVisit, CancellationToken cancellationToken = default)
        {
            lock (Calls) Calls.Add(url);
            return Task.FromResult(new WebFetchResult(url, url, 200, "OK", "text/html", text.Length, Text: text));
        }
    }

    // A minimal HTTP/1.1 server on loopback: one canned response per path, then the connection closes.
    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Dictionary<string, string> _responses;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        public LoopbackServer(Dictionary<string, string> responses)
        {
            _responses = responses;
            _listener.Start();
            _loop = Task.Run(AcceptAsync);
        }

        public Uri Url(string path) => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{path}");

        private async Task AcceptAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    return;
                }
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                var request = new StringBuilder();
                var buffer = new byte[4096];
                while (!request.ToString().Contains("\r\n\r\n"))
                {
                    var read = await stream.ReadAsync(buffer);
                    if (read == 0) return;
                    request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }
                var path = request.ToString().Split(' ')[1];
                var response = _responses.TryGetValue(path, out var found) ? found : Respond(404, "text/plain", "not found");
                await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _loop;
        }
    }
}
