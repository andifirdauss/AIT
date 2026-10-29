using AIThread.Common.Entities;

namespace AIThread.Service.Business;

public sealed partial class BusinessEnvService
{
    // The newest revision of a page whose source threads (every revision up to it) all hold in the environment, with
    // those threads; null when even the first one does not.
    private static (BusinessPageRevision? Best, List<Guid> Threads) BestRevision(IEnumerable<BusinessPageRevision> revisions, Func<Guid, bool> holds)
    {
        BusinessPageRevision? best = null;
        var threads = new List<Guid>();
        foreach (var revision in revisions.OrderBy(r => r.Version))
        {
            var sources = ThreadIds(revision.SourceThreadIds);
            if (!sources.All(holds)) break;
            best = revision;
            threads.AddRange(sources);
        }
        return (best, threads);
    }
}
