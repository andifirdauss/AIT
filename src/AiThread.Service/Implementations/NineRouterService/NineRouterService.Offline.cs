using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class NineRouterService
{
    private static RouterStatusDto Offline(string baseUrl, bool hasKey, string error) =>
        new(false, baseUrl, null, 0, 0, hasKey, null, error, DateTime.UtcNow);
}
