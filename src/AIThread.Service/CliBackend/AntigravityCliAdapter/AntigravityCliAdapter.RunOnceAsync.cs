using Microsoft.Extensions.Logging;

namespace AIThread.Service.CliBackend;

public sealed partial class AntigravityCliAdapter
{
    private async Task<CliRunOutput> RunOnceAsync(string model, CliPrompt prompt, CliCallLimits limits, Func<string, Task>? onDelta,
        CancellationToken cancellationToken)
    {
        var launch = ResolveLaunch() ?? throw NotInstalled(null);
        await _slots.WaitAsync(cancellationToken);
        string? work = null;
        var reader = new AntigravityStreamReader();
        try
        {
            work = CliProcess.NewWorkFolder();
            var agentFolder = Path.Combine(work, ".agents", "agents", AgentName);
            Directory.CreateDirectory(agentFolder);
            await File.WriteAllTextAsync(Path.Combine(agentFolder, "agent.md"), AgentFile(prompt.System), CliProcess.Utf8, cancellationToken);
            var settingsFile = Path.Combine(work, "ait-settings.json");
            await File.WriteAllTextAsync(settingsFile, PermissionSettings(), CliProcess.Utf8, cancellationToken);

            var exit = await CliProcess.RunAsync(launch, BuildArguments(model, settingsFile), work, InputLine(prompt) + "\n",
                environment => CleanEnvironment(environment, work),
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
            // The process has exited (or was killed): the conversation it saved is AIT's own and is not kept.
            if (options.Value.AntigravityCli.DeleteConversations && reader.ConversationId is { } conversation && !ForgetConversation(conversation))
                logger.LogDebug("Antigravity conversation {Conversation} was not found to delete.", conversation);
        }
    }
}
