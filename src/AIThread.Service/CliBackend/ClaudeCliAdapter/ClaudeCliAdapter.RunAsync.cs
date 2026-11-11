namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    public async Task<CliRunOutput> RunAsync(string model, CliPrompt prompt, CliCallLimits limits, Func<string, Task>? onDelta,
        CancellationToken cancellationToken = default)
    {
        var launch = ResolveLaunch() ?? throw NotInstalled(null);
        var slot = await _slots.AcquireAsync(cancellationToken);
        string? work = null;
        try
        {
            var key = WarmKey(launch, model, prompt.System);
            var warm = _warm.Take(key);
            WarmUp(launch, model, prompt.System, key);

            var input = InputLine(prompt) + "\n";
            var reader = new ClaudeStreamReader();
            if (warm is not null)
            {
                work = warm.WorkFolder;
                var warmExit = await CliProcess.RunStartedAsync(warm.Process, input, Lines(reader, onDelta), limits, DisplayName, cancellationToken, Answered(reader));
                if (reader.SawResult || reader.Text.Length > 0) return Output(model, reader, warmExit);

                // It died while it waited and answered nothing: this call starts its own.
                CliProcess.DeleteQuietly(work);
                reader = new ClaudeStreamReader();
            }

            work = CliProcess.NewWorkFolder();
            var systemFile = await WriteSystemFileAsync(work, prompt.System, cancellationToken);
            var exit = await CliProcess.RunAsync(launch, BuildArguments(model, systemFile), work, input, CleanEnvironment,
                Lines(reader, onDelta), limits, DisplayName, NotInstalled, cancellationToken, Answered(reader));
            return Output(model, reader, exit);
        }
        finally
        {
            slot.Dispose();
            if (work is not null) CliProcess.DeleteQuietly(work);
        }
    }
}
