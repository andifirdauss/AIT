namespace AIThread.Service.Security;

// Files whose whole content is a credential: private keys and certificates, key stores, SSH keys, token files of
// package managers and clouds, and the folders that hold them in the user's profile (AIT's own data included). They
// are never read for a model, nor written by an executor; a project can allow one back in .aithread/project.json
// ("secrets": { "allow": [...] }) or deny more ("deny": [...]), see SecretPolicy.
public static class SecretPaths
{
    // Exact names (lower case) and extensions of credential files, wherever they are.
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519", "id_ecdsa_sk", "id_ed25519_sk", ".git-credentials", ".npmrc", ".pypirc",
        ".netrc", "_netrc", ".htpasswd", ".pgpass", "secrets.json", "auth.json", ".credentials.json", "credentials.json",
        ".dockercfg", ".s3cfg", ".boto", "client_secret.json", "service-account.json", "keystore.properties"
    };

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pem", ".key", ".pfx", ".p12", ".jks", ".keystore", ".kdbx", ".ppk", ".snk", ".p8", ".mobileprovision"
    };

    // Folder names that only ever hold keys, wherever they are.
    private static readonly HashSet<string> Folders = new(StringComparer.OrdinalIgnoreCase) { ".ssh", ".gnupg", ".aws" };

    // Folders of this machine's profile that hold credentials: clouds, Windows secret stores, 9router's provider logins
    // and AIT's own data (database, keys, logs).
    private static readonly Lazy<string[]> ProfileFolders = new(() =>
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] relative =
        [
            Path.Combine(home, ".azure"), Path.Combine(home, ".kube"), Path.Combine(home, ".docker"), Path.Combine(home, ".config", "gcloud"),
            Path.Combine(home, ".9router"), Path.Combine(home, ".codex"), Path.Combine(home, ".omp"),
            Path.Combine(roaming, "9router"), Path.Combine(roaming, "gcloud"), Path.Combine(roaming, "Microsoft", "UserSecrets"),
            Path.Combine(roaming, "Microsoft", "Protect"), Path.Combine(roaming, "Microsoft", "Credentials"),
            Path.Combine(roaming, "Microsoft", "Crypto"), Path.Combine(roaming, "Microsoft", "SystemCertificates"),
            Path.Combine(local, "Microsoft", "Credentials"), Path.Combine(local, "AIThread"), Path.Combine(local, "ASP.NET", "DataProtection-Keys")
        ];
        return relative.Where(p => p.Length > 3).Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).ToArray();
    });

    private static long _blocked;

    // Reads (and writes) refused since AIT started.
    public static long Blocked => Interlocked.Read(ref _blocked);

    // The message the planner, the tools nodes and the executors get instead of the file.
    public static string Refusal(string path) =>
        $"'{path}' is a protected credential file (AIT secret policy): its content is never read for a model or changed by AIT. " +
        "Work with the setting names instead, and ask the developer when the value itself matters.";

    // fullPath: normalized absolute path. policy: the project's own rules (null outside a project).
    public static bool IsProtected(string fullPath, string? root, SecretPolicy? policy)
    {
        var relative = root is not null && IsUnder(fullPath, root)
            ? Path.GetRelativePath(root, fullPath).Replace('\\', '/')
            : null;
        if (relative is not null && policy is not null)
        {
            if (policy.Allows(relative)) return false;
            if (policy.Denies(relative)) return true;
        }

        var name = Path.GetFileName(fullPath);
        if (Names.Contains(name) || Extensions.Contains(Path.GetExtension(name))) return true;

        var segments = fullPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(Folders.Contains)) return true;
        return ProfileFolders.Value.Any(folder => IsUnder(fullPath, folder));
    }

    public static void CountBlocked() => Interlocked.Increment(ref _blocked);

    private static bool IsUnder(string fullPath, string folder) =>
        string.Equals(fullPath, folder, StringComparison.OrdinalIgnoreCase)
        || fullPath.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || fullPath.StartsWith(folder + '/', StringComparison.OrdinalIgnoreCase);
}
