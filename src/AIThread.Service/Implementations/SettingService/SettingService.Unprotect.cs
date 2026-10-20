using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class SettingService
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
            // Data Protection keys were rotated or deleted; the secret has to be entered again.
            logger.LogWarning(ex, "A stored secret (9router API key or Telegram bot token) can no longer be decrypted.");
            return null;
        }
    }
}
