namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // The plan the planner submits as final, with the batches it committed (commit_tasks) in front: merged, validated
    // and, once valid, ordered after the committed tasks on the same files. NewFolders: the folders outside the root it
    // changes that no committed batch was allowed for yet (they need the developer's permission).
    private static async Task<(ThreadPlan Plan, IReadOnlyList<string> Errors, IReadOnlyList<string> NewFolders)> ValidateFinalAsync(
        ThreadPlan submitted, PlannerInput input, CancellationToken cancellationToken)
    {
        if (input.Stream is not { HasCommitted: true } stream)
        {
            var errors = PlanValidator.Validate(submitted, input.Scope, input.AutoTesting, input.UnitTests?.Rules, Actors(input));
            return (submitted, errors, submitted.OutsideFolders ?? []);
        }

        using var _ = await stream.LockAsync(cancellationToken);
        var merged = stream.Merge(submitted, out var mergeErrors);
        if (mergeErrors.Count > 0) return (merged, mergeErrors, []);

        var problems = PlanValidator.Validate(merged, input.Scope, input.AutoTesting, input.UnitTests?.Rules, Actors(input), stream.Existence, stream.CommittedIds);
        if (problems.Count == 0) stream.Order(merged);
        var newFolders = (merged.OutsideFolders ?? []).Where(f => !stream.Folders.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
        return (merged, problems, newFolders);
    }
}
