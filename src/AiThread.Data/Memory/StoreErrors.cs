using System.Data.Common;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace AiThread.Data.Memory;

// Tells "the database is down" (server stopped, connection refused, timeout) apart from a bad query or schema, so
// callers can skip work quietly instead of logging a stack trace per row while a store is offline.
public static class StoreErrors
{
    public static bool IsUnreachable(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is SocketException or TimeoutException or DbException { IsTransient: true }) return true;
        }
        return false;
    }

    // An unreachable store logs one line with the provider's message; anything else keeps its stack trace.
    public static void LogStoreWarning(this ILogger logger, Exception ex, string what)
    {
        if (IsUnreachable(ex)) logger.LogWarning("{What}: store unreachable ({Error}).", what, MemoryStoreTester.ErrorMessage(ex));
        else logger.LogWarning(ex, "{What}.", what);
    }
}
