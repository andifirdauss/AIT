using AIThread.Common.DTOs;

namespace AIThread.Service.Testing;

public sealed partial class ImpersonationDetector
{
    public async Task<bool> ObserveAsync(string projectKey, ImpersonationInfo seen, string by, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(projectKey) || !seen.IsAvailable) return false;
        var current = (await store.GetAsync(projectKey, cancellationToken)).Impersonation;
        // A developer's word and a working detection win over what one test run saw.
        if (current is { Confirmed: true } or { Source: ImpersonationInfo.FromDeveloper } || current?.IsAvailable == true) return false;

        await store.SaveImpersonationAsync(projectKey, seen with { Source = ImpersonationInfo.FromTester, Confirmed = false, DetectedAt = DateTime.UtcNow, DetectedBy = by },
            by, cancellationToken);
        return true;
    }
}
