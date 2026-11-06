using System.Text.Json.Nodes;
using AIThread.Service.Proxy;

namespace AIThread.Tests;

public class PassthroughTranscriptTests
{
    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    private static string Content(JsonNode? message) => message?["content"] switch
    {
        JsonValue v => v.GetValue<string>(),
        JsonArray parts => string.Join("\n", parts.Select(p => p?["text"]?.GetValue<string>()).Where(t => t is not null)),
        _ => string.Empty
    };

    [Fact]
    public void Anthropic_request_keeps_system_text_tool_history_and_images()
    {
        var body = Parse("""
            {
              "model": "claude-haiku-5-5",
              "system": [{ "type": "text", "text": "Generate a short title." }],
              "tools": [{ "name": "Read", "input_schema": { "type": "object" } }],
              "messages": [
                { "role": "user", "content": [
                  { "type": "text", "text": "Read a.cs" },
                  { "type": "image", "source": { "type": "base64", "media_type": "image/png", "data": "AAA=" } } ] },
                { "role": "assistant", "content": [{ "type": "tool_use", "id": "t1", "name": "Read", "input": { "path": "a.cs" } }] },
                { "role": "user", "content": [{ "type": "tool_result", "tool_use_id": "t1", "content": "class A {}" }] },
                { "role": "user", "content": "Now summarize." }
              ]
            }
            """);

        var (messages, hadTools) = PassthroughTranscript.ToMessages(ClientProtocol.AnthropicMessages, body);

        Assert.True(hadTools);
        Assert.Equal("system", messages[0]!["role"]!.GetValue<string>());
        Assert.Contains("Generate a short title.", Content(messages[0]));
        Assert.Contains(PassthroughTranscript.TextOnlyNote, Content(messages[0]));
        Assert.Equal(4, messages.Count);
        Assert.Contains("Read a.cs", Content(messages[1]));
        Assert.Contains(messages[1]!["content"]!.AsArray(), p => p?["type"]?.GetValue<string>() == "image_url");
        Assert.Contains("[Called tool Read with {\"path\":\"a.cs\"}]", Content(messages[2]));
        var last = Content(messages[3]);
        Assert.Contains("[Tool result]\nclass A {}", last);
        Assert.Contains("Now summarize.", last);
    }

    [Fact]
    public void Anthropic_request_without_tools_has_no_text_only_note()
    {
        var (messages, hadTools) = PassthroughTranscript.ToMessages(ClientProtocol.AnthropicMessages,
            Parse("""{ "system": "Be brief.", "messages": [{ "role": "user", "content": "hi" }] }"""));

        Assert.False(hadTools);
        Assert.DoesNotContain(PassthroughTranscript.TextOnlyNote, Content(messages[0]));
        Assert.Equal("hi", Content(messages[1]));
    }

    [Fact]
    public void OpenAi_chat_request_maps_roles_and_tool_messages()
    {
        var body = Parse("""
            {
              "messages": [
                { "role": "developer", "content": "Rules." },
                { "role": "user", "content": "List files" },
                { "role": "assistant", "content": null, "tool_calls": [{ "id": "c1", "type": "function", "function": { "name": "ls", "arguments": "{}" } }] },
                { "role": "tool", "tool_call_id": "c1", "content": "a.cs" }
              ]
            }
            """);

        var (messages, _) = PassthroughTranscript.ToMessages(ClientProtocol.OpenAiChat, body);

        Assert.Equal(["system", "user", "assistant", "user"], messages.Select(m => m!["role"]!.GetValue<string>()));
        Assert.Contains("[Called tool ls with {}]", Content(messages[2]));
        Assert.Contains("a.cs", Content(messages[3]));
    }

    [Fact]
    public void OpenAi_responses_and_gemini_requests_become_turns()
    {
        var (responses, _) = PassthroughTranscript.ToMessages(ClientProtocol.OpenAiResponses, Parse("""
            { "instructions": "Be Codex.", "input": [
              { "type": "message", "role": "user", "content": [{ "type": "input_text", "text": "hello" }] },
              { "type": "function_call", "name": "shell", "arguments": "{\"cmd\":\"ls\"}" },
              { "type": "function_call_output", "output": "a.cs" } ] }
            """));
        Assert.Equal(["system", "user", "assistant", "user"], responses.Select(m => m!["role"]!.GetValue<string>()));
        Assert.Contains("Be Codex.", Content(responses[0]));
        Assert.Contains("a.cs", Content(responses[3]));

        var (gemini, _) = PassthroughTranscript.ToMessages(ClientProtocol.GeminiGenerate, Parse("""
            { "systemInstruction": { "parts": [{ "text": "Be Gemini." }] }, "contents": [
              { "role": "user", "parts": [{ "text": "hi" }] },
              { "role": "model", "parts": [{ "functionCall": { "name": "read", "args": { "p": 1 } } }] },
              { "role": "user", "parts": [{ "functionResponse": { "name": "read", "response": { "ok": true } } }] } ] }
            """));
        Assert.Equal(["system", "user", "assistant", "user"], gemini.Select(m => m!["role"]!.GetValue<string>()));
        Assert.Contains("[Called tool read with {\"p\":1}]", Content(gemini[2]));
    }
}
