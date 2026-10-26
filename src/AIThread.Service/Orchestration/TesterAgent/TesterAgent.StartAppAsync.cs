using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Testing;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // The app does not answer (reason from ProbeAsync): AIT starts it with the project's run command and waits until it
    // answers. Null when it is up (run.App holds the process when this run started it); otherwise what the developer
    // should do.
    private async Task<string?> StartAppAsync(TestRun run, string reason, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        var baseUrl = run.Test.BaseUrl!;
        var unreachable = Lang.T($"Aplikasi tidak bisa dihubungi di {baseUrl} ({reason}).", $"The application cannot be reached at {baseUrl} ({reason}).");
        var project = run.Input.Project;
        if (AppLaunchResolver.Resolve(project.RootPath, project.Modules, baseUrl, out var unknown) is not { } command)
            return $"{unreachable} {unknown}";

        await progress.StepAsync(Lang.T($"🚀 Aplikasi belum jalan, AIT menjalankannya: `{command.Display}`", $"🚀 The application is not running yet, AIT starts it: `{command.Display}`"), cancellationToken);
        recorder.Log(ActivityLevel.Info, Lang.T($"Testing: aplikasi belum jalan di {baseUrl}, dijalankan dengan {command.Display}", $"Testing: the application was not running at {baseUrl}, started with {command.Display}"));
        var started = await apps.StartAsync(command, baseUrl, cancellationToken);
        if (started.Error is not null)
        {
            // One readable sentence; what the app printed goes in its own block (FinalReport, the thread page).
            run.Report.AppCommand = command.Display;
            run.Report.AppOutput = started.Output;
            return Lang.T($"Aplikasi belum jalan di {baseUrl} dan AIT gagal menjalankannya: {started.Error}. Jalankan aplikasinya sendiri, lalu ulangi testing.", $"The application is not running at {baseUrl} and AIT failed to start it: {started.Error}. Start the application yourself, then test again.");
        }

        if (started.App is { } app)
        {
            run.App = app;
            run.Report.AppStarted = command.Display;
            await progress.StepAsync(Lang.T($"🟢 Aplikasi menjawab di {baseUrl}; dihentikan lagi setelah testing", $"🟢 The application answers at {baseUrl}; it is stopped again after testing"), cancellationToken);
        }
        return null;
    }
}
