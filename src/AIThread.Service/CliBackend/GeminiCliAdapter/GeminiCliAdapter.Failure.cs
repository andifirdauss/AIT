using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.CliBackend;

public sealed partial class GeminiCliAdapter
{
    public static NineRouterException Failure(string model, int? apiStatus, string detail, bool loginFailed = false) =>
        CliFailure.Map(DisplayName,
            Lang.T("Minta pemilik akun menjalankan `gemini` lalu `/auth` (mis. Login with Google) di komputer ini, atau pilih node cadangan.",
                "Ask the account owner to run `gemini`, then `/auth` (e.g. Login with Google) on this computer, or choose a fallback node."),
            model, apiStatus, detail, loginFailed);
}
