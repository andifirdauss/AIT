using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using AIThread.Common.DTOs;

namespace AIThread.UI.Hosting;

// "AIThread.exe setup": opens the Setup page, or exports / imports a team profile through the running instance, so a
// developer can apply the team's setup from a terminal right after installing (npm i -g aithread). Secrets are typed
// here, hidden, and never read from the file.
[SupportedOSPlatform("windows")]
public static class SetupCommand
{
    public const string Usage = "Pemakaian: AIThread.exe setup [export <file> | import <file> [--yes]]";

    private static readonly Dictionary<SetupProfileSection, string> SectionLabels = new()
    {
        [SetupProfileSection.Router] = "Koneksi 9router",
        [SetupProfileSection.Orchestration] = "Proxy & orkestrasi",
        [SetupProfileSection.Memory] = "Penyimpanan memory",
        [SetupProfileSection.Skills] = "Skill",
        [SetupProfileSection.Nodes] = "Node",
        [SetupProfileSection.Business] = "Business memory"
    };

    public static async Task<int> RunAsync(int port, IReadOnlyList<string> rest, bool yes)
    {
        Console.OutputEncoding = Encoding.UTF8;
        using var client = new ServerClient(port);
        if (await client.GetInfoAsync() is null)
        {
            Console.Error.WriteLine($"AIT tidak berjalan di {client.BaseUrl}. Jalankan 'AIThread.exe tray' dulu.");
            return 1;
        }

        switch (rest.FirstOrDefault()?.ToLowerInvariant())
        {
            case null:
                Cli.OpenBrowser($"{client.BaseUrl}/Setup");
                return 0;
            case "export" when rest.Count == 2:
                return await ExportAsync(client, rest[1]);
            case "import" when rest.Count == 2:
                return await ImportAsync(client, rest[1], yes);
            default:
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }

    private static async Task<int> ExportAsync(ServerClient client, string path)
    {
        var full = Path.GetFullPath(path);
        await File.WriteAllTextAsync(full, await client.ExportProfileAsync());
        Console.WriteLine($"Profil setup ditulis ke {full}. API key dan password tidak ikut.");
        return 0;
    }

    private static async Task<int> ImportAsync(ServerClient client, string path, bool yes)
    {
        SetupProfileFile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<SetupProfileFile>(await File.ReadAllTextAsync(path), ServerClient.ProfileJson);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Profil tidak bisa dibaca: {ex.Message}");
            return 1;
        }
        if (profile is null)
        {
            Console.Error.WriteLine("File profil kosong.");
            return 1;
        }

        var preview = await client.ImportProfileAsync(new SetupProfileImportRequest(profile));
        if (!preview.Success || preview.Data is null)
        {
            Console.Error.WriteLine(preview.Message);
            return 1;
        }
        Print(preview.Data);

        var secrets = Console.IsInputRedirected ? null : AskSecrets(preview.Data.Secrets);
        if (preview.Data.Changes.Count == 0 && secrets is null)
        {
            Console.WriteLine("Tidak ada perubahan: komputer ini sudah sesuai dengan profil.");
            return 0;
        }
        if (!yes && !Confirm("Terapkan profil ini? [y/N] "))
        {
            Console.WriteLine("Dibatalkan.");
            return 1;
        }

        var result = await client.ImportProfileAsync(new SetupProfileImportRequest(profile, Secrets: secrets, DryRun: false));
        if (!result.Success || result.Data is null)
        {
            Console.Error.WriteLine(result.Message);
            return 1;
        }
        Print(result.Data);
        Console.WriteLine(result.Data.Errors.Count > 0
            ? "Sebagian belum diterapkan. Perbaiki lalu jalankan perintah yang sama lagi; bagian yang sudah diterapkan tidak berubah."
            : $"Profil diterapkan. Cek langkah yang tersisa di {client.BaseUrl}/Setup (AIThread.exe setup).");
        return result.Data.Errors.Count > 0 ? 1 : 0;
    }

    private static void Print(SetupProfileImportResultDto result)
    {
        Console.WriteLine();
        Console.WriteLine(result.Changes.Count == 0 ? "Tidak ada perubahan." : result.DryRun ? "Yang akan berubah:" : "Yang diterapkan:");
        foreach (var group in result.Changes.GroupBy(c => c.Section))
        {
            Console.WriteLine($"  {SectionLabels.GetValueOrDefault(group.Key, group.Key.ToString())}");
            foreach (var change in group) Console.WriteLine($"    - {change.Text}");
        }
        foreach (var warning in result.Warnings) Console.WriteLine($"  ! {warning}");
        foreach (var error in result.Errors) Console.Error.WriteLine($"  x {error}");
        Console.WriteLine();
    }

    // Null when nothing was typed: every stored secret is kept.
    private static SetupProfileSecrets? AskSecrets(IReadOnlyList<SetupProfileSecretDto> secrets)
    {
        if (secrets.Count == 0) return null;
        Console.WriteLine("Secret di komputer ini (tidak ada di file; ketikan disembunyikan):");
        var typed = new Dictionary<string, string?>();
        foreach (var secret in secrets)
        {
            Console.Write($"  {secret.Label} [{(secret.Stored ? "Enter = tetap memakai yang tersimpan" : "Enter = lewati")}]: ");
            typed[secret.Id] = ReadHidden();
        }
        Console.WriteLine();
        if (typed.Values.All(v => v is null)) return null;
        return new SetupProfileSecrets(typed.GetValueOrDefault("nineRouterApiKey"), typed.GetValueOrDefault("shortPassword"),
            typed.GetValueOrDefault("longPassword"), typed.GetValueOrDefault("businessPassword"));
    }

    private static string? ReadHidden()
    {
        var text = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (text.Length == 0) continue;
                text.Length--;
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                text.Append(key.KeyChar);
                Console.Write('*');
            }
        }
        Console.WriteLine();
        return text.Length == 0 ? null : text.ToString();
    }

    private static bool Confirm(string question)
    {
        if (Console.IsInputRedirected) return false;
        Console.Write(question);
        return Console.ReadLine()?.Trim().ToLowerInvariant() is "y" or "ya" or "yes";
    }
}
