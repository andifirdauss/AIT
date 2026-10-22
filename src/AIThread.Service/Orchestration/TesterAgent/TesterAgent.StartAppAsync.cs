using AIThread.Common.Enums;
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
        var unreachable = $"Aplikasi tidak bisa dihubungi di {baseUrl} ({reason}).";
        var project = run.Input.Project;
        if (AppLaunchResolver.Resolve(project.RootPath, project.Modules, baseUrl, out var unknown) is not { } command)
            return $"{unreachable} {unknown}";

        await progress.StepAsync($"🚀 Aplikasi belum jalan, AIT menjalankannya: `{command.Display}`", cancellationToken);
        recorder.Log(ActivityLevel.Info, $"Testing: aplikasi belum jalan di {baseUrl}, dijalankan dengan {command.Display}");
        var started = await apps.StartAsync(command, baseUrl, cancellationToken);
        if (started.Error is not null)
            return $"{unreachable} AIT mencoba menjalankannya, tetapi {started.Error} Jalankan aplikasinya sendiri, lalu ulangi testing.";

        if (started.App is { } app)
        {
            run.App = app;
            run.Report.AppStarted = command.Display;
            await progress.StepAsync($"🟢 Aplikasi menjawab di {baseUrl}; dihentikan lagi setelah testing", cancellationToken);
        }
        return null;
    }
}
