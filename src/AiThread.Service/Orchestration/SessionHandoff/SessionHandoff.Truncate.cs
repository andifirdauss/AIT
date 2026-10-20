namespace AiThread.Service.Orchestration;

public sealed partial class SessionHandoff
{
    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
