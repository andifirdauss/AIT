namespace AIThread.Service.Interfaces;

// Keeps the Summary memory of every module up to date from its change entries in long memory, and migrates the facts
// of older versions (Rule, Flow, Pitfall, …) into it.
public interface IModuleSummaryWriter
{
    // One pass; the number of module summaries written.
    Task<int> RunAsync(CancellationToken cancellationToken);
}
