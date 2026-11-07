namespace AIThread.Service.CliBackend;

public sealed partial class GeminiCliAdapter
{
    // The user turn for stdin. Gemini CLI takes no image on stdin (an @file would need its read tool, which is off), so
    // attached images are named in a note. A turn starting with "/" would run as one of the CLI's slash commands, so it
    // gets a label first.
    public static string UserText(CliPrompt prompt)
    {
        var text = string.IsNullOrWhiteSpace(prompt.User) ? "Continue." : prompt.User;
        if (text.TrimStart().StartsWith('/')) text = "User message:\n" + text;
        return CliProcess.WithImageNote(text, prompt.Images.Count);
    }
}
