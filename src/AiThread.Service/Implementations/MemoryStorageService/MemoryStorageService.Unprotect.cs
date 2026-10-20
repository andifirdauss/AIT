using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    private string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try
        {
            return _protector.Unprotect(value);
        }
        catch (CryptographicException ex)
        {
            // Data Protection keys were rotated or deleted; the password has to be entered again.
            logger.LogWarning(ex, "A stored memory store password can no longer be decrypted.");
            return null;
        }
    }
}
