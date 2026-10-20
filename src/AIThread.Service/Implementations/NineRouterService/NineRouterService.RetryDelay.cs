using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    // Null means "do not retry here": the upstream asked for a longer wait than is worth blocking on,
    // so the caller should fail over to another model instead.
    public static TimeSpan? RetryDelay(NineRouterException ex, int attempt)
    {
        if (ex.RetryAfter is { } requested)
            return requested <= MaxRetryAfter ? requested : null;

        // Rate limits usually clear on a scale of seconds to a minute; other transient errors retry sooner.
        var baseSeconds = ex.IsRateLimited ? 5 * attempt : 2 * attempt;
        return TimeSpan.FromSeconds(baseSeconds + Random.Shared.NextDouble() * baseSeconds / 2);
    }
}
