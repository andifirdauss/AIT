using System.Text.Json.Nodes;
using AIThread.Service.Proxy;

namespace AIThread.Tests;

public class ProtocolAdapterTests
{
    private static readonly IReadOnlyDictionary<string, string?> NoHeaders = new Dictionary<string, string?>();

    private static ClaudeRequestInfo Inspect(ClientProtocol protocol, JsonObject body, string? model = null, bool? stream = null) =>
        ClaudeRequestInspector.Inspect(ProtocolAdapter.ToCanonical(protocol, body, model, stream), NoHeaders, 400);

    [Fact]
    public void OpenAi_chat_turn_is_orchestrated()
    {
        var body = JsonNode.Parse("""
            {
              "model": "gpt-5", "stream": true,
              "tools": [{ "type": "function", "function": { "name": "bash" } }],
              "messages": [
                { "role": "system", "content": "You are a coding agent.\nCurrent working directory: D:\\Source\\MyApp" },
                { "role": "user", "content": "earlier" },
                { "role": "assistant", "content": "earlier answer" },
                { "role": "user", "content": [{ "type": "text", "text": "Add a description column" }] }
              ]
            }
            """)!.AsObject();

        var info = Inspect(ClientProtocol.OpenAiChat, body);

        Assert.True(info.IsOrchestratable);
        Assert.True(info.Stream);
        Assert.Equal("gpt-5", info.Model);
        Assert.Equal(@"D:\Source\MyApp", info.WorkingDirectory);
        Assert.Equal("Add a description column", info.Prompt);
        Assert.Contains("earlier answer", info.ConversationContext);
    }

    [Fact]
    public void OpenAi_chat_tool_result_turn_is_passed_through()
    {
        var body = JsonNode.Parse("""
            {
              "model": "gpt-5",
              "tools": [{ "type": "function", "function": { "name": "bash" } }],
              "messages": [
                { "role": "user", "content": "run ls" },
                { "role": "assistant", "content": null, "tool_calls": [{ "id": "c1", "type": "function", "function": { "name": "bash", "arguments": "{}" } }] },
                { "role": "tool", "tool_call_id": "c1", "content": "a.txt" }
              ]
            }
            """)!.AsObject();

        var info = Inspect(ClientProtocol.OpenAiChat, body);

        Assert.False(info.IsOrchestratable);
        Assert.Equal("tool_result turn", info.Reason);
    }

    [Fact]
    public void OpenAi_chat_without_tools_is_passed_through()
    {
        var body = JsonNode.Parse("""{ "model": "gpt-5", "messages": [{ "role": "user", "content": "title please" }] }""")!.AsObject();

        Assert.False(Inspect(ClientProtocol.OpenAiChat, body).IsOrchestratable);
    }

    [Fact]
    public void Codex_responses_turn_reads_cwd_tag_and_session()
    {
        var body = JsonNode.Parse("""
            {
              "model": "gpt-5-codex", "stream": true,
              "instructions": "You are Codex.",
              "prompt_cache_key": "0b7a1c52-8a5e-4f3c-9d7e-1a2b3c4d5e6f",
              "tools": [{ "type": "function", "name": "shell" }],
              "input": [
                { "type": "message", "role": "user", "content": [{ "type": "input_text", "text": "<environment_context>\n  <cwd>D:\\Source\\MyApp</cwd>\n</environment_context>" }] },
                { "type": "message", "role": "user", "content": [{ "type": "input_text", "text": "Add a description column" }] }
              ]
            }
            """)!.AsObject();

        var info = Inspect(ClientProtocol.OpenAiResponses, body);

        Assert.True(info.IsOrchestratable);
        Assert.Equal(@"D:\Source\MyApp", info.WorkingDirectory);
        Assert.Equal("0b7a1c52-8a5e-4f3c-9d7e-1a2b3c4d5e6f", info.SessionId);
        Assert.Equal("Add a description column", info.Prompt);
    }

    [Fact]
    public void Codex_function_call_output_turn_is_passed_through()
    {
        var body = JsonNode.Parse("""
            {
              "model": "gpt-5-codex",
              "tools": [{ "type": "function", "name": "shell" }],
              "input": [
                { "type": "message", "role": "user", "content": [{ "type": "input_text", "text": "run ls" }] },
                { "type": "function_call", "name": "shell", "call_id": "c1", "arguments": "{}" },
                { "type": "function_call_output", "call_id": "c1", "output": "a.txt" }
              ]
            }
            """)!.AsObject();

        Assert.Equal("tool_result turn", Inspect(ClientProtocol.OpenAiResponses, body).Reason);
    }

    [Fact]
    public void Responses_string_input_is_a_user_prompt()
    {
        var body = JsonNode.Parse("""{ "model": "m", "tools": [{ "type": "function", "name": "shell" }], "input": "do it" }""")!.AsObject();

        var info = Inspect(ClientProtocol.OpenAiResponses, body);

        Assert.True(info.IsOrchestratable);
        Assert.Equal("do it", info.Prompt);
    }

    [Fact]
    public void Gemini_turn_takes_model_and_stream_from_the_url()
    {
        var body = JsonNode.Parse("""
            {
              "systemInstruction": { "parts": [{ "text": "You are Gemini CLI." }] },
              "tools": [{ "functionDeclarations": [{ "name": "run_shell_command" }] }],
              "contents": [
                { "role": "user", "parts": [{ "text": "I'm currently working in the directory: D:\\Source\\MyApp" }] },
                { "role": "model", "parts": [{ "text": "Got it." }] },
                { "role": "user", "parts": [{ "text": "Add a description column" }] }
              ]
            }
            """)!.AsObject();

        var info = Inspect(ClientProtocol.GeminiGenerate, body, "gemini-2.5-pro", stream: true);

        Assert.True(info.IsOrchestratable);
        Assert.True(info.Stream);
        Assert.Equal("gemini-2.5-pro", info.Model);
        Assert.Equal(@"D:\Source\MyApp", info.WorkingDirectory);
        Assert.Equal("Add a description column", info.Prompt);
    }

    [Fact]
    public void Gemini_function_response_turn_is_passed_through()
    {
        var body = JsonNode.Parse("""
            {
              "tools": [{ "functionDeclarations": [{ "name": "run_shell_command" }] }],
              "contents": [
                { "role": "user", "parts": [{ "text": "run ls" }] },
                { "role": "model", "parts": [{ "functionCall": { "name": "run_shell_command", "args": {} } }] },
                { "role": "user", "parts": [{ "functionResponse": { "name": "run_shell_command", "response": {} } }] }
              ]
            }
            """)!.AsObject();

        Assert.Equal("tool_result turn", Inspect(ClientProtocol.GeminiGenerate, body, "gemini-2.5-pro").Reason);
    }

    [Fact]
    public void OpenAi_chat_image_url_data_uri_is_kept_as_an_image()
    {
        var body = JsonNode.Parse("""
            {
              "model": "gpt-5", "stream": true,
              "tools": [{ "type": "function", "function": { "name": "bash" } }],
              "messages": [
                { "role": "user", "content": [
                  { "type": "text", "text": "What's disabled here?" },
                  { "type": "image_url", "image_url": { "url": "data:image/png;base64,aGVsbG8=" } }
                ] }
              ]
            }
            """)!.AsObject();

        var info = Inspect(ClientProtocol.OpenAiChat, body);

        Assert.True(info.IsOrchestratable);
        Assert.Equal("What's disabled here?", info.Prompt);
        var image = Assert.Single(info.Images);
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal("aGVsbG8=", image.Data);
    }

    [Fact]
    public void Gemini_inline_data_part_is_kept_as_an_image()
    {
        var body = JsonNode.Parse("""
            {
              "tools": [{ "functionDeclarations": [{ "name": "run_shell_command" }] }],
              "contents": [
                { "role": "user", "parts": [
                  { "text": "What's disabled here?" },
                  { "inlineData": { "mimeType": "image/png", "data": "aGVsbG8=" } }
                ] }
              ]
            }
            """)!.AsObject();

        var info = Inspect(ClientProtocol.GeminiGenerate, body, "gemini-2.5-pro");

        Assert.True(info.IsOrchestratable);
        Assert.Equal("What's disabled here?", info.Prompt);
        var image = Assert.Single(info.Images);
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal("aGVsbG8=", image.Data);
    }

    [Theory]
    [InlineData(ClientProtocol.AnthropicMessages, "/v1/messages?beta=true")]
    [InlineData(ClientProtocol.OpenAiChat, "/v1/chat/completions?beta=true")]
    [InlineData(ClientProtocol.OpenAiResponses, "/v1/responses?beta=true")]
    [InlineData(ClientProtocol.GeminiGenerate, "/v1beta/models/cc/claude-opus:streamGenerateContent?beta=true")]
    public void Router_path_matches_the_client_protocol(ClientProtocol protocol, string expected) =>
        Assert.Equal(expected, ProtocolAdapter.RouterPath(protocol, "cc/claude-opus", "streamGenerateContent", "?beta=true"));
}
