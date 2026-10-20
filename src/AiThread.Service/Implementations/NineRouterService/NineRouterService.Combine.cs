namespace AiThread.Service.Implementations;

public sealed partial class NineRouterService
{
    private static string Combine(string baseUrl, string path) => baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
}
