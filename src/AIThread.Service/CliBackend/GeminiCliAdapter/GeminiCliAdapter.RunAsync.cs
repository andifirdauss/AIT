namespace AIThread.Service.CliBackend;

public sealed partial class GeminiCliAdapter
{
    public async Task<CliRunOutput> RunAsync(string model, CliPrompt prompt, CliCallLimits limits, Func<string, Task>? onDelta,
        CancellationToken cancellationToken = default)
    {
        var launch = ResolveLaunch() ?? throw NotInstalled(null);
        var slot = await _slots.AcquireAsync(cancellationToken);
        string? work = null;
        try
        {
            work = CliProcess.NewWorkFolder();
            var systemFile = Path.Combine(work, "system.md");
            await File.WriteAllTextAsync(systemFile, prompt.System.Length == 0 ? "You are a helpful assistant." : prompt.System, CliProcess.Utf8, cancellationToken);
            var settingsFolder = Path.Combine(work, ".gemini");
            Directory.CreateDirectory(settingsFolder);
            await File.WriteAllTextAsync(Path.Combine(settingsFolder, "settings.json"), WorkspaceSettings(), CliProcess.Utf8, cancellationToken);

            var reader = new GeminiStreamReader();
            var exit = await CliProcess.RunAsync(launch, BuildArguments(model), work, UserText(prompt),
                environment =>
                {
                    CleanEnvironment(environment, work);
                    environment["GEMINI_SYSTEM_MD"] = systemFile;
                },
                async line =>
                {
                    var delta = reader.Feed(line);
                    if (delta is not null && onDelta is not null) await onDelta(delta);
                },
                limits, DisplayName, NotInstalled, cancellationToken);
            return Output(model, reader, exit);
        }
        finally
        {
            slot.Dispose();
            if (work is not null) CliProcess.DeleteQuietly(work);
        }
    }
}
