namespace AIThread.Common.Enums;

public enum ThreadSource
{
    // Any coding CLI through the proxy (Anthropic, OpenAI or Gemini format). The name predates the other clients and
    // is kept because it is stored in the database and sent to the UI.
    ClaudeCode = 1,
    Dashboard = 2,
    Telegram = 3
}
