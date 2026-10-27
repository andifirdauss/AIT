using System.Net;
using System.Net.Sockets;

namespace AIThread.Service.Workspace.Web;

public sealed partial class WebFetcher
{
    // The handler of the "webfetch" client: no automatic redirects (FetchAsync checks each hop), no cookies, no
    // default credentials, and a host name that resolves to a cloud metadata address is never connected to.
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        Credentials = null,
        DefaultProxyCredentials = null,
        PreAuthenticate = false,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var allowed = addresses.Where(a => !WebUrls.IsRefused(a)).ToArray();
            if (allowed.Length == 0) throw new HttpRequestException($"{context.DnsEndPoint.Host} resolves to a cloud metadata address, which is never fetched.");

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };
}
