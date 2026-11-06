using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    private static NineRouterException NotInstalled(Exception? inner) => new(
        Lang.T("Claude Code CLI (claude) tidak ditemukan di komputer ini. Pasang Claude Code, atau isi AIThread:ClaudeCli:Path di appsettings, lalu coba lagi.",
            "Claude Code CLI (claude) was not found on this computer. Install Claude Code, or set AIThread:ClaudeCli:Path in appsettings, then try again."),
        null, inner)
    {
        // Nothing to retry until someone installs it; a fallback node may still answer.
        IsProviderLoginRequired = true
    };
}
