using AIThread.Common.Enums;
using AIThread.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Business;

public sealed partial class BusinessDigestCollector
{
    private static IQueryable<Common.Entities.ThreadRun> Pending(AIThreadDbContext db, string projectKey) =>
        db.Threads.AsNoTracking().Where(t =>
            t.Status == ThreadStatus.Completed && t.BusinessDigestedAt == null && t.Project != null && t.Project.ProjectKey == projectKey);
}
