namespace AiThread.Service.Implementations;

public sealed partial class NineRouterService
{
    private HttpClient Client() => httpClientFactory.CreateClient(HttpClientName);
}
