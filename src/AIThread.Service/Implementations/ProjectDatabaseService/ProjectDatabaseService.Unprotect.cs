using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectDatabaseService
{
    // Null when there is none, or when it was encrypted with a key this machine no longer has.
    private string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try
        {
            return _protector.Unprotect(value);
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning(ex, "A project database password cannot be decrypted on this machine.");
            return null;
        }
    }
}
