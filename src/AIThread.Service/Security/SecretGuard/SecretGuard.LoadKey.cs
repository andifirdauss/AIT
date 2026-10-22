using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    private const string KeyPurpose = "AIThread.SecretMask.v1";

    // From its file (encrypted with Data Protection), created on first use. Without a writable file it lives in memory,
    // and placeholders change on the next start (old ones then come back only from files read again).
    private byte[] LoadKey()
    {
        var path = string.IsNullOrWhiteSpace(options.Value.SecretKeyPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIThread", "secret-mask.key")
            : options.Value.SecretKeyPath;
        var protector = dataProtection.CreateProtector(KeyPurpose);

        try
        {
            if (File.Exists(path))
            {
                var stored = Convert.FromBase64String(protector.Unprotect(File.ReadAllText(path).Trim()));
                if (stored.Length >= 32) return stored;
            }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The secret placeholder key at {Path} cannot be read; a new one is created.", path);
        }

        var created = RandomNumberGenerator.GetBytes(32);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, protector.Protect(Convert.ToBase64String(created)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "The secret placeholder key cannot be saved at {Path}; it is kept in memory until AIT stops.", path);
        }
        return created;
    }
}
