using AIThread.Common.Localization;

namespace AIThread.UI.Hosting.Chat;

// One chat command. Argument: the hint after the name ("[name]"), null when the command takes none.
public sealed record SlashCommand(string Name, string? Argument, string Description, params string[] Aliases)
{
    public string Usage => Argument is null ? Name : $"{Name} {Argument}";

    public string Summary => Aliases.Length == 0 ? Description : $"{Description} ({Lang.T("juga", "also")} {string.Join(", ", Aliases)})";
}

// The chat commands: the list behind /help and behind the menu that opens while the prompt is a single "/word".
public static class SlashCommands
{
    public static IReadOnlyList<SlashCommand> All =>
    [
        new("/help", null, Lang.T("bantuan perintah dan tombol", "help on commands and keys")),
        new("/project", Lang.T("[nama]", "[name]"), Lang.T("ganti project (tanpa nama: pilih dari daftar)", "switch project (no name: pick from the list)")),
        new("/cancel", null, Lang.T("batalkan thread terakhir yang masih berjalan", "cancel the last thread while it runs")),
        new("/retry", null, Lang.T("jalankan ulang tugas yang gagal dari thread terakhir", "run the failed tasks of the last thread again")),
        new("/open", null, Lang.T("buka halaman thread terakhir di browser", "open the page of the last thread in the browser")),
        new("/verbose", null, Lang.T("tampilkan/sembunyikan output tugas", "show/hide the task output")),
        new("/clear", null, Lang.T("hapus lampiran gambar", "remove the image attachments")),
        new("/resume", Lang.T("[id]", "[id]"), Lang.T("lanjutkan sesi chat lain (tanpa id: pilih dari daftar)", "resume another chat session (no id: pick from the list)")),
        new("/new", null, Lang.T("mulai sesi chat baru", "start a new chat session")),
        new("/exit", null, Lang.T("keluar", "quit"), "/quit"),
    ];

    // The commands for the prompt text: only "/" followed by one word without spaces opens the menu. Names (or
    // aliases) starting with the word come first, then the ones containing it.
    public static IReadOnlyList<SlashCommand> Matching(string text)
    {
        if (text.Length == 0 || text[0] != '/' || text.Any(char.IsWhiteSpace)) return [];

        var word = text[1..];
        var all = All;
        bool Starts(SlashCommand command) => Names(command).Any(n => n[1..].StartsWith(word, StringComparison.OrdinalIgnoreCase));
        bool Contains(SlashCommand command) => Names(command).Any(n => n[1..].Contains(word, StringComparison.OrdinalIgnoreCase));
        return [.. all.Where(Starts), .. all.Where(c => !Starts(c) && Contains(c))];
    }

    // What Tab writes for the command: its name, and a space when an argument can follow.
    public static string Complete(SlashCommand command) => command.Argument is null ? command.Name : command.Name + " ";

    // The command list of /help, one aligned line per command.
    public static string List()
    {
        var all = All;
        var column = all.Max(c => c.Usage.Length) + 3;
        return string.Join('\n', all.Select(c => "  " + c.Usage.PadRight(column) + c.Summary));
    }

    private static IEnumerable<string> Names(SlashCommand command) => [command.Name, .. command.Aliases];
}
