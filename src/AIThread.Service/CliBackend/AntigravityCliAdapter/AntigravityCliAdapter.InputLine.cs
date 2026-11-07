using System.Text.Json.Nodes;

namespace AIThread.Service.CliBackend;

public sealed partial class AntigravityCliAdapter
{
    // Closes the user turn when the system prompt holds the written tool protocol: Gemini models lean towards native
    // function calls when they see tool schemas, which agy rejects.
    private const string ToolReminder =
        "(Reply as text. To use a tool, write <tool_call> blocks exactly as the instructions describe; do not emit a native function call.)";

    // The user turn as one stream-json line. The CLI takes text blocks only (any other block ends the session with an
    // error), so attached images are named in a note.
    public static string InputLine(CliPrompt prompt)
    {
        var text = CliProcess.WithImageNote(string.IsNullOrWhiteSpace(prompt.User) ? "Continue." : prompt.User, prompt.Images.Count);
        if (prompt.System.Contains("<" + CliTranscript.ToolCallTag, StringComparison.Ordinal)) text += "\n\n" + ToolReminder;
        return new JsonObject
        {
            ["event"] = "user",
            ["message"] = new JsonObject { ["content"] = text }
        }.ToJsonString();
    }
}
