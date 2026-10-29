using System.Text;
using AIThread.Common.Localization;
using AIThread.Service.Diagnostics;

namespace AIThread.UI.Hosting;

// "AIThread.exe logs export [file] [--days n]": the diagnostic ZIP of the Log menu from a terminal. Through the running
// instance when there is one (activity log from the database included); otherwise straight from the folders
// (incidents and run logs only), so a client whose AIT no longer starts can still send its logs.
public static class LogsCommand
{
    public static string Usage => Lang.T("Pemakaian: AIThread.exe logs [-f] | logs export [file.zip] [--days n]",
        "Usage: AIThread.exe logs [-f] | logs export [file.zip] [--days n]");

    public static async Task<int> ExportAsync(int port, IReadOnlyList<string> rest)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var days = 3;
        string? path = null;
        for (var i = 1; i < rest.Count; i++)
        {
            if (rest[i] == "--days")
            {
                if (i + 1 >= rest.Count || !int.TryParse(rest[++i], out days) || days is < 1 or > 90)
                {
                    Console.Error.WriteLine(Lang.T("--days butuh angka 1-90.", "--days needs a number 1-90."));
                    return 2;
                }
            }
            else if (path is null) path = rest[i];
            else
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }
        }

        var full = Path.GetFullPath(path ?? DiagnosticBundle.FileName(DateTime.Now));
        using var client = new ServerClient(port);
        if (await client.GetInfoAsync() is not null)
        {
            try
            {
                await using var download = await client.ExportDiagnosticsAsync(days);
                await using var file = File.Create(full);
                await download.CopyToAsync(file);
                Console.WriteLine(Lang.T($"Log diagnostik ({days} hari terakhir) ditulis ke {full}. Secret sudah disamarkan.",
                    $"Diagnostic logs (last {days} days) written to {full}. Secrets are masked."));
                return 0;
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine(Lang.T($"Export lewat server gagal ({ex.Message}); membaca folder log langsung.",
                    $"Export through the server failed ({ex.Message}); reading the log folders directly."));
            }
        }

        await using (var file = File.Create(full))
        {
            await DiagnosticBundle.WriteAsync(file, new DiagnosticBundleInput(IncidentFolders.Resolve(null, "diagnostics"), AppPaths.LogDir, days,
                Incidents: true, RunLogs: true, ThreadId: null, ActivityText: null, Source: "cli-offline"), CancellationToken.None);
        }
        Console.WriteLine(Lang.T($"AIT tidak berjalan: ZIP berisi insiden dan file log saja (tanpa log aktivitas database), ditulis ke {full}.",
            $"AIT is not running: the ZIP holds the incidents and log files only (no database activity log), written to {full}."));
        return 0;
    }
}
