using System.Net;
using AIThread.Common.Localization;

namespace AIThread.Service.Skills;

public sealed partial class SkillFetcher
{
    private static void EnsureSuccess(HttpResponseMessage response, string url)
    {
        if (response.IsSuccessStatusCode) return;
        var hint = response.StatusCode switch
        {
            HttpStatusCode.NotFound => Lang.T(" — link tidak ditemukan atau repository privat", " — link not found or private repository"),
            HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests => Lang.T(" — dibatasi oleh server (rate limit), coba lagi nanti", " — limited by the server (rate limit), try again later"),
            _ => string.Empty
        };
        throw new HttpRequestException(Lang.T($"Unduh {url} gagal: HTTP {(int)response.StatusCode}{hint}.", $"Downloading {url} failed: HTTP {(int)response.StatusCode}{hint}."), null, response.StatusCode);
    }
}
