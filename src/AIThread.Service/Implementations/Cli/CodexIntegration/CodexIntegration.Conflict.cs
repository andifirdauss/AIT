using AIThread.Common.Localization;

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
                    return Lang.T($"{FileName} sudah punya [{table}] buatan Anda. Hapus atau ganti namanya dulu.", $"{FileName} already has a [{table}] made by you. Delete or rename it first.");
                continue;
            }

            if (KeyLine().Match(line) is not { Success: true } key) continue;
            var name = Normalize(key.Groups["key"].Value);
            if (table is null && name is "model_providers" or "profiles")
                return Lang.T($"{FileName} menulis {name} sebagai inline table ({name} = {{ ... }}). Ubah ke bentuk [{name}.<id>] dulu.", $"{FileName} writes {name} as an inline table ({name} = {{ ... }}). Change it to the [{name}.<id>] form first.");
            var fullName = table is null ? name : $"{table}.{name}";
            if (IsOurs(fullName))
                return Lang.T($"{FileName} sudah punya {fullName} buatan Anda. Hapus atau ganti namanya dulu.", $"{FileName} already has a {fullName} made by you. Delete or rename it first.");
        }
        return null;
    }
}
