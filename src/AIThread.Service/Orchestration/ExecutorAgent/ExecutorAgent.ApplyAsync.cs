using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class ExecutorAgent
{
    private async Task<(ExecutorOutcome? Outcome, IReadOnlyList<string> Errors)> ApplyAsync(
        ExecutorJob job, string fullPath, ParsedEdit parsed, CancellationToken cancellationToken)
    {
        // Other tasks may have edited this file since the executor read it; re-read under the lock.
        using var _ = await fileLocks.AcquireAsync(fullPath, cancellationToken);
        var current = await TextFileIo.ReadAsync(fullPath, cancellationToken);
        var (blocks, unknown) = RestoreSecrets(job, current, parsed.Blocks);
        if (unknown.Count > 0)
            return (null, [$"The blocks use secret placeholder(s) AIT cannot write back: {string.Join(", ", unknown.Distinct())}. Copy placeholders exactly as they appear in the current content, and never invent one."]);

        var applied = EditApplier.Apply(current, blocks);
        if (!applied.Success) return (null, applied.Errors);

        var journal = job.Journal;
        if (journal is not null) await journal.BeforeWriteAsync(fullPath);
        await TextFileIo.WriteAsync(fullPath, applied.Content, current, CancellationToken.None);
        if (journal is not null) await journal.AfterWriteAsync(fullPath);
        // The diff keeps the placeholders: it is shown in the report and stored with the thread.
        var diff = DiffBuilder.Build(job.Task.File, parsed.Blocks, applied.CreatesFile);
        var summary = parsed.Summary ?? $"{(applied.CreatesFile ? "Membuat" : "Mengubah")} {job.Task.File} ({parsed.Blocks.Count} blok).";
        return (new ExecutorOutcome(true, summary, diff, null, 0, 0), []);
    }
}
