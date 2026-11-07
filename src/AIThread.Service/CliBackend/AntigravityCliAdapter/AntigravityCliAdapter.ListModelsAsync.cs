using Microsoft.Extensions.Logging;

namespace AIThread.Service.CliBackend;

public sealed partial class AntigravityCliAdapter
{
    // What `agy models` lists for the signed-in account (no request is spent on a model), cached for a few minutes.
    // When it cannot be listed (logged out, offline), only the CLI's default model ("cli:agy") is offered.
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        if (_models is { } cached && DateTime.UtcNow - cached.CheckedAt < ModelsCacheDuration) return cached.Models;
        var launch = ResolveLaunch();
        if (launch is null) return [];

        IReadOnlyList<string> models = [string.Empty];
        string? work = null;
        try
        {
            work = CliProcess.NewWorkFolder();
            var output = new List<string>();
            var exit = await CliProcess.RunAsync(launch, ["models"], work, null, e => CleanEnvironment(e, work),
                line =>
                {
                    output.Add(line);
                    return Task.CompletedTask;
                },
                ModelsLimits, DisplayName, NotInstalled, cancellationToken);
            var listed = ParseModels(output);
            if (exit.ExitCode == 0 && listed.Count > 0) models = listed;
            else logger.LogWarning("`agy models` gave no model (exit {ExitCode}): {Detail}", exit.ExitCode, CliProcess.LastLines(exit.Stderr));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not list the Antigravity CLI models.");
        }
        finally
        {
            if (work is not null) CliProcess.DeleteQuietly(work);
        }

        _models = new ModelsCache(models, DateTime.UtcNow);
        return models;
    }
}
