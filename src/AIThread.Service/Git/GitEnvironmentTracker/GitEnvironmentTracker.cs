using AIThread.Data.Context;
using AIThread.Data.Memory;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Git;

// The environments of a project (ProjectEnvironment: dev = origin/develop, prod = origin/main, ...) and which AIT
// change reached which of them (ChangePresence). After a fetch, this clone's refs are compared with every shared
// ThreadChange of the project key (any developer's): a change is in an environment when the environment's history holds
// one of its commits, or the blob of every file it wrote (a squash or cherry-pick). What one AIT sees is recorded for
// all, and the business wiki's environment pages follow (IBusinessEnvService).
public sealed partial class GitEnvironmentTracker(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    LongMemoryStore longStore,
    IBusinessEnvService envPages,
    IEventBus eventBus,
    ILogger<GitEnvironmentTracker> logger) : IGitEnvironmentTracker
{
    public const int MaxEnvironments = 20;

    // Changes looked for in the environments: those shared within this period, the newest first.
    public static readonly TimeSpan LookBack = TimeSpan.FromDays(180);
    private const int MaxChanges = 1_000;

    // Changes whose files changed again in the environment are looked for blob by blob in its history: at most this
    // many per environment and pass.
    private const int MaxHistoryLookups = 50;

    private const int PathsPerCall = 100;

    // A shared change as it is looked for.
    private sealed record Candidate(Guid Id, DateTime CreatedAt, IReadOnlyList<string> Commits, IReadOnlyList<SharedFile> Files);
}
