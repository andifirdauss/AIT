namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    private HttpClient Client() => httpClientFactory.CreateClient(HttpClientName);
}
