namespace AiThread.Service.Implementations;

public sealed partial class NineRouterLauncher
{
    private static string Tail(string text, int lines = 5) =>
        string.Join(" | ", text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).TakeLast(lines));
}
