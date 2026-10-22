using AIThread.Common.Options;
using AIThread.Service.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIThread.Tests;

// A secret guard for services built by hand in tests: its key lives in memory protection and a throwaway file.
internal static class TestSecrets
{
    public static SecretGuard Guard() => new(
        new EphemeralDataProtectionProvider(),
        Options.Create(new AIThreadOptions { SecretKeyPath = Path.Combine(Path.GetTempPath(), "aithread-tests", $"secret-mask-{Guid.NewGuid():N}.key") }),
        NullLogger<SecretGuard>.Instance);
}
