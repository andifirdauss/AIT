namespace AiThread.Service.Implementations;

public sealed partial class NineRouterLauncher
{
    private static string LogTail()
    {
        try { return File.Exists(LogPath) ? Tail(File.ReadAllText(LogPath)) : ""; }
        catch (IOException) { return ""; }
    }
}
