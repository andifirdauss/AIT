using System.Text.RegularExpressions;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.CliBackend;

public enum CliFailureKind
{
    Login,
    RateLimit,
    Busy,
    Model,
    Other
}

// A CLI's failure as the error 9router calls raise, so NodeCaller retries a transient one and moves to a fallback node
// when the model cannot serve (logged out, rate limited, overloaded, unknown model). Shared by the CLI adapters; each
// passes its name and how its account owner logs in.
public static partial class CliFailure
{
    public static NineRouterException Map(string cli, string loginHint, string model, int? apiStatus, string detail, bool loginFailed = false)
    {
        var shown = Shown(model);
        var kind = Classify(apiStatus, detail, loginFailed);
        if (kind == CliFailureKind.Login)
        {
            return new NineRouterException(
                Lang.T($"{cli} CLI belum login atau loginnya kedaluwarsa ({detail}). {loginHint}",
                    $"{cli} CLI is not logged in or its login expired ({detail}). {loginHint}"),
                apiStatus)
            {
                IsProviderLoginRequired = true
            };
        }

        if (kind == CliFailureKind.RateLimit)
            return new NineRouterException(
                Lang.T($"Batas pemakaian akun {cli} tercapai untuk model '{shown}': {detail}", $"The {cli} account hit its usage limit for model '{shown}': {detail}"), 429);

        if (kind == CliFailureKind.Busy)
            return new NineRouterException(
                Lang.T($"Provider di balik {cli} sedang sibuk atau gagal untuk model '{shown}': {detail}", $"The provider behind {cli} is busy or failing for model '{shown}': {detail}"),
                apiStatus is >= 500 ? apiStatus : 503);

        if (kind == CliFailureKind.Model)
            return new NineRouterException(
                Lang.T($"Model '{shown}' tidak tersedia untuk akun {cli} ini: {detail}", $"Model '{shown}' is not available for this {cli} account: {detail}"), 404);

        return new NineRouterException(Lang.T($"{cli} CLI gagal: {detail}", $"{cli} CLI failed: {detail}"), apiStatus ?? 502);
    }

    public static NineRouterException Empty(string cli, string model) =>
        new(Lang.T($"Model '{Shown(model)}' lewat {cli} CLI mengembalikan jawaban kosong.", $"Model '{Shown(model)}' through {cli} CLI returned an empty answer."), 502)
        {
            IsEmptyReply = true
        };

    // Nothing to retry until someone installs it; a fallback node may still answer.
    public static NineRouterException NotInstalled(string cli, string command, string optionsSection, Exception? inner) =>
        new(Lang.T($"{cli} CLI ({command}) tidak ditemukan di komputer ini. Pasang {cli}, atau isi AIThread:{optionsSection}:Path di appsettings, lalu coba lagi.",
                $"{cli} CLI ({command}) was not found on this computer. Install {cli}, or set AIThread:{optionsSection}:Path in appsettings, then try again."),
            null, inner)
        {
            IsProviderLoginRequired = true
        };

    public static string Shown(string model) => model.Length == 0 ? "default" : model;

    // What kind of failure the status and text describe, checked in this order.
    public static CliFailureKind Classify(int? apiStatus, string detail, bool loginFailed = false)
    {
        if (loginFailed || apiStatus is 401 or 403 || LoginPattern().IsMatch(detail)) return CliFailureKind.Login;
        if (apiStatus == 429 || RateLimitPattern().IsMatch(detail)) return CliFailureKind.RateLimit;
        if (apiStatus is >= 500 || OverloadedPattern().IsMatch(detail)) return CliFailureKind.Busy;
        return ModelPattern().IsMatch(detail) ? CliFailureKind.Model : CliFailureKind.Other;
    }

    // An HTTP status written in a CLI's error text ("unexpected status 401 Unauthorized", "got status: 429"), for the
    // retry/fallback mapping.
    public static int? StatusIn(string? text) =>
        text is not null && StatusPattern().Match(text) is { Success: true } match ? int.Parse(match.Groups[1].Value) : null;

    [GeneratedRegex(@"status[:\s]+(\d{3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex StatusPattern();

    [GeneratedRegex(@"/login|/auth\b|not logged in|not signed in|log ?in again|sign ?in again|codex login|invalid api key|api key not valid|authentication|oauth token|unauthori[sz]ed|unauthenticated|credential|auth method", RegexOptions.IgnoreCase)]
    private static partial Regex LoginPattern();

    [GeneratedRegex(@"rate.?limit|usage limit|limit reached|too many requests|quota|resource.?exhausted", RegexOptions.IgnoreCase)]
    private static partial Regex RateLimitPattern();

    [GeneratedRegex(@"overloaded|internal server error|service unavailable|bad gateway|\bunavailable\b", RegexOptions.IgnoreCase)]
    private static partial Regex OverloadedPattern();

    [GeneratedRegex(@"model.{0,40}(not found|not available|not supported|invalid|does not exist|unknown)|(unknown|invalid|unsupported) model", RegexOptions.IgnoreCase)]
    private static partial Regex ModelPattern();
}
