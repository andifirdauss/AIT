using System.Text;
using System.Text.Json.Nodes;
using AiThread.API.Infrastructure;
using AiThread.Service.Proxy;
using Microsoft.AspNetCore.Http;

namespace AiThread.Tests;

public class ClientStreamWriterTests
{
    private static async Task<string> StreamAsync(ClientProtocol protocol, params string[] deltas)
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        await using (var writer = ClientResponses.CreateWriter(protocol, context.Response, "m"))
        {
            await writer.StartAsync(100, CancellationToken.None);
            foreach (var delta in deltas) await writer.TextAsync(delta, CancellationToken.None);
            await writer.FinishAsync(CancellationToken.None);
        }

        Assert.Equal("text/event-stream; charset=utf-8", context.Response.ContentType);
        return Encoding.UTF8.GetString(body.ToArray());
    }

    // "data: {json}" payloads of every frame that carries JSON.
    private static List<JsonObject> Payloads(string stream) =>
        stream.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(frame => frame.Split('\n'))
            .Where(line => line.StartsWith("data: {"))
            .Select(line => JsonNode.Parse(line[6..])!.AsObject())
            .ToList();

    [Fact]
    public async Task Anthropic_stream_keeps_its_event_sequence()
    {
        var stream = await StreamAsync(ClientProtocol.AnthropicMessages, "Hel", "lo");

        var events = stream.Split('\n').Where(l => l.StartsWith("event: ")).Select(l => l[7..]).ToList();
        Assert.Equal(["message_start", "content_block_start", "ping", "content_block_delta", "content_block_delta", "content_block_stop", "message_delta", "message_stop"], events);
    }

    [Fact]
    public async Task OpenAi_chat_stream_ends_with_stop_and_done()
    {
        var stream = await StreamAsync(ClientProtocol.OpenAiChat, "Hel", "lo");

        var chunks = Payloads(stream);
        var text = string.Concat(chunks.Select(c => c["choices"]![0]!["delta"]!["content"]?.GetValue<string>()));
        Assert.Equal("Hello", text);
        Assert.Equal("stop", chunks[^1]["choices"]![0]!["finish_reason"]!.GetValue<string>());
        Assert.EndsWith("data: [DONE]\n\n", stream);
    }

    [Fact]
    public async Task Responses_stream_completes_with_the_full_text()
    {
        var stream = await StreamAsync(ClientProtocol.OpenAiResponses, "Hel", "lo");

        var payloads = Payloads(stream);
        Assert.Equal(
            ["response.created", "response.in_progress", "response.output_item.added", "response.content_part.added",
             "response.output_text.delta", "response.output_text.delta", "response.output_text.done",
             "response.content_part.done", "response.output_item.done", "response.completed"],
            payloads.Select(p => p["type"]!.GetValue<string>()));
        Assert.Equal(Enumerable.Range(0, payloads.Count), payloads.Select(p => p["sequence_number"]!.GetValue<int>()));

        var completed = payloads[^1]["response"]!;
        Assert.Equal("completed", completed["status"]!.GetValue<string>());
        Assert.Equal("Hello", completed["output"]![0]!["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Gemini_stream_finishes_with_stop_and_usage()
    {
        var stream = await StreamAsync(ClientProtocol.GeminiGenerate, "Hel", "lo");

        var chunks = Payloads(stream);
        var text = string.Concat(chunks.Select(c => c["candidates"]![0]!["content"]!["parts"]![0]!["text"]!.GetValue<string>()));
        Assert.Equal("Hello", text);
        Assert.Equal("STOP", chunks[^1]["candidates"]![0]!["finishReason"]!.GetValue<string>());
        Assert.Equal(100, chunks[^1]["usageMetadata"]!["promptTokenCount"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(ClientProtocol.AnthropicMessages, "content")]
    [InlineData(ClientProtocol.OpenAiChat, "choices")]
    [InlineData(ClientProtocol.OpenAiResponses, "output")]
    [InlineData(ClientProtocol.GeminiGenerate, "candidates")]
    public void Non_stream_message_has_the_protocol_shape(ClientProtocol protocol, string field)
    {
        var json = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(ClientResponses.Message(protocol, "m", "Hello", 100)))!;

        Assert.NotNull(json[field]);
        Assert.Contains("Hello", json.ToJsonString());
    }

    [Theory]
    [InlineData(ClientProtocol.AnthropicMessages, "type")]
    [InlineData(ClientProtocol.OpenAiChat, "error")]
    [InlineData(ClientProtocol.GeminiGenerate, "error")]
    public void Error_body_has_the_protocol_shape(ClientProtocol protocol, string field)
    {
        var json = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(ClientResponses.Error(protocol, 401, "authentication_error", "nope")))!;

        Assert.NotNull(json[field]);
        Assert.Contains("nope", json.ToJsonString());
    }
}
