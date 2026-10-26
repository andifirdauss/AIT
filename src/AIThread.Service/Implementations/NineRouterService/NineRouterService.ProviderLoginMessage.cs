using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    private static string ProviderLoginMessage(string model, string error, string baseUrl)
    {
        var detail = ProviderMessagePattern().Matches(error).LastOrDefault()?.Groups[1].Value ?? error;
        return Lang.T($"Akun provider untuk model '{model}' di 9router perlu login ulang ({detail.Trim()}). ", $"The provider account for model '{model}' in 9router needs to log in again ({detail.Trim()}). ") +
               Lang.T($"Sambungkan ulang akunnya di dashboard 9router ({baseUrl}), atau pasang node cadangan dengan provider lain.", $"Reconnect the account in the 9router dashboard ({baseUrl}), or set up a fallback node with another provider.");
    }
}
