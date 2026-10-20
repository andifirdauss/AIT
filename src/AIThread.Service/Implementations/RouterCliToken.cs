using System.Security.Cryptography;
using System.Text;

namespace AIThread.Service.Implementations;

// The "x-9r-cli-token" header 9router's own CLI sends to its dashboard API (9router src/cli/api/client.js): the first
// 16 hex characters of sha256(machine-id + "9r-cli-auth" + cli-secret), both files in 9router's data folder. With it
// the quota lookup (/api/combos, /api/providers/client, /api/usage) works while "Require login" is on, which is
// 9router's default. Only valid for a 9router on this machine under the same user, so it is never sent elsewhere.
public static class RouterCliToken
{
    public const string HeaderName = "x-9r-cli-token";
    private const string Salt = "9r-cli-auth";
    private const string AppName = "9router";

    // Resolved like 9router does: DATA_DIR, else %APPDATA%\9router on Windows and ~/.9router elsewhere.
    public static string DataDirectory(string? dataDirEnv, string? appData, string home, bool isWindows)
    {
        if (!string.IsNullOrWhiteSpace(dataDirEnv)) return dataDirEnv;
        if (!isWindows) return Path.Combine(home, "." + AppName);
        return Path.Combine(string.IsNullOrEmpty(appData) ? Path.Combine(home, "AppData", "Roaming") : appData, AppName);
    }

    public static string Compute(string machineId, string secret) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(machineId + Salt + secret)))[..16];

    // Null while 9router has not written its machine id yet (it does on its first start). A missing secret is created
    // the way 9router's CLI does it; the server reads the same file the first time a token arrives.
    public static string? Load(string dataDirectory)
    {
        var machineId = ReadTrimmed(Path.Combine(dataDirectory, "machine-id"));
        if (machineId is null) return null;

        var secretFile = Path.Combine(dataDirectory, "auth", "cli-secret");
        var secret = ReadTrimmed(secretFile);
        if (secret is null)
        {
            secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            Directory.CreateDirectory(Path.GetDirectoryName(secretFile)!);
            File.WriteAllText(secretFile, secret);
        }
        return Compute(machineId, secret);
    }

    private static string? ReadTrimmed(string path)
    {
        if (!File.Exists(path)) return null;
        var text = File.ReadAllText(path).Trim();
        return text.Length == 0 ? null : text;
    }
}
