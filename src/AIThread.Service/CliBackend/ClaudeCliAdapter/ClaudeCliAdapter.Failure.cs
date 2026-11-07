using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    // The CLI's failure as the error 9router calls raise (CliFailure), plus Claude Code's own "max turns" stop.
    public static NineRouterException Failure(string model, int? apiStatus, string? subtype, string detail)
    {
        if (subtype != "error_max_turns" || CliFailure.Classify(apiStatus, detail) is not (CliFailureKind.Model or CliFailureKind.Other))
        {
            return CliFailure.Map(DisplayName,
                Lang.T("Minta pemilik akun menjalankan `claude` lalu `/login` di komputer ini, atau pilih node cadangan.",
                    "Ask the account owner to run `claude`, then `/login` on this computer, or choose a fallback node."),
                model, apiStatus, detail);
        }

        var shown = CliFailure.Shown(model);
        return new NineRouterException(
            Lang.T($"Model '{shown}' lewat Claude Code CLI mencoba memakai tool bawaan CLI (dimatikan oleh AIT) dan tidak menjawab.",
                $"Model '{shown}' through Claude Code CLI tried to use a built-in CLI tool (turned off by AIT) and gave no answer."), 502);
    }
}
