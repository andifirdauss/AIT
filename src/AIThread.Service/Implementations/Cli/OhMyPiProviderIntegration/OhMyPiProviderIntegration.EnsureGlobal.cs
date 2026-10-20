using AIThread.Common.Enums;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations.Cli;

public sealed partial class OhMyPiProviderIntegration
{
    private static void EnsureGlobal(CliScope scope)
    {
        if (scope != CliScope.Global) throw new AppValidationException("models.yml oh-my-pi hanya ada di level global (per pengguna).");
    }
}
