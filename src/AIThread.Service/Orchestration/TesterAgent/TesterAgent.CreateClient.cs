using System.Net;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // Per run: its own cookies, no redirects followed (a 302 to the login page must show), any certificate accepted and
    // the Windows login of this account for apps with Windows authentication (IIS, Negotiate) — only localhost is ever
    // called, usually over the development certificate.
    private static HttpClient CreateClient() =>
        new(new SocketsHttpHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = false,
            Credentials = CredentialCache.DefaultCredentials,
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true }
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
}
