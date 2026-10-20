using AiThread.Service.Models;

namespace AiThread.Service.Memory;

public sealed partial class CodeMapper
{
    // Files already handed to the model by another caller are awaited instead of being sent again.
    private async Task MapUnsureAsync(ProjectContext project, IReadOnlyList<string> unsure, CancellationToken cancellationToken,
        Action<int>? decided = null)
    {
        var waits = new List<Task>();
        var mine = new List<string>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        foreach (var path in unsure)
        {
            var inflightKey = project.Key + "|" + path;
            var task = _inflight.GetOrAdd(inflightKey, completion.Task);
            if (ReferenceEquals(task, completion.Task)) mine.Add(path);
            else waits.Add(task);
        }

        try
        {
            if (mine.Count > 0) await DecideAsync(project, mine, decided, cancellationToken);
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            throw;
        }
        finally
        {
            foreach (var path in mine) _inflight.TryRemove(project.Key + "|" + path, out _);
        }

        // Another caller's failure only means those files stay unmapped for now.
        foreach (var wait in waits)
        {
            try
            {
                await wait.WaitAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
            }
        }
    }
}
