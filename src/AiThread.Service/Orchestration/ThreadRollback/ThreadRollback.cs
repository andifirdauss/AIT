using AiThread.Common.Entities;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Orchestration;

// Undoes the file changes of a finished thread from its snapshots (FileJournal). All or nothing: when any file was
// changed again after the thread (by the developer or another thread), nothing is touched and the files are named.
// A file is safe to undo while its content is still exactly what the thread last wrote.
public sealed partial class ThreadRollback(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IThreadRegistry registry,
    IFileLockProvider fileLocks,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ICodeStructureService codeStructure) : IThreadRollback
{
    public const string Restore = "restore";
    public const string Delete = "delete";
    public const string Unchanged = "unchanged";
    public const string Conflict = "conflict";

    private sealed record RollbackState(ThreadRun Thread, string? Root, IReadOnlyList<ThreadFileSnapshot> Snapshots, bool Running);
}
