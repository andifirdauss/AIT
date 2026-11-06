using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    // The CLI's failure as the error 9router calls raise, so NodeCaller retries a transient one and moves to a fallback
    // node when the model cannot serve (logged out, rate limited, overloaded).
    public static NineRouterException Failure(string model, int? apiStatus, string? subtype, string detail)
    {
        var shown = Shown(model);
        if (apiStatus is 401 or 403 || LoginPattern().IsMatch(detail))
        {
            return new NineRouterException(
                Lang.T($"Claude Code CLI belum login atau loginnya kedaluwarsa ({detail}). Minta pemilik akun menjalankan `claude` lalu `/login` di komputer ini, atau pilih node cadangan.",
                    $"Claude Code CLI is not logged in or its login expired ({detail}). Ask the account owner to run `claude`, then `/login` on this computer, or choose a fallback node."),
                apiStatus)
            {
                IsProviderLoginRequired = true
            };
        }

        if (apiStatus == 429 || RateLimitPattern().IsMatch(detail))
            return new NineRouterException(
                Lang.T($"Batas pemakaian akun Claude Code tercapai untuk model '{shown}': {detail}", $"The Claude Code account hit its usage limit for model '{shown}': {detail}"), 429);

        if (apiStatus is >= 500 || OverloadedPattern().IsMatch(detail))
            return new NineRouterException(
                Lang.T($"Provider Claude sedang sibuk atau gagal untuk model '{shown}': {detail}", $"The Claude provider is busy or failing for model '{shown}': {detail}"), apiStatus is >= 500 ? apiStatus : 503);

        if (subtype == "error_max_turns")
            return new NineRouterException(
                Lang.T($"Model '{shown}' lewat Claude Code CLI mencoba memakai tool bawaan CLI (dimatikan oleh AIT) dan tidak menjawab.",
                    $"Model '{shown}' through Claude Code CLI tried to use a built-in CLI tool (turned off by AIT) and gave no answer."), 502);

        if (ModelPattern().IsMatch(detail))
            return new NineRouterException(
                Lang.T($"Model '{shown}' tidak tersedia untuk akun Claude Code ini: {detail}", $"Model '{shown}' is not available for this Claude Code account: {detail}"), 404);

        return new NineRouterException(Lang.T($"Claude Code CLI gagal: {detail}", $"Claude Code CLI failed: {detail}"), apiStatus ?? 502);
    }
}
