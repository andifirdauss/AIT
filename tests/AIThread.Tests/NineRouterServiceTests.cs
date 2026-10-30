using System.Net;
using System.Text;
using AIThread.Common.DTOs;
using AIThread.Service.Implementations;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIThread.Tests;

public class NineRouterServiceTests
{
    private const string GoodKey = "sk-good";

    // Mimics 9router: health and models are public; chat checks the key before the body when requireApiKey is on.
    private static NineRouterService Create(string? storedKey, bool requireApiKey)
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/health") return Json(HttpStatusCode.OK, "{\"ok\":true}");
            if (path == "/v1/models") return Json(HttpStatusCode.OK, "{\"object\":\"list\",\"data\":[{\"id\":\"cc/claude-opus-5-5\",\"owned_by\":\"cc\"}]}");

            var auth = request.Headers.Authorization?.Parameter;
            if (requireApiKey && auth is null) return Json(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"Missing API key\"}}");
            if (requireApiKey && auth != GoodKey) return Json(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"Invalid API key\"}}");
            return Json(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Missing model\"}}");
        });

        return new NineRouterService(new StubFactory(handler), new StubSettings(storedKey), TestSecrets.Guard(), NullLogger<NineRouterService>.Instance);
    }

    [Fact]
    public async Task Status_flags_missing_key_when_router_requires_one()
    {
        var status = await Create(storedKey: null, requireApiKey: true).CheckStatusAsync();

        Assert.True(status.IsOnline);
        Assert.False(status.HasApiKey);
        Assert.False(status.ApiKeyValid);
        Assert.Equal(NineRouterService.KeyMissingMessage, status.Error);
    }

    [Fact]
    public async Task Status_is_clean_without_key_when_router_does_not_require_one()
    {
        var status = await Create(storedKey: null, requireApiKey: false).CheckStatusAsync();

        Assert.True(status.IsOnline);
        Assert.Null(status.ApiKeyValid);
        Assert.Null(status.Error);
    }

    [Theory]
    [InlineData(GoodKey, true)]
    [InlineData("sk-wrong", false)]
    public async Task Status_reports_whether_the_stored_key_is_accepted(string key, bool valid)
    {
        var error = valid ? null : NineRouterService.KeyRejectedMessage;
        var status = await Create(key, requireApiKey: true).CheckStatusAsync();

        Assert.True(status.HasApiKey);
        Assert.Equal(valid, status.ApiKeyValid);
        Assert.Equal(error, status.Error);
    }

    [Fact]
    public async Task Chat_without_stored_key_explains_how_to_fix_it()
    {
        var router = Create(storedKey: null, requireApiKey: true);

        var ex = await Assert.ThrowsAsync<NineRouterException>(() => router.ChatAsync(new ChatCompletionRequest
        {
            Model = "cc/claude-opus-5-5",
            Messages = [ChatMessages.User("hi")]
        }));

        Assert.Equal(401, ex.StatusCode);
        Assert.False(ex.IsTransient);
        Assert.Contains("Missing API key", ex.Message);
        Assert.Contains(NineRouterService.KeyMissingMessage, ex.Message);
    }

    [Fact]
    public async Task Rate_limited_chat_honours_retry_after_and_then_succeeds()
    {
        var calls = 0;
        var handler = new StubHandler(_ =>
        {
            if (++calls == 1)
            {
                var limited = Json(HttpStatusCode.TooManyRequests, "{\"error\":{\"message\":\"Rate limit reached\"}}");
                limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return limited;
            }
            return Json(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}");
        });
        var router = new NineRouterService(new StubFactory(handler), new StubSettings(GoodKey), TestSecrets.Guard(), NullLogger<NineRouterService>.Instance);

        var result = await router.ChatAsync(new ChatCompletionRequest { Model = "cc/claude-opus-5-5", Messages = [ChatMessages.User("hi")] });

        Assert.Equal("ok", result.Content);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Provider_logged_out_is_reported_once_without_retrying_and_allows_fail_over()
    {
        var calls = 0;
        const string body = "{\"error\":{\"message\":\"[claude/claude-opus-5-5] [401]: {\\\"type\\\":\\\"error\\\",\\\"error\\\":{\\\"type\\\":\\\"authentication_error\\\",\\\"message\\\":\\\"OAuth access token has expired. Re-authenticate to continue.\\\"}} (reset after 2m)\"}}";
        var handler = new StubHandler(_ =>
        {
            calls++;
            return Json(HttpStatusCode.ServiceUnavailable, body);
        });
        var router = new NineRouterService(new StubFactory(handler), new StubSettings(GoodKey), TestSecrets.Guard(), NullLogger<NineRouterService>.Instance);

        var ex = await Assert.ThrowsAsync<NineRouterException>(() =>
            router.ChatAsync(new ChatCompletionRequest { Model = "cc/claude-opus-5-5", Messages = [ChatMessages.User("hi")] }));

        Assert.Equal(1, calls);
        Assert.True(ex.IsProviderLoginRequired);
        Assert.False(ex.IsTransient);
        Assert.True(ex.IsModelUnavailable);
        Assert.Contains("perlu login ulang (OAuth access token has expired. Re-authenticate to continue.)", ex.Message);
        Assert.DoesNotContain("{", ex.Message);
    }

    [Theory]
    [InlineData("stop", "tanpa teks dan tanpa tool call")]
    [InlineData("length", "max tokens")]
    public async Task Empty_reply_is_a_provider_failure_without_retrying_that_allows_fail_over(string finish, string reason)
    {
        var calls = 0;
        var handler = new StubHandler(_ =>
        {
            calls++;
            return Json(HttpStatusCode.OK, $"{{\"choices\":[{{\"message\":{{\"content\":\"\"}},\"finish_reason\":\"{finish}\"}}]}}");
        });
        var router = new NineRouterService(new StubFactory(handler), new StubSettings(GoodKey), TestSecrets.Guard(), NullLogger<NineRouterService>.Instance);

        var ex = await Assert.ThrowsAsync<NineRouterException>(() =>
            router.ChatAsync(new ChatCompletionRequest { Model = "csi/empty-model", Messages = [ChatMessages.User("hi")] }));

        Assert.Equal(1, calls);
        Assert.True(ex.IsEmptyReply);
        Assert.False(ex.IsTransient);
        Assert.True(ex.IsModelUnavailable);
        Assert.Contains("'csi/empty-model' mengembalikan jawaban kosong", ex.Message);
        Assert.Contains(reason, ex.Message);
    }

    // An executor got "<<<<<<<" then "<<<<<<< SEARCH\n    private const" with finish "length": the limit went to
    // reasoning, so the model is not asked again for "smaller blocks" but treated like an empty reply (fail-over).
    [Theory]
    [InlineData("<<<<<<<", 0)]
    [InlineData("<<<<<<< SEARCH\\n    private const string ValueName = \\\"AIThread\\\";\\n    private const string Legacy", 4000)]
    public async Task Reply_cut_by_max_tokens_after_a_few_characters_is_spent_on_reasoning(string content, int completionTokens)
    {
        var calls = 0;
        var handler = new StubHandler(_ =>
        {
            calls++;
            return Json(HttpStatusCode.OK,
                $"{{\"choices\":[{{\"message\":{{\"content\":\"{content}\"}},\"finish_reason\":\"length\"}}],\"usage\":{{\"prompt_tokens\":900,\"completion_tokens\":{completionTokens}}}}}");
        });
        var router = new NineRouterService(new StubFactory(handler), new StubSettings(GoodKey), TestSecrets.Guard(), NullLogger<NineRouterService>.Instance);

        var ex = await Assert.ThrowsAsync<NineRouterException>(() =>
            router.ChatAsync(new ChatCompletionRequest { Model = "qd/efficient", Messages = [ChatMessages.User("hi")] }));

        Assert.Equal(1, calls);
        Assert.True(ex.IsEmptyReply);
        Assert.True(ex.IsModelUnavailable);
        Assert.Contains("reasoning", ex.Message);
    }

    [Fact]
    public async Task Long_reply_cut_by_max_tokens_is_still_returned()
    {
        var text = string.Concat(Enumerable.Repeat("word ", 400));
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            $"{{\"choices\":[{{\"message\":{{\"content\":\"{text}\"}},\"finish_reason\":\"length\"}}],\"usage\":{{\"prompt_tokens\":900,\"completion_tokens\":400}}}}"));
        var router = new NineRouterService(new StubFactory(handler), new StubSettings(GoodKey), TestSecrets.Guard(), NullLogger<NineRouterService>.Instance);

        var result = await router.ChatAsync(new ChatCompletionRequest { Model = "m", Messages = [ChatMessages.User("hi")] });

        Assert.Equal("length", result.FinishReason);
        Assert.Equal(text, result.Content);
    }

    [Fact]
    public async Task Prompt_cache_key_is_sent_and_dropped_for_a_model_whose_provider_rejects_it()
    {
        var bodies = new List<string>();
        var handler = new StubHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().Result;
            bodies.Add(body);
            return body.Contains("prompt_cache_key") && body.Contains("strict/model")
                ? Json(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Unrecognized request argument supplied: prompt_cache_key\"}}")
                : Json(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}");
        });
        var router = new NineRouterService(new StubFactory(handler), new StubSettings(GoodKey), TestSecrets.Guard(), NullLogger<NineRouterService>.Instance);
        ChatCompletionRequest Request(string model) => new()
        {
            Model = model,
            Messages = [ChatMessages.User("hi")],
            PromptCacheKey = "ait:abc:planner"
        };

        Assert.Equal("ok", (await router.ChatAsync(Request("cc/claude-opus-5-5"))).Content);
        Assert.Contains("\"prompt_cache_key\":\"ait:abc:planner\"", bodies[0]);

        Assert.Equal("ok", (await router.ChatAsync(Request("strict/model"))).Content);
        Assert.Equal(3, bodies.Count);
        Assert.DoesNotContain("prompt_cache_key", bodies[2]);

        // Remembered: the next call to that model goes without the key at once.
        await router.ChatAsync(Request("strict/model"));
        Assert.Equal(4, bodies.Count);
        Assert.DoesNotContain("prompt_cache_key", bodies[3]);
    }

    [Fact]
    public async Task A_stream_that_goes_silent_is_dropped_and_retried_instead_of_waiting_for_the_request_timeout()
    {
        var calls = 0;
        var handler = new StubHandler(_ =>
        {
            if (++calls > 1) return Json(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}");
            var silent = new StreamContent(new SilentAfterStream("data: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}\n\n"));
            silent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = silent };
        });
        var router = new NineRouterService(new StubFactory(handler), new StubSettings(GoodKey), TestSecrets.Guard(), NullLogger<NineRouterService>.Instance,
            stallTimeout: TimeSpan.FromMilliseconds(300));

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var result = await router.ChatAsync(new ChatCompletionRequest { Model = "cc/claude-opus-5-5", Messages = [ChatMessages.User("hi")] });

        Assert.Equal("ok", result.Content);
        Assert.Equal(2, calls);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), "the silent stream waited for the request timeout");
    }

    [Fact]
    public void Long_retry_after_is_not_waited_on_so_the_caller_can_fail_over()
    {
        var ex = new NineRouterException("limited", 429, retryAfter: TimeSpan.FromMinutes(5));

        Assert.True(ex.IsModelUnavailable);
        Assert.Null(NineRouterService.RetryDelay(ex, 1));
    }

    [Fact]
    public void Rate_limit_without_retry_after_backs_off_longer_than_other_errors()
    {
        var rateLimited = NineRouterService.RetryDelay(new NineRouterException("limited", 429), 1)!.Value;
        var serverError = NineRouterService.RetryDelay(new NineRouterException("boom", 502), 1)!.Value;

        Assert.InRange(rateLimited.TotalSeconds, 5, 7.5);
        Assert.InRange(serverError.TotalSeconds, 2, 3);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    // Gives its text, then blocks every read until cancelled: a connection that dropped without closing.
    private sealed class SilentAfterStream(string text) : Stream
    {
        private readonly MemoryStream _head = new(Encoding.UTF8.GetBytes(text));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = _head.Read(buffer.Span);
            if (read > 0) return read;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubSettings(string? key) : ISettingService
    {
        public Task<RuntimeSettings> GetRuntimeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RuntimeSettings("http://router.test", key, null, null, true, true, 24, 1, 8, 60));

        public Task<SystemSettingDto> GetAsync(string proxyBaseUrl, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SystemSettingDto> UpdateAsync(UpdateSystemSettingRequest request, string proxyBaseUrl, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RuntimeSettings> UpdateRouterAsync(string baseUrl, string? apiKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TelegramSettingsDto> GetTelegramAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TelegramSettingsDto> UpdateTelegramAsync(UpdateTelegramSettingsRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RuntimeSettings> UpdateSkillsAsync(UpdateSkillSettingsRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RuntimeSettings> UpdateBusinessAsync(UpdateBusinessSettingsRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RuntimeSettings> UpdateLanguageAsync(string language, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
