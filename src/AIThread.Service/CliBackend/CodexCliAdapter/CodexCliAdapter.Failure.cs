using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.CliBackend;

public sealed partial class CodexCliAdapter
{
    public static NineRouterException Failure(string model, int? apiStatus, string detail) =>
        CliFailure.Map(DisplayName,
            Lang.T("Minta pemilik akun menjalankan `codex login` di komputer ini, atau pilih node cadangan.",
                "Ask the account owner to run `codex login` on this computer, or choose a fallback node."),
            model, apiStatus, detail);
}
