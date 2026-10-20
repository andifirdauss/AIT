namespace AIThread.Service.Implementations.Cli;

public sealed partial class CodexIntegration
{
    // A user-written entry with our id, or an inline table we cannot add a key to, makes appending unsafe.
    public static string? Conflict(string content)
    {
        string? table = null;
        foreach (var raw in ManagedTextBlock.Outside(content))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (TableHeader().Match(line) is { Success: true } header)
            {
                table = Normalize(header.Groups["name"].Value);
                if (IsOurs(table))
                    return $"{FileName} sudah punya [{table}] buatan Anda. Hapus atau ganti namanya dulu.";
                continue;
            }

            if (KeyLine().Match(line) is not { Success: true } key) continue;
            var name = Normalize(key.Groups["key"].Value);
            if (table is null && name is "model_providers" or "profiles")
                return $"{FileName} menulis {name} sebagai inline table ({name} = {{ ... }}). Ubah ke bentuk [{name}.<id>] dulu.";
            var fullName = table is null ? name : $"{table}.{name}";
            if (IsOurs(fullName))
                return $"{FileName} sudah punya {fullName} buatan Anda. Hapus atau ganti namanya dulu.";
        }
        return null;
    }
}
