using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.UI.Hosting.Chat;

// A refused request: the server's message (validation, thread not found, ...) or why the server was not reached.
public sealed class ChatApiException(string message) : Exception(message);

// The REST API and the SSE feed of a running AIT, as aithread chat uses them: the same endpoints as the thread page.
public sealed class ChatApiClient(int port) : IDisposable
{
    // As the API writes them: camelCase, enums by name.
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

    // The event stream stays open for the whole thread.
    private readonly HttpClient _stream = new() { Timeout = Timeout.InfiniteTimeSpan };

    public string BaseUrl => $"http://localhost:{port}";

    public string ThreadUrl(Guid threadId) => $"{BaseUrl}/Thread/Detail/{threadId}";

    public Task<IReadOnlyList<ProjectDto>> GetProjectsAsync(CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<ProjectDto>>(HttpMethod.Get, "/api/projects", null, cancellationToken);

    // sessionId: the chat session the prompt belongs to (ChatSessionKey); the server gives the planner its earlier turns.
    public Task<Guid> StartThreadAsync(Guid projectId, string prompt, IReadOnlyList<ImageAttachmentDto>? images, string sessionId,
        CancellationToken cancellationToken) =>
        SendAsync<Guid>(HttpMethod.Post, "/api/threads", new StartThreadRequest(projectId, prompt, images is { Count: > 0 } ? images : null, sessionId),
            cancellationToken);

    public Task<IReadOnlyList<ChatSessionDto>> GetSessionsAsync(Guid projectId, int take, CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<ChatSessionDto>>(HttpMethod.Get, $"/api/threads/sessions?projectId={projectId}&take={take}", null, cancellationToken);

    // The latest threads of a chat session, newest first.
    public Task<IReadOnlyList<ThreadSummaryDto>> GetSessionThreadsAsync(Guid projectId, string sessionId, int take, CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<ThreadSummaryDto>>(HttpMethod.Get,
            $"/api/threads?projectId={projectId}&sessionId={Uri.EscapeDataString(sessionId)}&take={take}", null, cancellationToken);

    public Task<ThreadDetailDto> GetThreadAsync(Guid threadId, CancellationToken cancellationToken) =>
        SendAsync<ThreadDetailDto>(HttpMethod.Get, $"/api/threads/{threadId}", null, cancellationToken);

    public async Task<PendingApprovalDto?> GetApprovalAsync(Guid threadId, CancellationToken cancellationToken) =>
        await SendAsync<PendingApprovalDto?>(HttpMethod.Get, $"/api/threads/{threadId}/approval", null, cancellationToken);

    // The thread that goes on: this one (a commit/push, or the paused planner resumed), a new one only when the planner
    // could not be resumed.
    public Task<Guid> ApproveAsync(Guid threadId, ApprovalDecision decision, CancellationToken cancellationToken) =>
        SendAsync<Guid>(HttpMethod.Post, $"/api/threads/{threadId}/approval", new ApproveRequest(decision), cancellationToken);

    public Task<bool> CancelAsync(Guid threadId, CancellationToken cancellationToken) =>
        SendAsync<bool>(HttpMethod.Post, $"/api/threads/{threadId}/cancel", null, cancellationToken);

    public Task<Guid> RetryAsync(Guid threadId, CancellationToken cancellationToken) =>
        SendAsync<Guid>(HttpMethod.Post, $"/api/threads/{threadId}/retry", null, cancellationToken);

    // Everything about one thread (thread., task. and approval. events) until the token is cancelled or the server
    // closes the stream.
    public async IAsyncEnumerable<AppEvent> StreamAsync(Guid threadId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/stream?threadId={threadId}&types=thread.,task.,approval.,log");
            request.Headers.Accept.ParseAdd("text/event-stream");
            response = await _stream.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            throw new ChatApiException(Unreachable(ex.Message));
        }

        using (response)
        {
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(body, Encoding.UTF8);
            await foreach (var evt in ReadEventsAsync(reader, cancellationToken)) yield return evt;
        }
    }

    // Server-sent events as /api/stream writes them: "event: <type>" and "data: {data, threadId, timestamp}", a blank
    // line ending each event. Several data lines are joined with a newline; comments (":") and unknown fields are
    // skipped, and an event whose data is not JSON keeps a null Data.
    public static async IAsyncEnumerable<AppEvent> ReadEventsAsync(TextReader reader, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? type = null;
        var data = new StringBuilder();
        var hasData = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null || line.Length == 0)
            {
                if (hasData || type is not null)
                {
                    if (ToEvent(type ?? "message", hasData ? data.ToString() : null) is { } evt) yield return evt;
                    type = null;
                    data.Clear();
                    hasData = false;
                }
                if (line is null) yield break;
                continue;
            }

            if (line.StartsWith(':')) continue;
            var colon = line.IndexOf(':');
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? string.Empty : line[(colon + 1)..];
            if (value.StartsWith(' ')) value = value[1..];

            switch (field)
            {
                case "event":
                    type = value;
                    break;
                case "data":
                    if (hasData) data.Append('\n');
                    data.Append(value);
                    hasData = true;
                    break;
            }
        }
    }

    private static AppEvent? ToEvent(string type, string? data)
    {
        if (data is null) return new AppEvent(type, null, null, DateTime.UtcNow);
        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var payload))
                return new AppEvent(type, root.Clone(), null, DateTime.UtcNow);

            Guid? threadId = root.TryGetProperty("threadId", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var parsed) ? parsed : null;
            var timestamp = root.TryGetProperty("timestamp", out var at) && at.ValueKind == JsonValueKind.String && at.TryGetDateTime(out var time) ? time : DateTime.UtcNow;
            return new AppEvent(type, payload.Clone(), threadId, timestamp);
        }
        catch (JsonException)
        {
            return new AppEvent(type, null, null, DateTime.UtcNow);
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BaseUrl + path);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ChatApiException(Unreachable(ex.Message));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatApiException(Lang.T($"AIT di {BaseUrl} tidak menjawab.", $"AIT at {BaseUrl} did not answer."));
        }

        using (response)
        {
            ApiResponse<T>? result;
            try
            {
                result = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(Json, cancellationToken);
            }
            catch (JsonException)
            {
                result = null;
            }

            if (result is { Success: true }) return result.Data!;
            throw new ChatApiException(result?.Message is { Length: > 0 } message ? message : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
    }

    private string Unreachable(string reason) =>
        Lang.T($"Tidak bisa terhubung ke AIT di {BaseUrl}: {reason}", $"Cannot reach AIT at {BaseUrl}: {reason}");

    public void Dispose()
    {
        _http.Dispose();
        _stream.Dispose();
    }
}
