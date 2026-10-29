namespace AIThread.Service.Implementations;

public sealed partial class ThreadService
{
    // The text taken literally inside a LIKE pattern whose escape character is '\'.
    private static string LikeEscape(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
}
