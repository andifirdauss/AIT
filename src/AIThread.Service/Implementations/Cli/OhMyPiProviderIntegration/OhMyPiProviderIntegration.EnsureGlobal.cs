using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations.Cli;

public sealed partial class OhMyPiProviderIntegration
{
    private static void EnsureGlobal(CliScope scope)
    {
        if (scope != CliScope.Global) throw new AppValidationException(Lang.T("models.yml oh-my-pi hanya ada di level global (per pengguna).", "The oh-my-pi models.yml exists at the global level (per user) only."));
    }
}
