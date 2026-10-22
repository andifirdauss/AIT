namespace AIThread.Service.Testing;

public sealed partial class AppLauncher
{
    // Only localhost is called, usually over the development certificate; redirects are an answer, not followed.
    private static HttpClient CreateClient() =>
        new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true }
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
}
