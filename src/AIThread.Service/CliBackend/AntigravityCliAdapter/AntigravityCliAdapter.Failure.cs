using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.CliBackend;

public sealed partial class AntigravityCliAdapter
{
    public static NineRouterException Failure(string model, int? apiStatus, string detail) =>
        CliFailure.Map(DisplayName,
            Lang.T("Minta pemilik akun menjalankan `agy` lalu login dengan akun Google-nya di komputer ini, atau pilih node cadangan.",
                "Ask the account owner to run `agy` and sign in with their Google account on this computer, or choose a fallback node."),
            model, apiStatus, detail);
}
