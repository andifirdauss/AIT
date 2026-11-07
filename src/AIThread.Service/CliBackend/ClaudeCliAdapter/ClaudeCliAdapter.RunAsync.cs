namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    public async Task<CliRunOutput> RunAsync(string model, CliPrompt prompt, CliCallLimits limits, Func<string, Task>? onDelta,
        CancellationToken cancellationToken = default)
    {
        var launch = ResolveLaunch() ?? throw NotInstalled(null);
        await _slots.WaitAsync(cancellationToken);
        string? work = null;
        try
        {
            work = CliProcess.NewWorkFolder();
            var systemFile = Path.Combine(work, "system-prompt.md");
            await File.WriteAllTextAsync(systemFile, prompt.System.Length == 0 ? "You are a helpful assistant." : prompt.System, CliProcess.Utf8, cancellationToken);

            var reader = new ClaudeStreamReader();
            var exit = await CliProcess.RunAsync(launch, BuildArguments(model, systemFile), work, InputLine(prompt) + "\n", CleanEnvironment,
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
            _slots.Release();
            if (work is not null) CliProcess.DeleteQuietly(work);
        }
    }
}
