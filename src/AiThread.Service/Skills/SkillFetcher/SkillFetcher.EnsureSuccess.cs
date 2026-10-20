using System.Net;

namespace AiThread.Service.Skills;

public sealed partial class SkillFetcher
{
    private static void EnsureSuccess(HttpResponseMessage response, string url)
    {
        if (response.IsSuccessStatusCode) return;
        var hint = response.StatusCode switch
        {
            HttpStatusCode.NotFound => " — link tidak ditemukan atau repository privat",
            HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests => " — dibatasi oleh server (rate limit), coba lagi nanti",
            _ => string.Empty
        };
        throw new HttpRequestException($"Unduh {url} gagal: HTTP {(int)response.StatusCode}{hint}.", null, response.StatusCode);
    }
}
