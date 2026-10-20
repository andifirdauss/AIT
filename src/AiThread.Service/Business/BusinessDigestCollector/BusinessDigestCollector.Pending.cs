using AiThread.Common.Enums;
using AiThread.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Business;

public sealed partial class BusinessDigestCollector
{
    private static IQueryable<Common.Entities.ThreadRun> Pending(AiThreadDbContext db, string projectKey) =>
        db.Threads.AsNoTracking().Where(t =>
            t.Status == ThreadStatus.Completed && t.BusinessDigestedAt == null && t.Project != null && t.Project.ProjectKey == projectKey);
}
