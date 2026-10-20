using System.Net;
using System.Net.Sockets;

namespace AiThread.Service.Implementations;

public sealed partial class NineRouterLauncher
{
    private static async Task<bool> IsListeningAsync(int port, CancellationToken cancellationToken)
    {
        foreach (var address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
        {
            using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            try
            {
                await socket.ConnectAsync(address, port, timeout.Token);
                return true;
            }
            catch (Exception ex) when (ex is SocketException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)) { }
        }
        return false;
    }
}
