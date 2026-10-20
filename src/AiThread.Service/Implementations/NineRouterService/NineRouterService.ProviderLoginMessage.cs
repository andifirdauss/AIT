namespace AiThread.Service.Implementations;

public sealed partial class NineRouterService
{
    private static string ProviderLoginMessage(string model, string error, string baseUrl)
    {
        var detail = ProviderMessagePattern().Matches(error).LastOrDefault()?.Groups[1].Value ?? error;
        return $"Akun provider untuk model '{model}' di 9router perlu login ulang ({detail.Trim()}). " +
               $"Sambungkan ulang akunnya di dashboard 9router ({baseUrl}), atau pasang node cadangan dengan provider lain.";
    }
}
