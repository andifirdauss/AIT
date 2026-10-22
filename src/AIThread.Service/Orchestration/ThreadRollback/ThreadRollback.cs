using AIThread.Common.Entities;
using AIThread.Data.Context;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Orchestration;

// Undoes the file changes of a finished thread from its snapshots (FileJournal). All or nothing: when any file was
// changed again after the thread (by the developer or another thread), nothing is touched and the files are named.
// A file is safe to undo while its content is still exactly what the thread last wrote. The unit test catalog forgets
// the tests the thread added and takes back the ones it made obsolete.
public sealed partial class ThreadRollback(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IThreadRegistry registry,
    IFileLockProvider fileLocks,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ICodeStructureService codeStructure,
    IUnitTestCatalog unitTests) : IThreadRollback
{
    public const string Restore = "restore";
    public const string Delete = "delete";
    public const string Unchanged = "unchanged";
    public const string Conflict = "conflict";

    private sealed record RollbackState(ThreadRun Thread, string? Root, IReadOnlyList<ThreadFileSnapshot> Snapshots, bool Running);
}
