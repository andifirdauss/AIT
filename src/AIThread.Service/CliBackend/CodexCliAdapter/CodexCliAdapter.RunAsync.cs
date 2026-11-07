namespace AIThread.Service.CliBackend;

public sealed partial class CodexCliAdapter
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
            var instructions = Path.Combine(work, "instructions.md");
            await File.WriteAllTextAsync(instructions, prompt.System.Length == 0 ? "You are a helpful assistant." : prompt.System, CliProcess.Utf8, cancellationToken);
            var images = prompt.Images.Select((image, index) => CliProcess.WriteImage(work, image, index)).ToList();
            var ignoreUserConfig = options.Value.CodexCli.IgnoreUserConfig || RoutesToAit();

            var reader = new CodexStreamReader();
            var exit = await CliProcess.RunAsync(launch, BuildArguments(model, instructions, images, ignoreUserConfig), work,
                string.IsNullOrWhiteSpace(prompt.User) ? "Continue." : prompt.User, CleanEnvironment,
                line =>
                {
                    reader.Feed(line);
                    return Task.CompletedTask;
                },
                limits, DisplayName, NotInstalled, cancellationToken);
            var output = Output(model, reader, exit);
            // Codex sends the message whole: it is streamed at once.
            if (onDelta is not null) await onDelta(output.Text);
            return output;
        }
        finally
        {
            _slots.Release();
            if (work is not null) CliProcess.DeleteQuietly(work);
        }
    }
}
