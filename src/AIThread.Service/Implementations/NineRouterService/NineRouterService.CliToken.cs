using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    // 9router's CLI token, only for a 9router on this machine: the secret behind it belongs to this machine and must
    // not go to someone else's server. Null when it cannot be read; the request then goes without it.
    private string? CliToken(string baseUrl)
    {
        if (!NineRouterLocator.TryGetLocalPort(baseUrl, out _)) return null;
        try
        {
            var directory = RouterCliToken.DataDirectory(
                Environment.GetEnvironmentVariable("DATA_DIR"),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                OperatingSystem.IsWindows());
            return RouterCliToken.Load(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug("9router CLI token unavailable: {Message}", ex.Message);
            return null;
        }
    }
}
