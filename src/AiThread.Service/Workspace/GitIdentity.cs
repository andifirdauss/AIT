namespace AiThread.Service.Workspace;

// The developer's own name from their global git config (user.name), the identity they already commit with. Read
// from the file directly — git is never run — and cached briefly, since it is read on every settings snapshot.
public static class GitIdentity
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private static (string? Name, DateTime ExpiresAt) _cache;

    public static string? UserName
    {
        get
        {
            var cache = _cache;
            if (cache.ExpiresAt > DateTime.UtcNow) return cache.Name;
            var name = Read();
            _cache = (name, DateTime.UtcNow + CacheLifetime);
            return name;
        }
    }

    private static string? Read()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".config");
        string[] files = [Path.Combine(home, ".gitconfig"), Path.Combine(xdg, "git", "config")];

        foreach (var file in files)
        {
            try
            {
                if (File.Exists(file) && UserNameIn(File.ReadLines(file)) is { } name) return name;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable config: try the next one.
            }
        }
        return null;
    }

    // name = ... inside the [user] section.
    public static string? UserNameIn(IEnumerable<string> lines)
    {
        var inUser = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inUser = line.Trim('[', ']').Trim().Equals("user", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inUser || line.StartsWith('#') || line.StartsWith(';')) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0 || !line[..eq].Trim().Equals("name", StringComparison.OrdinalIgnoreCase)) continue;
            var value = line[(eq + 1)..].Trim().Trim('"').Trim();
            return value.Length == 0 ? null : value.Length > 200 ? value[..200] : value;
        }
        return null;
    }
}
